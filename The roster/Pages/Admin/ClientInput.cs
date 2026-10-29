using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ClientShowcase.Pages.Admin;

public sealed class ClientInput
{
    [Required(ErrorMessage = "Full name is required."), StringLength(120, MinimumLength = 2)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [StringLength(120), Display(Name = "Headline / role")]
    public string? Headline { get; set; }

    [Range(1, 120, ErrorMessage = "Age must be between 1 and 120.")]
    public int? Age { get; set; }

    [StringLength(120)]
    public string? Location { get; set; }

    [StringLength(4000), Display(Name = "About")]
    public string? Bio { get; set; }

    [StringLength(30), RegularExpression(@"^\+?[0-9()\s.\-]{6,29}$", ErrorMessage = "Enter a valid phone number, e.g. +20 100 123 4567.")]
    [Display(Name = "Phone number")]
    public string? Phone { get; set; }

    [StringLength(254), EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string? Email { get; set; }

    [StringLength(300), Display(Name = "TikTok")]
    public string? TikTok { get; set; }

    [StringLength(300), Display(Name = "Instagram")]
    public string? Instagram { get; set; }

    [Display(Name = "Show phone number on the website")]
    public bool ShowPhone { get; set; } = true;

    [Display(Name = "Show email address on the website")]
    public bool ShowEmail { get; set; } = true;

    [Display(Name = "Visible on the website")]
    public bool IsPublished { get; set; } = true;

    [StringLength(4000), Display(Name = "Private notes")]
    public string? PrivateNotes { get; set; }

    [Range(Priorities.Top, Priorities.Low, ErrorMessage = "Choose a priority.")]
    public int Priority { get; set; } = Priorities.Standard;

    public static ClientInput From(Client c) => new()
    {
        FullName = c.FullName,
        Headline = c.Headline,
        Age = c.Age,
        Location = c.Location,
        Bio = c.Bio,
        Phone = c.Phone,
        Email = c.Email,
        TikTok = c.TikTokUrl,
        Instagram = c.InstagramUrl,
        ShowPhone = c.ShowPhone,
        ShowEmail = c.ShowEmail,
        IsPublished = c.IsPublished,
        PrivateNotes = c.PrivateNotes,
        Priority = c.Priority,
    };

    /// <summary>
    /// Normalizes social links and copies the input onto the entity. Returns false when the model state
    /// (including attribute validation) has any error, so every problem is reported in one pass.
    /// </summary>
    public bool TryApply(Client target, ModelStateDictionary modelState, string prefix)
    {
        if (!SocialLinks.TryNormalize(TikTok, "tiktok", out var tiktok, out var tiktokError))
            modelState.AddModelError($"{prefix}.{nameof(TikTok)}", tiktokError!);
        if (!SocialLinks.TryNormalize(Instagram, "instagram", out var instagram, out var instagramError))
            modelState.AddModelError($"{prefix}.{nameof(Instagram)}", instagramError!);
        if (!modelState.IsValid) return false;

        target.FullName = Clean(FullName)!;
        target.Headline = Clean(Headline);
        target.Age = Age;
        target.Location = Clean(Location);
        target.Bio = Clean(Bio, multiline: true);
        target.Phone = Clean(Phone);
        target.Email = Clean(Email)?.ToLowerInvariant();
        target.TikTokUrl = tiktok;
        target.InstagramUrl = instagram;
        target.ShowPhone = ShowPhone;
        target.ShowEmail = ShowEmail;
        target.IsPublished = IsPublished;
        target.PrivateNotes = Clean(PrivateNotes, multiline: true);
        target.Priority = Priority;
        return true;
    }

    internal static string? Clean(string? value, bool multiline = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return multiline
            ? trimmed.Replace("\r\n", "\n")
            : string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
