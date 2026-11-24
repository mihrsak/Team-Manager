using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using TeamManager.Web.Pages;

namespace TeamManager.Web;

[HtmlTargetElement("a", Attributes="navigation")]
public sealed class ActiveNavigationTagHelper : TagHelper
{
    [HtmlAttributeName("navigation")] public string Area { get; set; }="";
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; }=null!;
    public override int Order=>int.MaxValue-1;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("navigation");
        output.Attributes.RemoveAll("aria-current");
        if(ViewContext.HttpContext.Response.StatusCode>=400) return;
        var page=ViewContext.RouteData.Values["page"]?.ToString();
        var section=Area=="administration"?page:Primary(page);
        var target=System.Net.WebUtility.HtmlDecode(output.Attributes["href"]?.Value?.ToString());
        if(section is not null && target is not null &&
            ReturnNavigation.PagePath(target,ViewContext.HttpContext).Equals(section,StringComparison.OrdinalIgnoreCase))
            output.Attributes.SetAttribute("aria-current","page");
    }

    private string? Primary(string? page)=>page switch
    {
        "/Issue"=>"/Issues", "/TimeEntry"=>"/TimeReport", "/Gantt"=>"/Calendar",
        "/Project" or "/Roadmap" or "/Wiki" or "/ProjectDocuments" or "/ProjectNews" or "/Forum" or
            "/Repositories" or "/RepositoryHistory" or "/IssueFields" or "/MailIntake"=>"/Projects",
        "/ApiTokens" or "/EmailNotifications"=>"/Profile",
        "/RegistrationRequests" or "/Groups"=>"/Users",
        "/Profile" when ViewContext.ViewData.Model is ProfileModel person=>person.IsSelf?"/Profile":"/Users",
        "/CustomFields" when ViewContext.ViewData.Model is CustomFieldsModel fields=>fields.Scope switch
        {
            "project"=>"/Projects", "time"=>"/TimeReport",
            _=>fields.EntityId is int id && id.ToString()!=ViewContext.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)?"/Users":"/Profile"
        },
        "/NotFound" or "/Denied" or "/Error"=>null,
        _=>page
    };
}
