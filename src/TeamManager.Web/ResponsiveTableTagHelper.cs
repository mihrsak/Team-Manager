using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

[HtmlTargetElement("table")]
public sealed class ResponsiveTableTagHelper(IStringLocalizer<UiText> text) : TagHelper
{
    [HtmlAttributeName("scroll-label")] public string? ScrollLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var classes=(output.Attributes["class"]?.Value?.ToString()??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
        // Planning grids and the issue time table already have their own widths and scrolling.
        if(classes.Intersect(new[]{"gantt","deadline-calendar","issue-time-table"}).Any()) return;
        output.Attributes.RemoveAll("scroll-label");
        output.Attributes.SetAttribute("class",string.Join(' ',classes.Append("responsive-table")));
        var region=new TagBuilder("div");
        region.Attributes["class"]="table-scroll responsive-table-scroll";
        region.Attributes["role"]="region";
        region.Attributes["tabindex"]="0";
        region.Attributes["aria-label"]=text["{0}: vodoravno pomicanje",ScrollLabel??text["Tablica podataka"].Value];
        output.PreElement.AppendHtml(region.RenderStartTag());
        output.PostElement.AppendHtml(region.RenderEndTag());
    }
}
