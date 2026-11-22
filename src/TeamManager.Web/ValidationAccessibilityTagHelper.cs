using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TeamManager.Web;

[HtmlTargetElement("div", Attributes="asp-validation-summary")]
public sealed class ValidationAccessibilityTagHelper : TagHelper
{
    public override int Order => int.MaxValue;
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext {get;set;} = null!;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (ViewContext.ViewData.ModelState.IsValid) return;
        if (!output.Attributes.ContainsName("id")) output.Attributes.SetAttribute("id","validation-errors");
        ViewContext.ViewData["ValidationSummaryId"]=output.Attributes["id"].Value?.ToString();
        output.Attributes.SetAttribute("role","alert");
        output.Attributes.SetAttribute("tabindex","-1");
        output.Attributes.SetAttribute("data-validation-focus","");
    }
}

[HtmlTargetElement("input", Attributes="asp-for")]
[HtmlTargetElement("select", Attributes="asp-for")]
[HtmlTargetElement("textarea", Attributes="asp-for")]
public sealed class InvalidFieldAccessibilityTagHelper : TagHelper
{
    public override int Order => int.MaxValue;
    [HtmlAttributeName("asp-for")] public ModelExpression For {get;set;} = null!;
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext {get;set;} = null!;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        var name=ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        if (!ViewContext.ViewData.ModelState.TryGetValue(name,out var entry) || entry.Errors.Count==0) return;
        output.Attributes.SetAttribute("aria-invalid","true");
        var described=output.Attributes["aria-describedby"]?.Value?.ToString();
        var summary=ViewContext.ViewData["ValidationSummaryId"]?.ToString()??"validation-errors";
        output.Attributes.SetAttribute("aria-describedby",string.IsNullOrEmpty(described)?summary:described+" "+summary);
    }
}
