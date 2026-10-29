using ClientShowcase.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientShowcase.Pages;

public sealed class ClientModel(ClientRepository clients) : PageModel
{
    public RankedClient Profile { get; private set; } = null!;
    public Client C => Profile.Client;
    public Client? Previous { get; private set; }
    public Client? Next { get; private set; }

    public IActionResult OnGet(string slug)
    {
        var (profile, previous, next) = clients.GetPublishedProfile(slug);
        if (profile is null) return NotFound();
        Profile = profile;
        Previous = previous;
        Next = next;
        return Page();
    }
}
