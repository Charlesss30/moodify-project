using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/[controller]"), Authorize]
public class RecommendationController(
    MongoStore db,
    ExternalCatalog external,
    MoodAnalysisService analysisService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Recommend(RecommendationDto dto)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (dto.Valence.HasValue != dto.Arousal.HasValue
            || (dto.Source == "ai_va" && (!dto.Valence.HasValue || !dto.Arousal.HasValue || string.IsNullOrWhiteSpace(dto.ModelVersion))))
            return BadRequest(new { message = "AI VA recommendations require both valence and arousal in the range -1 to 1." });
        var mood=await db.Find<TamTrang>(dto.TamTrangID);
        if(mood==null)return NotFound(new {message="Mood not found."});
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { dto.TamTrangID, dto.Valence, dto.Arousal, dto.Source, dto.ModelVersion, dto.LoaiNoiDung, dto.Limit, dto.Balanced, dto.Mode }))));
        var mappingVersion = string.IsNullOrWhiteSpace(dto.AnalysisId) ? null
            : (await analysisService.GetMappingAsync(HttpContext.RequestAborted)).Version;
        var moodConfigurationHash = MoodAnalysisService.MoodConfigurationHash(mood);
        if (!string.IsNullOrWhiteSpace(dto.AnalysisId))
        {
            var previous = await db.LichSuTamTrangs.FirstOrDefaultAsync(
                x => x.AnalysisId == dto.AnalysisId && x.TaiKhoanID == userId);
            if (previous is not null)
            {
                if (previous.AnalysisExpiresAt <= DateTime.UtcNow
                    || previous.RecommendationRequestHash != requestHash
                    || previous.MappingVersion != mappingVersion
                    || previous.TamTrangID != mood.TamTrangID
                    || previous.MoodConfigHash != moodConfigurationHash
                    || string.IsNullOrWhiteSpace(previous.RecommendationResponseJson))
                    return Conflict(new { message = "This analysis was already used, expired, or its mood configuration changed." });
                return Content(previous.RecommendationResponseJson, "application/json");
            }
        }
        using var reservation = analysisService.TryReserveRecommendation(
            dto.AnalysisId, userId, dto.TamTrangID, moodConfigurationHash, requestHash, mappingVersion);
        if (reservation is null)
            return Conflict(new { message = "This analysis is invalid, expired, belongs to another account, or is already being processed." });
        if (reservation.CachedResponse is not null) return Ok(reservation.CachedResponse);
        var provenance = reservation.Provenance;

        var v=dto.Valence ?? (mood.MinValence+mood.MaxValence)/2;
        var a=dto.Arousal ?? (mood.MinArousal+mood.MaxArousal)/2;
        var query=db.NoiDungGiaiTris;
        if(dto.LoaiNoiDung=="Movie")query=query.Where(x=>x.LoaiNoiDung=="Movie");
        if(dto.LoaiNoiDung=="Music")query=query.Where(x=>x.LoaiNoiDung=="Song");
        var balanced = dto.Balanced && dto.LoaiNoiDung == null;
        var ranked = query.OrderBy(x=>(x.Valence-v)*(x.Valence-v)+(x.Arousal-a)*(x.Arousal-a)).ThenBy(x=>x.NoiDungID);
        var local = balanced
            ? (await ranked.Where(x=>x.LoaiNoiDung=="Movie").Take((dto.Limit+1)/2).ToListAsync())
                .Concat(await ranked.Where(x=>x.LoaiNoiDung=="Song").Take(dto.Limit/2).ToListAsync()).ToList()
            : await ranked.Take(dto.Limit).ToListAsync();
        if (dto.Mode != "mood") local = await query.ToListAsync();
        var items = await db.Hydrate(local);
        var remote=new List<NoiDungGiaiTri>();
        if(dto.LoaiNoiDung!="Music") remote.AddRange(await external.List("Movie",HttpContext.RequestAborted));
        // ID-only songs have no trustworthy mood label; never substitute fake zero coordinates.
        var candidates = items.Concat(remote.Where(x=>x.MoodAvailable && x.MetadataStatus=="available"))
            .OrderBy(x=>MoodScoring.DistanceSquared(x.Valence,x.Arousal,v,a)).ThenBy(x=>x.NoiDungID);
        IReadOnlyList<RankedContent>? personalized = null;
        if (dto.Mode != "mood") personalized = await new HybridRecommender(db).RankAsync(candidates.ToList(), userId, v, a, dto.Mode);
        var ordered = personalized is null ? candidates.AsEnumerable() : personalized.Select(x => x.Content);
        items = balanced
            ? ordered.Where(x=>x.LoaiNoiDung=="Movie").Take((dto.Limit+1)/2)
                .Concat(ordered.Where(x=>x.LoaiNoiDung=="Song").Take(dto.Limit/2)).ToList()
            : ordered.Take(dto.Limit).ToList();
        var algorithm = dto.Mode == "mood" ? "mood-euclidean-v1" : dto.Mode == "content" ? "content-feedback-v1" : "hybrid-feedback-v1";
        var ranking = personalized?.ToDictionary(x => x.Content.NoiDungID);
        var recommendationId = Guid.NewGuid().ToString("N");
        var response = new {recommendationId,algorithm,mood=mood.TenTamTrang,valence=v,arousal=a,
            items=items.Select(x=>new {content=new{recommendationId,x.NoiDungID,x.TieuDe,x.LoaiNoiDung,x.HinhAnh,x.MoTa,x.Valence,x.Arousal,
                director=x.Phim?.Director,artist=x.Nhac?.TenNgheSi,duration=x.Nhac?.Duration,album=x.Nhac?.Album,canPreview=false,playbackMode=x.Nhac?.Source=="Spotify"?"embed":null,sourceUrl=x.Nhac?.SourceUrl},
                score=ranking?.GetValueOrDefault(x.NoiDungID)?.Score,
                contentScore=ranking?.GetValueOrDefault(x.NoiDungID)?.ContentScore,
                collaborativeScore=ranking?.GetValueOrDefault(x.NoiDungID)?.CollaborativeScore,
                match=MoodScoring.MatchPercent(MoodScoring.DistanceSquared(x.Valence,x.Arousal,v,a))})};
        var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var history = new LichSuTamTrang{RecommendationId=recommendationId,TaiKhoanID=userId,TamTrangID=mood.TamTrangID,Valence=v,Arousal=a,ThoiGian=DateTime.UtcNow,
            Source=provenance?.DecisionSource ?? dto.Source,
            AnalyzerVersion=dto.Source == "ai_va" ? dto.ModelVersion : provenance?.ModelVersion,
            Algorithm=algorithm,
            AnalysisId=provenance?.AnalysisId,
            MappingVersion=provenance?.MappingVersion,AnalysisSource=provenance?.AnalysisSource,
            MoodConfigHash=moodConfigurationHash,AnalysisExpiresAt=provenance?.ExpiresAt,
            RecommendationRequestHash=provenance is null ? null : requestHash,
            RecommendationResponseJson=provenance is null ? null : responseJson,
            DecisionSource=provenance?.DecisionSource ?? dto.Source,
            RecommendedItems=items.Select(x=>new RecommendationSnapshot {NoiDungID=x.NoiDungID,TieuDe=x.TieuDe,LoaiNoiDung=x.LoaiNoiDung,HinhAnh=x.HinhAnh}).ToList()};
        try
        {
            await db.Insert(history);
        }
        catch (MongoWriteException error) when (
            provenance is not null && error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var previous = await db.LichSuTamTrangs.FirstOrDefaultAsync(
                x => x.AnalysisId == provenance.AnalysisId && x.TaiKhoanID == userId);
            if (previous?.TamTrangID == mood.TamTrangID
                && previous.RecommendationRequestHash == requestHash
                && previous.MappingVersion == mappingVersion
                && previous.MoodConfigHash == moodConfigurationHash
                && previous.AnalysisExpiresAt > DateTime.UtcNow
                && !string.IsNullOrWhiteSpace(previous.RecommendationResponseJson))
                return Content(previous.RecommendationResponseJson, "application/json");
            throw;
        }
        reservation.Complete(response, mood.TamTrangID, requestHash);
        return Ok(response);
    }
}
