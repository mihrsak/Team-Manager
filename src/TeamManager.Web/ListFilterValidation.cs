using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TeamManager.Web;

public static class ListFilterValidation
{
    public static bool Valid(ModelStateDictionary state, params string[] keys)
        =>keys.All(key=>!state.TryGetValue(key,out var entry)||entry.Errors.Count==0);

    public static string Value(ModelStateDictionary state,string key,string? fallback)
        =>state.TryGetValue(key,out var entry)?entry.AttemptedValue??fallback??"":fallback??"";

    public static void SingleValues(HttpRequest request,ModelStateDictionary state,params string[] keys)
    {
        foreach(var key in keys)
        {
            var query=request.Query[key];
            var form=request.HasFormContentType?request.Form[key]:default;
            if(query.Count>1 || form.Count>1 || query.Count==1&&form.Count==1&&query[0]!=form[0])
                state.TryAddModelError(key,"Svaki filtar smije imati samo jednu vrijednost.");
        }
    }
}
