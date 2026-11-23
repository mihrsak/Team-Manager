using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public sealed class TimeEntryModel(IssueStore store):PageModel
{
    [BindProperty(SupportsGet=true)]public int IssueId{get;set;}
    [BindProperty(SupportsGet=true)]public int Id{get;set;}
    [BindProperty]public string Token{get;set;}="";
    [BindProperty,DataType(DataType.Date)]public DateTime Date{get;set;}
    [BindProperty,Required]public string Hours{get;set;}="";
    [BindProperty,Required,StringLength(60)]public string Activity{get;set;}="";
    [BindProperty,StringLength(1000)]public string? Note{get;set;}
    public bool CanCorrect{get;private set;}
    public EditableTime Entry{get;private set;}=null!;
    public ReturnNavigation? Navigation=>ReturnNavigation.Get(HttpContext);
    public string BackUrl=>Navigation?.Url??Url.Page("/TimeReport",new{ProjectId=Entry.ProjectId})!;
    public string BackLabel=>Navigation?.Label??"Natrag na izvještaj vremena";
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<EditableTime?> Load()
    {
        Response.Headers.CacheControl="private, no-store";
        var row=await store.TimeEntryFor(UserId,IssueId,Id);if(row is null)return null;
        Entry=row;
        CanCorrect=await store.Can(UserId,row.ProjectId,"time.log")&&(row.AuthorId==UserId||await store.CanMove(UserId,row.ProjectId));return row;
    }
    public async Task<IActionResult> OnGetAsync()
    {
        var row=await Load();if(row is null)return NotFound();Token=row.Token;Date=row.Date;Hours=row.Hours.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);Activity=row.Activity;Note=row.Note;return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if(await Load() is null)return NotFound();if(!CanCorrect)return Forbid();
        var parsed=decimal.TryParse(Hours?.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var hours);
        if(!parsed)ModelState.AddModelError(nameof(Hours),"Unesi valjan datum i broj sati.");if(!ModelState.IsValid)return Page();
        try{await store.CorrectTime(UserId,IssueId,Id,Token,Date,hours,Activity,Note);TempData["Notice"]="Unos vremena je ispravljen.";return Navigation is {} navigation?LocalRedirect(navigation.Url):RedirectToPage("/Issue",new{id=IssueId});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException){ModelState.AddModelError("",ex.Message);return Page();}
    }
}
