using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

[RequestSizeLimit(UploadLimits.RequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.RequestBytes)]
public sealed class ClientEditModel(ClientRepository clients, ImageStore images) : AdminPageModel
{
    [BindProperty]
    public ClientInput Input { get; set; } = new();

    public Client Client { get; private set; } = null!;

    public IActionResult OnGet(long id)
    {
        if (!Load(id)) return NotFound();
        Input = ClientInput.From(Client);
        return Page();
    }

    public IActionResult OnPost(long id)
    {
        if (!Load(id)) return NotFound();
        if (!Input.TryApply(Client, ModelState, nameof(Input))) return Page();
        clients.Update(Client);
        Flash("Profile saved.");
        return Redirect($"/admin/clients/{id}/edit");
    }

    public async Task<IActionResult> OnPostAddPhotosAsync(long id, List<IFormFile> photos, List<IFormFile> thumbs, CancellationToken ct)
    {
        if (clients.GetById(id, includeDetails: false) is null) return NotFound();
        try
        {
            var saved = await images.SaveAsync(photos, thumbs, ct);
            if (saved.Count == 0)
            {
                FlashError("Choose at least one photo to upload.");
            }
            else
            {
                clients.AddPhotos(id, saved);
                Flash(saved.Count == 1 ? "Photo added." : $"{saved.Count} photos added.");
            }
        }
        catch (ImageStore.ImageRejectedException ex)
        {
            FlashError(ex.Message);
        }
        return Redirect($"/admin/clients/{id}/edit#photos");
    }

    public IActionResult OnPostDeletePhoto(long id, long photoId)
    {
        images.Delete(clients.DeletePhoto(id, photoId));
        Flash("Photo removed.");
        return Redirect($"/admin/clients/{id}/edit#photos");
    }

    public IActionResult OnPostCover(long id, long photoId)
    {
        try
        {
            clients.MakeCover(id, photoId);
            Flash("Profile photo updated.");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        return Redirect($"/admin/clients/{id}/edit#photos");
    }

    public IActionResult OnPostReorderPhotos(long id, [FromBody] OrderRequest request) =>
        OrderResult(() => clients.ReorderPhotos(id, request.Ids));

    private bool Load(long id)
    {
        var client = clients.GetById(id);
        if (client is null) return false;
        Client = client;
        return true;
    }
}
