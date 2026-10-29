using ClientShowcase.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientShowcase.Pages;

public sealed class IndexModel(ClientRepository clients, SettingsRepository settings) : PageModel
{
    public const int PageSize = 48;
    private const int SpotlightCount = 3;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public List<RankedClient> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public SiteSettings Site { get; private set; } = new();
    public (int Clients, int Projects) Stats { get; private set; }

    /// <summary>Top-ranked clients shown in the hero collage.</summary>
    public List<RankedClient> HeroPicks { get; private set; } = [];

    /// <summary>On the first unfiltered page the top three get large spotlight cards.</summary>
    public List<RankedClient> Spotlight { get; private set; } = [];
    public List<RankedClient> Rest { get; private set; } = [];
    public bool IsFiltered => !string.IsNullOrEmpty(Search);

    public void OnGet()
    {
        Site = settings.Get();
        Search = Search?.Trim();
        if (Search?.Length > 100) Search = Search[..100];
        PageNumber = Math.Max(1, PageNumber);
        (Items, Total) = clients.ListPublished(Search, PageNumber, PageSize);
        Stats = clients.PublicStats();
        HeroPicks = clients.ListPublished(null, 1, SpotlightCount).Items;

        if (PageNumber == 1 && !IsFiltered && Items.Count > SpotlightCount)
        {
            Spotlight = Items.Take(SpotlightCount).ToList();
            Rest = Items.Skip(SpotlightCount).ToList();
        }
        else
        {
            Rest = Items;
        }
    }

    public string PageUrl(int page) =>
        Url.Page("/Index", new { q = string.IsNullOrEmpty(Search) ? null : Search, page = page == 1 ? (int?)null : page })! + "#roster";
}
