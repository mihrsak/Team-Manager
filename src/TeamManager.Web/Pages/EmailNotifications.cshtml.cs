using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TeamManager.Web.Pages;

public sealed class EmailNotificationsModel(EmailNotificationStore notifications, IConfiguration configuration, Microsoft.Extensions.Localization.IStringLocalizer<UiText> text) : PageModel
{
    public EmailNotificationPreferences Settings { get; private set; } = new(null, false, false);
    public bool HasDraft { get; private set; }
    public string? DraftAddress { get; private set; }
    public bool DraftSendWatchedIssues { get; private set; }
    public bool DraftSendDueReminders { get; private set; }
    public bool DeliveryEnabled => configuration.GetValue<bool>("Smtp:Enabled");
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "private, no-store";
        Settings = await notifications.Get(UserId);
    }

    public async Task<IActionResult> OnPostAsync(string? address, bool sendWatchedIssues, bool sendDueReminders)
    {
        HasDraft = true;
        DraftAddress = Request.Form["address"].ToString();
        DraftSendWatchedIssues = sendWatchedIssues;
        DraftSendDueReminders = sendDueReminders;
        address = address?.Trim();
        if (string.IsNullOrEmpty(address)) address = null;
        if (!ModelState.IsValid || (address is null && (sendWatchedIssues || sendDueReminders)) ||
            (address is not null && (address.Length > 254 || address.Any(char.IsControl) ||
              !MailAddress.TryCreate(address, out var email) || !email.Address.Equals(address, StringComparison.Ordinal))))
        {
            await OnGetAsync();
            ModelState.AddModelError("", text["Unesi valjanu adresu e-pošte za obavijesti."]);
            return Page();
        }

        await notifications.Save(UserId, address, sendWatchedIssues, sendDueReminders);
        TempData["Notice"] = "Postavke e-pošte su spremljene.";
        return RedirectToPage();
    }
}
