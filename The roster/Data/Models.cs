namespace ClientShowcase.Data;

public sealed class Client
{
    public long Id { get; set; }
    public string Slug { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Headline { get; set; }
    public int? Age { get; set; }
    public string? Location { get; set; }
    public string? Bio { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TikTokUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public bool ShowPhone { get; set; } = true;
    public bool ShowEmail { get; set; } = true;
    public bool IsPublished { get; set; } = true;
    public string? PrivateNotes { get; set; }
    public int Priority { get; set; } = Priorities.Standard;
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>The first photo in the client's gallery is their profile photo.</summary>
    public Photo? Cover { get; set; }
    public int PhotoCount { get; set; }
    public int WorkCount { get; set; }
    public List<Photo> Photos { get; set; } = [];
    public List<WorkEntry> Work { get; set; } = [];

    public string Initials =>
        string.Concat(FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}

/// <summary>
/// Priority tiers. Clients are ordered by tier first, then by their manual position within the tier.
/// </summary>
public static class Priorities
{
    public const int Top = 1;
    public const int High = 2;
    public const int Standard = 3;
    public const int Low = 4;

    public static readonly int[] All = [Top, High, Standard, Low];

    public static bool IsValid(int value) => value is >= Top and <= Low;

    public static string Name(int value) => value switch
    {
        Top => "Top priority",
        High => "High",
        Low => "Low",
        _ => "Standard",
    };

    public static string Hint(int value) => value switch
    {
        Top => "Always shown first",
        High => "Shown after top priority",
        Low => "Shown last",
        _ => "Regular placement",
    };
}

public sealed class Photo
{
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public string File { get; set; } = "";
    public string Thumb { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int Position { get; set; }

    public string Url => "/media/" + File;
    public string ThumbUrl => "/media/" + Thumb;
}

public sealed class WorkEntry
{
    public long Id { get; set; }
    public long ClientId { get; set; }
    public string Title { get; set; } = "";
    public string? Service { get; set; }
    public string? Description { get; set; }
    /// <summary>Free-form ISO-ish date: "2025", "2025-03" or "2025-03-14".</summary>
    public string? WorkDate { get; set; }
    public List<WorkLink> Links { get; set; } = [];
    public int Position { get; set; }
    public List<Photo> Images { get; set; } = [];

    public string? DisplayDate => DateDisplay.Format(WorkDate);
}

public sealed class WorkLink
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class SiteSettings
{
    public string SiteName { get; set; } = "The Roster";
    public string Tagline { get; set; } = "Talent, hand-picked.";
    public string HeroTitle { get; set; } = "The faces and creators *we proudly represent.*";
    public string HeroSubtitle { get; set; } =
        "A curated roster of models, creators and collaborators — every profile hand-placed, every partnership on record.";
    public string FooterNote { get; set; } = "All profiles are shared with the consent of our clients.";
}

public sealed class Admin
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public static class DateDisplay
{
    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string? Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('-');
        if (parts.Length >= 2 && int.TryParse(parts[1], out var m) && m is >= 1 and <= 12)
        {
            return parts.Length >= 3 && int.TryParse(parts[2], out var d)
                ? $"{d} {Months[m - 1]} {parts[0]}"
                : $"{Months[m - 1]} {parts[0]}";
        }
        return parts[0];
    }
}
