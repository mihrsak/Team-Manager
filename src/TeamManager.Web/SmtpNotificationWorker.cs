using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

public sealed class SmtpNotificationWorker(IConfiguration config, IServiceScopeFactory scopes,
    IStringLocalizer<UiText> text, ILogger<SmtpNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = config.GetSection("Smtp");
        if (!settings.GetValue<bool>("Enabled")) return;
        if (string.IsNullOrWhiteSpace(settings["Host"]) || !MailAddress.TryCreate(settings["From"],out var from) ||
            !Uri.TryCreate(settings["BaseUrl"],UriKind.Absolute,out var baseUrl) || baseUrl.Scheme!=Uri.UriSchemeHttps ||
            baseUrl.UserInfo.Length!=0 || baseUrl.Query.Length!=0 || baseUrl.Fragment.Length!=0)
        {
            logger.LogWarning("SMTP delivery requires a host, sender and HTTPS base URL.");
            return;
        }

        var port = settings.GetValue("Port",587);
        if (port is < 1 or > 65535)
        {
            logger.LogWarning("SMTP delivery has an invalid port.");
            return;
        }

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(settings["ReminderTimeZone"] ?? "UTC"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("SMTP delivery has an invalid reminder time zone.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.GetValue("PollSeconds",30),1,300));
        var retryBase = Math.Clamp(settings.GetValue("RetryBaseSeconds",60),1,3600);
        var root = baseUrl.AbsoluteUri.TrimEnd('/');
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<EmailDispatchStore>();
                var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).Date;
                await dispatch.QueueReminders(today,stoppingToken);
                for (var i=0;i<10;i++)
                {
                    var watched = await dispatch.Claim(false,stoppingToken);
                    var reminder = await dispatch.Claim(true,stoppingToken);
                    if (watched is null && reminder is null) break;
                    if (watched is not null) await Deliver(dispatch,watched,today,from,root,settings,port,retryBase,stoppingToken);
                    if (reminder is not null) await Deliver(dispatch,reminder,today,from,root,settings,port,retryBase,stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                var code = ex is Microsoft.Data.SqlClient.SqlException sql ? sql.Number : (int?)null;
                logger.LogWarning("Email queue processing failed ({FailureType}, SQL code {SqlCode}).",ex.GetType().Name,code);
            }

            await Task.Delay(interval,stoppingToken);
        }
    }

    private async Task Deliver(EmailDispatchStore dispatch, EmailJob job, DateTime today, MailAddress from,
        string root, IConfiguration settings, int port, int retryBase, CancellationToken stoppingToken)
    {
        var delivery = job.DueDate is null || job.DueDate == today ? await dispatch.Resolve(job,stoppingToken) : null;
        var failed = false;
        if (delivery is not null)
        {
            try
            {
                using var smtp = new SmtpClient(settings["Host"]!,port) { EnableSsl=settings.GetValue("UseTls",true) };
                if (settings["Username"] is { Length: > 0 } username)
                    smtp.Credentials = new NetworkCredential(username,settings["Password"]);

                var previous = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(delivery.Language=="en"?"en":"hr");
                    var subject = job.DueDate is null ? "Aktivnost na praćenom zadatku" : "Zadatak dospijeva danas";
                    using var message = new MailMessage(from,new MailAddress(delivery.Address))
                    {
                        Subject=text[subject].Value,
                        Body=text["Za pregled se prijavi u Team Manager: {0}",$"{root}/Issue?id={delivery.IssueId}"].Value
                    };
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    await smtp.SendMailAsync(message,timeout.Token);
                }
                finally { CultureInfo.CurrentUICulture=previous; }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested &&
                ex is SmtpException or OperationCanceledException or FormatException or InvalidOperationException)
            {
                failed = true;
                logger.LogWarning("SMTP attempt {Attempt} failed ({FailureType}).",job.Attempts+1,ex.GetType().Name);
            }
        }
        await dispatch.Complete(job,failed,retryBase*(1<<job.Attempts),stoppingToken);
    }
}
