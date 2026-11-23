using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using TeamManager.Web.Pages;

namespace TeamManager.Web;

public sealed record ReturnNavigation(string Url,string Label)
{
    private static readonly HashSet<string> Pages=new(StringComparer.OrdinalIgnoreCase)
        {"/Issue","/Project","/Roadmap","/Wiki","/ProjectDocuments","/ProjectNews","/Forum","/Profile","/TimeEntry","/CustomFields","/IssueFields","/Repositories","/RepositoryHistory"};
    public static bool Supports(string? page)=>page is not null&&Pages.Contains(page);
    public static ReturnNavigation? Get(HttpContext context)=>context.Items[typeof(ReturnNavigation)] as ReturnNavigation;
    public static string PagePath(string url,HttpContext context)
    {
        var path=url.Split('?', '#')[0];
        var prefix=context.Request.PathBase.Value;
        if(!string.IsNullOrEmpty(prefix)&&path.StartsWith(prefix+"/",StringComparison.OrdinalIgnoreCase))path=path[prefix.Length..];
        return path;
    }
    public static ReturnNavigation? Create(IUrlHelper helper,string? value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>4096||!helper.IsLocalUrl(value))return null;
        var url=helper.Content(value)!;
        var label=PagePath(url,helper.ActionContext.HttpContext).ToLowerInvariant() switch
        {
            "/search"=>"Natrag na pretragu","/timereport"=>"Natrag na izvještaj vremena",
            "/issues"=>"Natrag na zadatke","/projects"=>"Natrag na projekte",_=>"Natrag"
        };
        return new(url,label);
    }
    public static string Append(string url,ReturnNavigation navigation)
        =>QueryHelpers.ParseQuery(url.Split('#')[0].Split('?',2).ElementAtOrDefault(1)).ContainsKey("ReturnUrl")?url:QueryHelpers.AddQueryString(url,"ReturnUrl",navigation.Url);
}

public sealed class ReturnNavigationFilter(IUrlHelperFactory helpers):IPageFilter
{
    public void OnPageHandlerSelected(PageHandlerSelectedContext context){}
    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if(!ReturnNavigation.Supports(context.RouteData.Values["page"]?.ToString()))return;
        var request=context.HttpContext.Request;
        var values=request.HasFormContentType&&request.Form.ContainsKey("ReturnUrl")?request.Form["ReturnUrl"]:request.Query["ReturnUrl"];
        var navigation=values.Count==1?ReturnNavigation.Create(helpers.GetUrlHelper(context),values[0]):null;
        context.HttpContext.Items[typeof(ReturnNavigation)]=navigation;
        if(context.HandlerInstance is IssueModel issue)issue.ReturnUrl=navigation?.Url;
    }
    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
        var navigation=ReturnNavigation.Get(context.HttpContext);if(navigation is null)return;
        if(context.Result is RedirectToPageResult redirect&&ReturnNavigation.Supports(redirect.PageName??context.RouteData.Values["page"]?.ToString()))
        {
            redirect.RouteValues??=new();redirect.RouteValues["ReturnUrl"]=navigation.Url;
        }
        else if(context.Result is StatusCodeResult {StatusCode:303}&&context.HttpContext.Response.Headers.Location.ToString() is {Length:>0} location)
        {
            var helper=helpers.GetUrlHelper(context);
            if(helper.IsLocalUrl(location)&&ReturnNavigation.Supports(ReturnNavigation.PagePath(location,context.HttpContext)))
                context.HttpContext.Response.Headers.Location=ReturnNavigation.Append(location,navigation);
        }
    }
}
