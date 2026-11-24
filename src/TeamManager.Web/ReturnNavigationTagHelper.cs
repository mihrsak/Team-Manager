using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using TeamManager.Web.Pages;

namespace TeamManager.Web;

[HtmlTargetElement("a")]
[HtmlTargetElement("form")]
public sealed class ReturnNavigationTagHelper(IUrlHelperFactory helpers):TagHelper
{
    public override int Order=>int.MaxValue;
    [ViewContext,HtmlAttributeNotBound]public ViewContext ViewContext{get;set;}=null!;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        var helper=helpers.GetUrlHelper(ViewContext);
        var navigation=ReturnNavigation.Get(ViewContext.HttpContext)??(ViewContext.ViewData.Model switch
        {
            SearchModel search=>ReturnNavigation.Create(helper,search.ReturnUrl),
            CalendarModel calendar=>ReturnNavigation.Create(helper,calendar.ReturnUrl),
            GanttModel gantt=>ReturnNavigation.Create(helper,gantt.ReturnUrl),
            ActivityModel activity=>ReturnNavigation.Create(helper,activity.ReturnUrl),
            TimeReportModel report=>ReturnNavigation.Create(helper,report.PageUrl(report.PageNumber)),_=>null
        });
        if(navigation is null)return;
        var attribute=output.TagName=="a"?"href":"action";
        // Razor expression attributes can already contain HTML entities, whereas
        // MVC-generated attributes contain plain URL text. Normalize once before
        // replacing the attribute, whose new value is encoded by Razor again.
        var target=System.Net.WebUtility.HtmlDecode(output.Attributes[attribute]?.Value?.ToString())??(output.TagName=="form"?ViewContext.HttpContext.Request.Path.Value:null);
        if(target is null||target==navigation.Url||!helper.IsLocalUrl(target)||!ReturnNavigation.Supports(ReturnNavigation.PagePath(helper.Content(target)!,ViewContext.HttpContext)))return;
        if(output.TagName=="a")
        {
            // The issue link in time details is the same destination as the back
            // link. Reuse its full context instead of nesting an issue inside itself.
            var targetQuery=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(target.Split('#')[0].Split('?',2).ElementAtOrDefault(1));
            var originQuery=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(navigation.Url.Split('#')[0].Split('?',2).ElementAtOrDefault(1));
            var sameIssue=ReturnNavigation.PagePath(target,ViewContext.HttpContext).Equals("/Issue",StringComparison.OrdinalIgnoreCase)&&
                ReturnNavigation.PagePath(navigation.Url,ViewContext.HttpContext).Equals("/Issue",StringComparison.OrdinalIgnoreCase)&&
                targetQuery.Count==1&&targetQuery.TryGetValue("id",out var targetId)&&targetId.Count==1&&
                originQuery.TryGetValue("id",out var originId)&&originId.Count==1&&targetId[0]==originId[0];
            output.Attributes.SetAttribute("href",sameIssue?navigation.Url:ReturnNavigation.Append(target,navigation));
        }
        else
        {
            var hidden=new TagBuilder("input");hidden.TagRenderMode=TagRenderMode.SelfClosing;
            hidden.Attributes["type"]="hidden";hidden.Attributes["name"]="ReturnUrl";hidden.Attributes["value"]=navigation.Url;
            output.PostContent.AppendHtml(hidden);
        }
    }
}
