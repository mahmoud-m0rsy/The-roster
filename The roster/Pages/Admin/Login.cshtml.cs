using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ClientShowcase.Pages.Admin;

[EnableRateLimiting("login")]
public sealed class LoginModel(AdminAuth auth, AdminRepository admins) : PageModel
{
    [BindProperty, Required, StringLength(64)]
    public string Username { get; set; } = "";

    [BindProperty, Required, StringLength(200), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (!admins.Any()) return RedirectToPage("/Admin/Setup");
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        switch (auth.Validate(Username, Password, out var admin))
        {
            case AdminAuth.LoginResult.Success:
                await AdminAuth.SignInAsync(HttpContext, admin!);
                return LocalRedirect(SafeReturn());
            case AdminAuth.LoginResult.LockedOut:
                ModelState.AddModelError("", "Too many failed attempts. This account is locked for 15 minutes.");
                break;
            default:
                ModelState.AddModelError("", "The username or password is incorrect.");
                break;
        }
        Password = "";
        return Page();
    }

    private string SafeReturn() =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && ReturnUrl.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            ? ReturnUrl
            : "/admin";
}
