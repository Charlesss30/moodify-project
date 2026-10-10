using mood_recommendation.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver.Linq;
using MongoDB.Driver;
using mood_recommendation.Data;
namespace mood_recommendation.Controllers;

[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin")]
public class DashboardController(MongoStore db, ExternalCatalog external) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Summary()
    {
        var groups=await db.LichSuTamTrangs.GroupBy(x=>x.TamTrangID).Select(g=>new { id=g.Key,count=g.Count() }).ToListAsync();
        var names=(await db.TamTrangs.ToListAsync()).ToDictionary(x=>x.TamTrangID,x=>x.TenTamTrang);
        var ratingCount=await db.DanhGias.CountAsync();
        return Ok(new {
            users=await db.TaiKhoans.CountAsync(),activeUsers=await db.TaiKhoans.CountAsync(x=>x.TrangThai),movies=await db.Phims.CountAsync()+await external.Count("Movie"),music=await db.Nhacs.CountAsync()+await external.Count("Song"),genres=await db.TheLoais.CountAsync(),
            moodEntries=await db.LichSuTamTrangs.CountAsync(),ratings=ratingCount,
            averageRating=ratingCount==0?(double?)null:await db.DanhGias.Select(x=>(double)x.SoSao).AverageAsync(),
            moods=groups.Select(g=>new {name=names.GetValueOrDefault(g.id),g.count})
        });
    }

    [HttpGet("feedback"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Feedback([FromQuery] int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var ratings = await db.DanhGias.OrderByDescending(x=>x.ThoiGian).ThenByDescending(x=>x.DanhGiaID)
            .Skip((page-1)*50).Take(50).ToListAsync();
        var ids = ratings.Select(x=>x.NoiDungID).Distinct().ToArray();
        var contents = (await db.Hydrate(await db.NoiDungGiaiTris.Where(x=>ids.Contains(x.NoiDungID)).ToListAsync()))
            .ToDictionary(x=>x.NoiDungID);
        foreach (var id in ids.Where(id=>!contents.ContainsKey(id)))
        {
            var item = await external.Get(id, HttpContext.RequestAborted);
            if (item?.MetadataStatus == "available") contents[id] = item;
        }
        var accountIds = ratings.Select(x=>x.TaiKhoanID).Distinct().ToArray();
        var accounts = (await db.TaiKhoans.Where(x=>accountIds.Contains(x.TaiKhoanID)).ToListAsync()).ToDictionary(x=>x.TaiKhoanID,x=>x.TenDangNhap);
        return Ok(new { total=await db.DanhGias.CountAsync(), page, pageSize=50,
            items=ratings.Select(x=> {
                var c=contents.GetValueOrDefault(x.NoiDungID);
                return new {x.DanhGiaID,x.NoiDungID,x.TaiKhoanID,x.SoSao,x.NhanXet,x.ThoiGian,
                    username=accounts.GetValueOrDefault(x.TaiKhoanID),
                    content=c==null?null:new {id=c.NoiDungID,title=c.TieuDe,imageUrl=c.HinhAnh,type=c.LoaiNoiDung,
                        artist=c.Nhac?.TenNgheSi,album=c.Nhac?.Album,genre=c.Nhac?.Genre??c.Phim?.TheLoai}};
            }) });
    }
}
