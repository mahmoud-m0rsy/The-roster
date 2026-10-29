using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

public sealed class AccountModel(AdminRepository admins, AdminAuth auth) : AdminPageModel
{
    [BindProperty, Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [BindProperty, Required, StringLength(200, MinimumLength = 10, ErrorMessage = "Use at least 10 characters.")]
    [DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [BindProperty, Required, Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    [DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var admin = admins.FindById(AdminAuth.CurrentAdminId(User));
        if (admin is null) return Redirect("/admin/login");
        if (!auth.Verify(admin, CurrentPassword))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "Your current password is incorrect.");
            return Page();
        }

        admin.SecurityStamp = admins.UpdatePassword(admin.Id, auth.Hash(NewPassword));
        await AdminAuth.SignInAsync(HttpContext, admin);
        Flash("Password changed. Any other signed-in sessions have been signed out.");
        return Redirect("/admin/account");
    }
}
