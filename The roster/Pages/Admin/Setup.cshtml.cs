using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ClientShowcase.Pages.Admin;

[EnableRateLimiting("login")]
public sealed class SetupModel(AdminRepository admins, AdminAuth auth, SetupCode setupCode) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Enter the setup code shown when the app started.")]
    [Display(Name = "Setup code")]
    public string Code { get; set; } = "";

    [BindProperty, Required, StringLength(64, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._@-]+$", ErrorMessage = "Use letters, numbers, dots, dashes or underscores.")]
    public string Username { get; set; } = "";

    [BindProperty, Required, StringLength(200, MinimumLength = 10, ErrorMessage = "Use at least 10 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty, Required, Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    [DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";

    public IActionResult OnGet() => admins.Any() ? RedirectToPage("/Admin/Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (admins.Any()) return RedirectToPage("/Admin/Login");
        if (ModelState.IsValid && !setupCode.Matches(Code))
            ModelState.AddModelError(nameof(Code), "That setup code is not correct.");
        if (!ModelState.IsValid) return Page();

        admins.Create(Username.Trim(), auth.Hash(Password));
        setupCode.Clear();
        await AdminAuth.SignInAsync(HttpContext, admins.Find(Username.Trim())!);
        TempData["Flash"] = "Your admin account is ready. Add your first client to get started.";
        return Redirect("/admin");
    }
}
