using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

public sealed class SettingsModel(SettingsRepository settings) : AdminPageModel
{
    [BindProperty] public SettingsInput Input { get; set; } = new();

    public sealed class SettingsInput
    {
        [Required, StringLength(60), Display(Name = "Website name")]
        public string SiteName { get; set; } = "";

        [Required, StringLength(80)]
        public string Tagline { get; set; } = "";

        [Required, StringLength(160), Display(Name = "Homepage headline")]
        public string HeroTitle { get; set; } = "";

        [Required, StringLength(400), Display(Name = "Homepage introduction")]
        public string HeroSubtitle { get; set; } = "";

        [StringLength(200), Display(Name = "Footer note")]
        public string? FooterNote { get; set; }
    }

    public void OnGet()
    {
        var s = settings.Get();
        Input = new SettingsInput
        {
            SiteName = s.SiteName, Tagline = s.Tagline, HeroTitle = s.HeroTitle,
            HeroSubtitle = s.HeroSubtitle, FooterNote = s.FooterNote,
        };
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        settings.Save(new SiteSettings
        {
            SiteName = Input.SiteName.Trim(),
            Tagline = Input.Tagline.Trim(),
            HeroTitle = Input.HeroTitle.Trim(),
            HeroSubtitle = Input.HeroSubtitle.Trim(),
            FooterNote = Input.FooterNote?.Trim() ?? "",
        });
        Flash("Website settings saved.");
        return Redirect("/admin/settings");
    }
}
