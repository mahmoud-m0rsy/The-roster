using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

public sealed class WorkModel(ClientRepository clients, ImageStore images) : AdminPageModel
{
    public Client Client { get; private set; } = null!;

    public IActionResult OnGet(long id)
    {
        var client = clients.GetById(id);
        if (client is null) return NotFound();
        Client = client;
        return Page();
    }

    public IActionResult OnPostReorder(long id, [FromBody] OrderRequest request) =>
        OrderResult(() => clients.ReorderWork(id, request.Ids));

    public IActionResult OnPostMove(long id, long entryId, int position)
    {
        try
        {
            clients.MoveWork(id, entryId, position);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return Redirect($"/admin/clients/{id}/work#entry-{entryId}");
    }

    public IActionResult OnPostDelete(long id, long entryId)
    {
        var entry = clients.GetWorkEntry(id, entryId);
        if (entry is null) return NotFound();
        images.Delete(clients.DeleteWorkEntry(id, entryId));
        Flash($"“{entry.Title}” was removed from the work history.");
        return Redirect($"/admin/clients/{id}/work");
    }
}
