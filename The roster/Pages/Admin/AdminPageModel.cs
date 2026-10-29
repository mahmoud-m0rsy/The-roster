using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientShowcase.Pages.Admin;

public sealed class OrderRequest
{
    public List<long> Ids { get; set; } = [];

    /// <summary>Optional priority tier per id (same order); used by the client ranking list.</summary>
    public List<int>? Priorities { get; set; }
}

public abstract class AdminPageModel : PageModel
{
    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["FlashError"] = message;

    /// <summary>Runs a reorder and reports the outcome as JSON for the drag-and-drop UI.</summary>
    protected IActionResult OrderResult(Action apply)
    {
        try
        {
            apply();
            return new JsonResult(new { ok = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            return new JsonResult(new { ok = false, error = ex.Message }) { StatusCode = StatusCodes.Status409Conflict };
        }
    }
}
