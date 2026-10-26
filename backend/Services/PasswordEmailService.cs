using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace mood_recommendation.Services;

public sealed record PasswordEmail(string Recipient,string Code);
public sealed class PasswordEmailService(IConfiguration config,IHostEnvironment env,
    ILogger<PasswordEmailService> log) : BackgroundService
{
    private readonly Channel<PasswordEmail> queue=Channel.CreateBounded<PasswordEmail>(
        new BoundedChannelOptions(100) {FullMode=BoundedChannelFullMode.Wait,SingleReader=true});
    private bool TestPickup => env.IsDevelopment() && config.GetValue<bool>("Testing:Enabled")
        && (config["Mongo:Database"]??"").StartsWith("moodify_test_",StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(config["Email:PickupDirectory"]);
    public bool IsConfigured => TestPickup || (!string.IsNullOrWhiteSpace(config["Email:Host"])
        && !string.IsNullOrWhiteSpace(config["Email:Username"])
        && !string.IsNullOrWhiteSpace(config["Email:Password"])
        && !string.IsNullOrWhiteSpace(config["Email:From"]));
    public bool Enqueue(string recipient,string code) => queue.Writer.TryWrite(new(recipient,code));
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken)) {
            var message=new MimeMessage();
            message.From.Add(new MailboxAddress("Moodify", config["Email:From"]??"test@moodify.invalid"));
            message.To.Add(MailboxAddress.Parse(email.Recipient));
            message.Subject="Mã đặt lại mật khẩu Moodify";
            message.Body=new TextPart("plain") { Text=$"Mã xác nhận của bạn: {email.Code}\n\nMã có hiệu lực trong 10 phút và chỉ dùng một lần. Nếu bạn không yêu cầu, hãy bỏ qua thư này." };
            for(var attempt=0;attempt<2 && !stoppingToken.IsCancellationRequested;attempt++) {
                try {
                    if(TestPickup) {
                        var folder=Path.GetFullPath(config["Email:PickupDirectory"]!);
                        Directory.CreateDirectory(folder);
                        await message.WriteToAsync(Path.Combine(folder,Guid.NewGuid().ToString("N")+".eml"),stoppingToken);
                    } else {
                        using var smtp=new SmtpClient {Timeout=10000};
                        await smtp.ConnectAsync(config["Email:Host"]!,config.GetValue("Email:Port",587),
                            SecureSocketOptions.StartTls,stoppingToken);
                        await smtp.AuthenticateAsync(config["Email:Username"]!,config["Email:Password"]!,stoppingToken);
                        await smtp.SendAsync(message,stoppingToken);
                        await smtp.DisconnectAsync(true,stoppingToken);
                    }
                    break;
                } catch(Exception error) when(!stoppingToken.IsCancellationRequested) {
                    // No OTP, mailbox, credential or SMTP transcript is logged.
                    log.LogWarning("Password email delivery failed ({ErrorType}).",error.GetType().Name);
                    if(attempt==0)await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);
                }
            }
        }
    }
}
