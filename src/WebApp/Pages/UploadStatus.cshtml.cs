using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class UploadStatusModel(IFaceReviewRepository repository) : PageModel
{
    public UploadRecord Upload { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var upload = await repository.GetUploadAsync(id, cancellationToken);
        if (upload is null)
        {
            return NotFound();
        }
        Upload = upload;
        return Page();
    }

    public async Task<IActionResult> OnGetStateAsync(string id, CancellationToken cancellationToken)
    {
        var upload = await repository.GetUploadAsync(id, cancellationToken);
        return upload is null
            ? NotFound()
            : new JsonResult(new { upload.Id, upload.Status, upload.DetectedFaceCount, upload.FailureSummary, upload.UpdatedUtc });
    }
}