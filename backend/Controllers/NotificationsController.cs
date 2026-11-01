using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using mood_recommendation.Services;

namespace mood_recommendation.Controllers;

[ApiController,Route("api/Notifications"),Authorize]
public sealed class NotificationsController(PushNotificationService push):ControllerBase
{
    private int AccountId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private object View(PushPreferences p)=>new {
        p.Enabled,p.DailyMoodReminder,p.WeeklySummary,p.Hour,p.Minute,p.OffsetMinutes,
        deliveryAvailable=push.IsConfigured
    };
    [HttpGet("settings")]
    public async Task<IActionResult> Get(CancellationToken ct)=>Ok(View(await push.GetAsync(AccountId,ct)));
    [HttpPut("settings")]
    public async Task<IActionResult> Save(NotificationSettingsDto dto,CancellationToken ct) {
        if(dto.Enabled && !push.IsConfigured)
            return StatusCode(503,new{message="Thông báo chưa sẵn sàng. Hãy thử lại sau."});
        var value=new PushPreferences{AccountId=AccountId,Enabled=dto.Enabled,
            DailyMoodReminder=dto.DailyMoodReminder,WeeklySummary=dto.WeeklySummary,
            Hour=dto.Hour,Minute=dto.Minute,OffsetMinutes=dto.OffsetMinutes};
        await push.SaveAsync(value,ct);
        return Ok(View(value));
    }
    [HttpPost("devices")]
    public async Task<IActionResult> Register(PushDeviceDto dto,CancellationToken ct) {
        if(!push.IsConfigured)return StatusCode(503,new{message="Thông báo chưa sẵn sàng."});
        if(!(await push.GetAsync(AccountId,ct)).Enabled)
            return BadRequest(new{message="Hãy bật nhận thông báo trước."});
        await push.RegisterAsync(AccountId,dto.Fid,ct);
        return Ok(new{registered=true});
    }
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister(PushDeviceDto dto,CancellationToken ct) {
        await push.UnregisterAsync(AccountId,dto.Fid,ct);
        return NoContent();
    }
    [HttpPost("test"),EnableRateLimiting("push-test")]
    public async Task<IActionResult> Test(CancellationToken ct) {
        if(!push.IsConfigured)return StatusCode(503,new{message="Thông báo chưa sẵn sàng."});
        if(!(await push.GetAsync(AccountId,ct)).Enabled)
            return BadRequest(new{message="Hãy bật nhận thông báo trước."});
        var count=await push.SendAsync(AccountId,"test",null,ct);
        return count>0?Ok(new{accepted=count,message="Đã gửi yêu cầu thông báo thử."})
            :BadRequest(new{message="Chưa gửi được thông báo. Hãy kiểm tra quyền nhận và kết nối thiết bị."});
    }
}
public sealed class NotificationSettingsDto {
    public bool Enabled {get;set;}
    public bool DailyMoodReminder {get;set;}=true;
    public bool WeeklySummary {get;set;}
    [Range(0,23)] public int Hour {get;set;}=20;
    [Range(0,59)] public int Minute {get;set;}
    [Range(-840,840)] public int OffsetMinutes {get;set;}=420;
}
public sealed class PushDeviceDto {
    [Required,StringLength(256,MinimumLength=20)] public string Fid {get;set;}="";
}
