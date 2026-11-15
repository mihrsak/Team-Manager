using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

public sealed class LocalizedValidationFilter(IStringLocalizer<UiText> text) : IPageFilter
{
    public void OnPageHandlerSelected(PageHandlerSelectedContext context) { }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context) { }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
        foreach (var entry in context.ModelState.Values)
        {
            for (var i = 0; i < entry.Errors.Count; i++)
            {
                var error = entry.Errors[i];
                if (error.ErrorMessage.Length == 0) continue;
                var localized = text[error.ErrorMessage];
                if (!localized.ResourceNotFound)
                    entry.Errors[i] = new ModelError(localized.Value);
            }
        }
    }
}
