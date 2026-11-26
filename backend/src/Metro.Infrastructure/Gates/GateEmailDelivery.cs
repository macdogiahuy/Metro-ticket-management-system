using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Gates;

public sealed class GateEmailOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? From { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
}
public sealed class GateEmailProtection(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector=provider.CreateProtector("Metro.GateEmailOutbox.v1");
    public string Protect(GateEmailMessage message)=>protector.Protect(JsonSerializer.Serialize(message));
    public GateEmailMessage Unprotect(string payload)=>JsonSerializer.Deserialize<GateEmailMessage>(protector.Unprotect(payload))!;
}
public sealed class SmtpGateEmailSender(IOptions<GateEmailOptions> options):IGateEmailSender
{
    public async Task SendAsync(GateEmailMessage value,CancellationToken ct)
    {
        var o=options.Value;
        if(string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.From))throw new GateException("EMAIL_NOT_CONFIGURED",503);
        using var client=new SmtpClient(o.Host,o.Port){EnableSsl=o.EnableSsl,Timeout=15000};
        if(!string.IsNullOrWhiteSpace(o.UserName))client.Credentials=new NetworkCredential(o.UserName,o.Password);
        using var message=new MailMessage(o.From,value.Recipient){Subject=value.Subject,Body=value.Body,IsBodyHtml=false,BodyEncoding=Encoding.UTF8,SubjectEncoding=Encoding.UTF8};
        message.Headers.Add("X-Metro-Event-Id",value.DeliveryId.ToString());
        await client.SendMailAsync(message,ct);
    }
}
