using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class IssueModel(IssueStore store,WorkflowStore workflow,IssueLinkStore links,RoadmapStore roadmap,IssuePersonalStore personal,IssueAttachmentStore attachments,IssueFieldStore fields):PageModel
{
    public FieldValueDraft? FieldDraft { get; private set; }
    [BindProperty]public IssueInput Input{get;set;}=new();
    [BindProperty(SupportsGet=true)] public string? ReturnUrl {get;set;}
    public string BackUrl => ReturnUrl is not null && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : Url.Page("/Issues",new{ProjectId})!;
    public string TimeReturnUrl(int time)=>Url.Page("/Issue",new{Id,ReturnUrl})+"#time-"+time;
    public string BackLabel=>ReturnNavigation.Get(HttpContext) is {} navigation&&navigation.Label!="Natrag na zadatke"?navigation.Label:"← Projektni zadaci";
    public int? Id{get;set;}
    public int ProjectId{get;set;}
    public string ProjectName{get;set;}="";
    public Dictionary<int,string> HistoryProjects {get;private set;}=[];
    public List<Team> Assignees{get;set;}=[];
    public List<string> Types{get;set;}=[];public List<string> Statuses{get;set;}=[];public List<string> Priorities{get;set;}=[];
    public List<IssueEvent> Events{get;set;}=[];
    public List<TimeEntry> Time{get;set;}=[];
    public bool CanSetPrivacy {get;private set;}
    public bool CanEdit{get;set;}
    public bool CanComment{get;set;}
    public bool CanLogTime{get;set;} public bool ShowTime{get;set;}
    public List<IssueField> CustomFields {get;private set;}=[];
    public List<AttachmentInfo> Attachments {get;private set;}=[];
    public List<PrivateIssueNote> PrivateNotes {get;private set;}=[];
    public bool CanAddPrivateNote {get;private set;}
    public bool IsWatching {get;private set;}
    public bool HasTimeDraft {get;private set;}
    public string TimeDraftDate {get;private set;}=DateTime.Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    public string? TimeDraftHours {get;private set;}
    public string? TimeDraftActivity {get;private set;}
    public string? TimeDraftNote {get;private set;}
    public List<RoadmapItem> RoadmapItems {get;set;}=[];
    public List<IssueLink> Links {get;set;}=[];
    public List<Team> CopyProjects {get;set;}=[];
    public List<Team> MoveProjects {get;set;}=[];
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task<IActionResult> OnGetAsync(int? id,int projectId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Id=id;
        if(ReturnUrl is not null && !Url.IsLocalUrl(ReturnUrl)) ReturnUrl=null;
        Types=await workflow.Values("type");Statuses=await workflow.Values("status");Priorities=await workflow.Values("priority");
        if (id is int selected)
        {
            var issue = await store.Get(UserId, selected);
            if (issue is null) return NotFound();
            ProjectId = issue.ProjectId;
            Input = issue.Input;
            CanSetPrivacy = issue.CreatorId == UserId || await store.CanMove(UserId, ProjectId);
            try
            {
                var activity = await store.Activity(UserId, selected);
                Events = activity.Events;
                Time = activity.Time;
            }
            catch (UnauthorizedAccessException) { return NotFound(); }
        }
        else ProjectId=projectId;
        var visibleProjects=await store.Projects(UserId);HistoryProjects=visibleProjects.ToDictionary(p=>p.Id,p=>p.Name);
        var project=visibleProjects.FirstOrDefault(x=>x.Id==ProjectId);if(project is null)return NotFound();ProjectName=project.Name;Assignees=await store.Assignees(ProjectId);
        RoadmapItems = await roadmap.List(ProjectId);
        CanEdit=await store.Can(UserId,ProjectId,id is null?"issues.create":"issues.edit");CanComment=await store.Can(UserId,ProjectId,"issues.comment");ShowTime=await store.Can(UserId,ProjectId,"time.view");CanLogTime=await store.Can(UserId,ProjectId,"time.log");if(id is null&&!CanEdit)return Forbid();
        if (id is not null)
        {
            CustomFields = await fields.ForIssue(UserId,ProjectId,id.Value);
            Attachments = await attachments.List(UserId, id.Value, ProjectId);
            PrivateNotes = await personal.Notes(UserId, id.Value, ProjectId);
            CanAddPrivateNote = await personal.CanAddNote(ProjectId);
            IsWatching = await store.IsWatching(UserId, id.Value);
            Links = await links.List(UserId, id.Value, ProjectId);
            var canMove = CanEdit && await store.CanMove(UserId, ProjectId);
            foreach (var target in await store.TransferProjects(UserId))
            {
                CopyProjects.Add(new(target.Id,target.Name));
                if (canMove && target.Id != ProjectId && target.CanMove)
                    MoveProjects.Add(new(target.Id,target.Name));
            }
        }
        Types=await workflow.Selectable("type",id is null?null:Input.Type);
        Statuses=await workflow.Selectable("status",id is null?null:Input.Status);
        Priorities=await workflow.Selectable("priority",id is null?null:Input.Priority);
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAsync(int? id,int projectId)
    {
        var submitted=Input;var result=await OnGetAsync(id,projectId);if(result is not PageResult)return result;Input=submitted;
        if(!CanEdit)return Forbid();if(!ModelState.IsValid)return Page();
        try{var saved=await store.Save(UserId,ProjectId,id,Input);TempData["Notice"]="Zadatak je spremljen.";return RedirectToPage(new{id=saved,returnUrl=ReturnUrl});}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostCommentAsync(int id,string? text)
    {
        ModelState.Clear();var result=await OnGetAsync(id,0);if(result is not PageResult)return result;
        try{await store.Comment(UserId,id,text??"");return RedirectToPage(new{id,returnUrl=ReturnUrl});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostTimeAsync(int id,DateTime? date,string? hours,string? note,string? activity)
    {
        HasTimeDraft=true;TimeDraftDate=Request.Form["date"].ToString();TimeDraftHours=Request.Form["hours"].ToString();TimeDraftActivity=Request.Form["activity"].ToString();TimeDraftNote=Request.Form["note"].ToString();
        var parsed=decimal.TryParse(hours?.Replace(',','.'),System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value);
        var valid=date is not null&&parsed&&!ModelState.Any(x=>x.Key=="date" && x.Value!.Errors.Count>0);
        ModelState.Clear();var result=await OnGetAsync(id,0);if(result is not PageResult)return result;if(!valid){ModelState.AddModelError("","Unesi valjan datum i broj sati.");return Page();}
        try{await store.LogTime(UserId,id,date!.Value,value,note,activity);return RedirectToPage(new{id,returnUrl=ReturnUrl});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException ex){ModelState.AddModelError("",ex.Message);return Page();}
    }
    public async Task<IActionResult> OnPostTransferAsync(int id, int? revision, int destination, bool move)
    {
        if (id <= 0 || destination <= 0 || revision is null or < 0 ||
            ModelState.GetValidationState(nameof(move)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
            return BadRequest();
        try
        {
            var saved = await store.Transfer(UserId, id, revision.Value, destination, move);
            TempData["Notice"] = move ? "Zadatak je premješten." : "Zadatak je kopiran.";
            return RedirectToPage(new { id = saved, returnUrl=ReturnUrl });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            var result = await OnGetAsync(id, 0);
            if (result is not PageResult) return result;
            ModelState.AddModelError("", ex.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostLinkAsync(int id, int otherId, string? relation, bool remove)
    {
        if (id <= 0 || otherId <= 0 ||
            ModelState.GetValidationState(nameof(remove)) == Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Invalid)
            return BadRequest();
        var (kind, outgoing) = relation switch
        {
            "blockedBy" => ("blocks", false),
            "subtaskOf" => ("parent", false),
            _ => (relation ?? "", true)
        };
        string error;
        try
        {
            await links.Set(UserId, id, otherId, kind, outgoing, remove);
            return RedirectToPage(new { id, returnUrl=ReturnUrl });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 51015 or 51016 or 51017)
        {
            error = ex.Number switch
            {
                51016 => "Podzadatak već ima nadređeni zadatak.",
                51017 => "Ova bi veza stvorila ciklus.",
                _ => "Veze se trenutačno uređuju. Pokušaj ponovno."
            };
        }

        ModelState.Clear();
        var result = await OnGetAsync(id, 0);
        if (result is not PageResult) return result;
        ModelState.AddModelError("", error);
        return Page();
    }

    public async Task<IActionResult> OnPostWatchAsync(int id, bool? watch)
    {
        if (id <= 0 || watch is null) return BadRequest();
        try { await store.SetWatching(UserId, id, watch.Value); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        return RedirectToPage(new { id, returnUrl=ReturnUrl });
    }

    public async Task<IActionResult> OnPostPrivateNoteAsync(int id, string? text)
    {
        if (id <= 0) return BadRequest();
        try { await personal.AddNote(UserId, id, text); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex)
        {
            ModelState.Clear();
            var result = await OnGetAsync(id, 0);
            if (result is not PageResult) return result;
            ModelState.AddModelError("", ex.Message);
            return Page();
        }
        return RedirectToPage(new { id, returnUrl=ReturnUrl });
    }

    public async Task<IActionResult> OnGetAttachmentAsync(int id, int attachmentId)
    {
        if (id <= 0 || attachmentId <= 0) return NotFound();
        var attachment = await attachments.Download(UserId, id, attachmentId);
        if (attachment is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(attachment.Content, "application/octet-stream", attachment.Name);
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, IFormFile? attachment)
    {
        if (id <= 0) return BadRequest();
        try { await attachments.Add(UserId, id, attachment, HttpContext.RequestAborted); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex)
        {
            ModelState.Clear();
            var result = await OnGetAsync(id, 0);
            if (result is not PageResult) return result;
            ModelState.AddModelError("", ex.Message);
            return Page();
        }
        Response.Headers.Location = Url.Page("/Issue", new { id, returnUrl=ReturnUrl });
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    public async Task<IActionResult> OnPostRemoveAttachmentAsync(int id, int attachmentId)
    {
        if (id <= 0 || attachmentId <= 0) return NotFound();
        try
        {
            if (!await attachments.Remove(UserId, id, attachmentId)) return NotFound();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        Response.Headers.Location = Url.Page("/Issue", new { id, returnUrl=ReturnUrl });
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    public async Task<IActionResult> OnPostSetFieldAsync(int id, int fieldId, int? revision, string? fieldValue, bool resolveConflict = false, int? reviewedRevision = null)
    {
        if (id <= 0 || fieldId <= 0 || revision is null or < 0) return BadRequest();
        if (!ListFilterValidation.Valid(ModelState,"id","fieldId","revision","reviewedRevision","resolveConflict") || resolveConflict && reviewedRevision is null or < 0) return BadRequest();
        var attemptedRevision = resolveConflict ? reviewedRevision!.Value : revision.Value;
        try { await fields.SetValue(UserId, id, fieldId, attemptedRevision, fieldValue); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ModelState.Clear();
            var result = await OnGetAsync(id, 0);
            if (result is not PageResult) return result;
            if (!CanEdit || !CustomFields.Any(f => f.Id == fieldId)) return Forbid();
            FieldDraft = new(fieldId, attemptedRevision, Request.Form["fieldValue"].ToString());
            ModelState.AddModelError("", ex.Message);
            return Page();
        }
        return RedirectToPage(new { id, returnUrl=ReturnUrl });
    }

    public IActionResult OnPost()=>BadRequest();
}
