using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace TeamManager.Web.Pages;
public class InitialPasswordModel(UserStore store,IPasswordHasher<AppUser> hasher):PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        if(User.Identity?.Name!="admin")return RedirectToPage("/Users");
        var user=await store.Find("admin");
        return user is not null && hasher.VerifyHashedPassword(user,user.PasswordHash,"admin")!=PasswordVerificationResult.Failed
            ? Page() : RedirectToPage("/Users");
    }
}
