using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using mood_recommendation.Data;
using mood_recommendation.DTOs;
using mood_recommendation.Models;
using mood_recommendation.Services;
namespace mood_recommendation.Controllers;
[ApiController, Route("api/[controller]"), Authorize]
public class RecommendationController(MongoStore db, ExternalCatalog external) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Recommend(RecommendationDto dto)
    {
        var mood=await db.Find<TamTrang>(dto.TamTrangID);
        if(mood==null)return NotFound(new {message="Mood not found."});
        var v=(mood.MinValence+mood.MaxValence)/2;var a=(mood.MinArousal+mood.MaxArousal)/2;
        var query=db.NoiDungGiaiTris;
        if(dto.LoaiNoiDung=="Movie")query=query.Where(x=>x.LoaiNoiDung=="Movie");
        if(dto.LoaiNoiDung=="Music")query=query.Where(x=>x.LoaiNoiDung=="Song");
        var items=await db.Hydrate(await query.OrderBy(x=>(x.Valence-v)*(x.Valence-v)+(x.Arousal-a)*(x.Arousal-a)).ThenBy(x=>x.NoiDungID).Take(dto.Limit).ToListAsync());
        var remote=new List<NoiDungGiaiTri>();
        if(dto.LoaiNoiDung!="Music") remote.AddRange(await external.List("Movie",HttpContext.RequestAborted));
        // ID-only songs have no trustworthy mood label; never substitute fake zero coordinates.
        items=items.Concat(remote.Where(x=>x.MoodAvailable && x.MetadataStatus=="available")).OrderBy(x=>MoodScoring.DistanceSquared(x.Valence,x.Arousal,v,a)).ThenBy(x=>x.NoiDungID).Take(dto.Limit).ToList();
        await db.Insert(new LichSuTamTrang{TaiKhoanID=int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),TamTrangID=mood.TamTrangID,Valence=v,Arousal=a,ThoiGian=DateTime.UtcNow});
        return Ok(new {algorithm="mood-euclidean-v1",mood=mood.TenTamTrang,valence=v,arousal=a,
            items=items.Select(x=>new {content=new{x.NoiDungID,x.TieuDe,x.LoaiNoiDung,x.HinhAnh,x.MoTa,x.Valence,x.Arousal,
                artist=x.Nhac?.TenNgheSi,duration=x.Nhac?.Duration,album=x.Nhac?.Album,canPreview=false,playbackMode=x.Nhac?.Source=="Spotify"?"embed":null,sourceUrl=x.Nhac?.SourceUrl},
                match=MoodScoring.MatchPercent(MoodScoring.DistanceSquared(x.Valence,x.Arousal,v,a))})});
    }
}
