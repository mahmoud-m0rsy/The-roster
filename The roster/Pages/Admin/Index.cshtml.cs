using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

public sealed class IndexModel(ClientRepository clients, ImageStore images) : AdminPageModel
{
    public List<Client> Clients { get; private set; } = [];
    public (int Total, int Published, int Photos, int WorkEntries) Stats { get; private set; }

    public void OnGet()
    {
        Clients = clients.ListAll();
        Stats = clients.Stats();
    }

    public IActionResult OnPostReorder([FromBody] OrderRequest request) =>
        OrderResult(() => clients.ReorderClients(request.Ids, request.Priorities));

    public IActionResult OnPostPriority(long id, int priority)
    {
        var client = clients.GetById(id, includeDetails: false);
        if (client is null) return NotFound();
        if (!Priorities.IsValid(priority)) return BadRequest();
        clients.SetPriority(id, priority);
        Flash($"{client.FullName} is now “{Priorities.Name(priority)}” and was placed at the end of that group.");
        return Redirect($"/admin#client-{id}");
    }

    public IActionResult OnPostMove(long id, int position)
    {
        var client = clients.GetById(id, includeDetails: false);
        if (client is null) return NotFound();
        clients.MoveClient(id, position);
        Flash($"{client.FullName} moved to position {Math.Max(1, Math.Min(position, clients.ListAll().Count))}.");
        return Redirect($"/admin#client-{id}");
    }

    public IActionResult OnPostTogglePublish(long id)
    {
        var client = clients.GetById(id, includeDetails: false);
        if (client is null) return NotFound();
        clients.SetPublished(id, !client.IsPublished);
        Flash(client.IsPublished ? $"{client.FullName} is now hidden from the website." : $"{client.FullName} is now live on the website.");
        return Redirect($"/admin#client-{id}");
    }

    public IActionResult OnPostDelete(long id)
    {
        var client = clients.GetById(id, includeDetails: false);
        if (client is null) return NotFound();
        images.Delete(clients.Delete(id));
        Flash($"{client.FullName} and all of their photos and work history were deleted.");
        return Redirect("/admin");
    }
}
