using System.ComponentModel.DataAnnotations;
using ClientShowcase.Data;
using ClientShowcase.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClientShowcase.Pages.Admin;

public sealed class WorkInput
{
    [Required(ErrorMessage = "Give this project or service a name."), StringLength(160, MinimumLength = 2)]
    [Display(Name = "Project / service name")]
    public string Title { get; set; } = "";

    [StringLength(120), Display(Name = "Type of work")]
    public string? Service { get; set; }

    [StringLength(6000)]
    public string? Description { get; set; }

    [RegularExpression(@"^\d{4}(-(0[1-9]|1[0-2])(-(0[1-9]|[12]\d|3[01]))?)?$",
        ErrorMessage = "Use YYYY, YYYY-MM or YYYY-MM-DD, e.g. 2025-03.")]
    [Display(Name = "Date")]
    public string? WorkDate { get; set; }

    public List<LinkInput> Links { get; set; } = [];

    public sealed class LinkInput
    {
        [StringLength(60)]
        public string? Label { get; set; }

        [StringLength(500)]
        public string? Url { get; set; }
    }
}

[RequestSizeLimit(UploadLimits.RequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = UploadLimits.RequestBytes)]
public sealed class WorkEntryModel(ClientRepository clients, ImageStore images) : AdminPageModel
{
    public const int MaxLinks = 10;

    [BindProperty]
    public WorkInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Images { get; set; } = [];

    [BindProperty]
    public List<IFormFile> Thumbs { get; set; } = [];

    public Client Client { get; private set; } = null!;
    public WorkEntry? Entry { get; private set; }
    public bool IsNew => Entry is null;

    public IActionResult OnGet(long id, string entryId)
    {
        if (!Load(id, entryId)) return NotFound();
        if (Entry is not null)
        {
            Input = new WorkInput
            {
                Title = Entry.Title,
                Service = Entry.Service,
                Description = Entry.Description,
                WorkDate = Entry.WorkDate,
                Links = Entry.Links.Select(l => new WorkInput.LinkInput { Label = l.Label, Url = l.Url }).ToList(),
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, string entryId, CancellationToken ct)
    {
        if (!Load(id, entryId)) return NotFound();

        var links = new List<WorkLink>();
        for (var i = 0; i < Input.Links.Count; i++)
        {
            var link = Input.Links[i];
            var url = link.Url?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                if (!string.IsNullOrWhiteSpace(link.Label))
                    ModelState.AddModelError($"Input.Links[{i}].Url", "Add the web address for this link.");
                continue;
            }
            if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
            if (!SocialLinks.IsWebUrl(url))
            {
                ModelState.AddModelError($"Input.Links[{i}].Url", "Enter a full web address, e.g. https://example.com/campaign.");
                continue;
            }
            links.Add(new WorkLink { Label = ClientInput.Clean(link.Label) ?? "", Url = url });
        }
        if (links.Count > MaxLinks) ModelState.AddModelError("", $"Add at most {MaxLinks} links per entry.");
        if (!ModelState.IsValid) return Page();

        var entry = Entry ?? new WorkEntry { ClientId = id };
        entry.Title = ClientInput.Clean(Input.Title)!;
        entry.Service = ClientInput.Clean(Input.Service);
        entry.Description = ClientInput.Clean(Input.Description, multiline: true);
        entry.WorkDate = ClientInput.Clean(Input.WorkDate);
        entry.Links = links;

        List<NewImage> saved = [];
        if (Images.Count > 0)
        {
            try
            {
                saved = await images.SaveAsync(Images, Thumbs, ct);
            }
            catch (ImageStore.ImageRejectedException ex)
            {
                ModelState.AddModelError(nameof(Images), ex.Message);
                return Page();
            }
        }

        if (IsNew) clients.CreateWorkEntry(entry);
        else clients.UpdateWorkEntry(entry);
        if (saved.Count > 0) clients.AddWorkImages(entry.Id, saved);

        Flash(IsNew ? $"“{entry.Title}” was added to {Client.FullName}'s work history." : "Work entry saved.");
        return Redirect(IsNew ? $"/admin/clients/{id}/work" : $"/admin/clients/{id}/work/{entry.Id}");
    }

    public async Task<IActionResult> OnPostAddImagesAsync(long id, string entryId, CancellationToken ct)
    {
        if (!Load(id, entryId) || Entry is null) return NotFound();
        try
        {
            var saved = await images.SaveAsync(Images, Thumbs, ct);
            if (saved.Count > 0)
            {
                clients.AddWorkImages(Entry.Id, saved);
                Flash(saved.Count == 1 ? "Image added." : $"{saved.Count} images added.");
            }
        }
        catch (ImageStore.ImageRejectedException ex)
        {
            FlashError(ex.Message);
        }
        return Redirect($"/admin/clients/{id}/work/{Entry.Id}#images");
    }

    public IActionResult OnPostDeleteImage(long id, string entryId, long imageId)
    {
        if (!Load(id, entryId) || Entry is null) return NotFound();
        images.Delete(clients.DeleteWorkImage(Entry.Id, imageId));
        Flash("Image removed.");
        return Redirect($"/admin/clients/{id}/work/{Entry.Id}#images");
    }

    public IActionResult OnPostReorderImages(long id, string entryId, [FromBody] OrderRequest request)
    {
        if (!long.TryParse(entryId, out var eid) || !clients.WorkEntryBelongsTo(id, eid)) return NotFound();
        return OrderResult(() => clients.ReorderWorkImages(eid, request.Ids));
    }

    private bool Load(long id, string entryId)
    {
        var client = clients.GetById(id, includeDetails: false);
        if (client is null) return false;
        Client = client;
        if (entryId == "new") return true;
        if (!long.TryParse(entryId, out var eid)) return false;
        Entry = clients.GetWorkEntry(id, eid);
        return Entry is not null;
    }
}
