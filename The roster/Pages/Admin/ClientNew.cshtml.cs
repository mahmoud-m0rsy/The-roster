using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

[RequestSizeLimit(UploadLimits.RequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.RequestBytes)]
public sealed class ClientNewModel(ClientRepository clients, ImageStore images) : AdminPageModel
{
    [BindProperty]
    public ClientInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Photos { get; set; } = [];

    [BindProperty]
    public List<IFormFile> Thumbs { get; set; } = [];

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = new Client();
        if (!Input.TryApply(client, ModelState, nameof(Input))) return Page();

        List<NewImage> saved;
        try
        {
            saved = await images.SaveAsync(Photos, Thumbs, ct);
        }
        catch (ImageStore.ImageRejectedException ex)
        {
            ModelState.AddModelError(nameof(Photos), ex.Message);
            return Page();
        }

        var id = clients.Create(client);
        if (saved.Count > 0) clients.AddPhotos(id, saved);
        Flash($"{client.FullName} was added at position {client.Position}. Add their work history whenever you're ready.");
        return Redirect($"/admin/clients/{id}/edit");
    }
}

public static class UploadLimits
{
    public const long RequestBytes = 300L * 1024 * 1024;
}
