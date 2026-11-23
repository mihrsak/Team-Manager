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
            TimeReportModel report=>ReturnNavigation.Create(helper,report.PageUrl(report.PageNumber)),_=>null
        });
        if(navigation is null)return;
        var attribute=output.TagName=="a"?"href":"action";
        // Razor expression attributes can already contain HTML entities, whereas
        // MVC-generated attributes contain plain URL text. Normalize once before
        // replacing the attribute, whose new value is encoded by Razor again.
        var target=System.Net.WebUtility.HtmlDecode(output.Attributes[attribute]?.Value?.ToString())??(output.TagName=="form"?ViewContext.HttpContext.Request.Path.Value:null);
        if(target is null||target==navigation.Url||!helper.IsLocalUrl(target)||!ReturnNavigation.Supports(ReturnNavigation.PagePath(helper.Content(target)!,ViewContext.HttpContext)))return;
        if(output.TagName=="a")output.Attributes.SetAttribute("href",ReturnNavigation.Append(target,navigation));
        else
        {
            var hidden=new TagBuilder("input");hidden.TagRenderMode=TagRenderMode.SelfClosing;
            hidden.Attributes["type"]="hidden";hidden.Attributes["name"]="ReturnUrl";hidden.Attributes["value"]=navigation.Url;
            output.PostContent.AppendHtml(hidden);
        }
    }
}
