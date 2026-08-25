using System.ComponentModel.DataAnnotations;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class UploadModel(IUploadService uploadService, ILogger<UploadModel> logger) : PageModel
{
    [BindProperty]
    public IFormFile? Photo { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Photo is null)
        {
            ModelState.AddModelError(nameof(Photo), "Choose a JPEG or PNG photo.");
            return Page();
        }

        try
        {
            var result = await uploadService.UploadAsync(Photo, cancellationToken);
            return RedirectToPage("/UploadStatus", new { id = result.UploadId });
        }
        catch (ValidationException exception)
        {
            ModelState.AddModelError(nameof(Photo), exception.Message);
            return Page();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to accept photo upload");
            ModelState.AddModelError(string.Empty, "The photo could not be uploaded. Try again later.");
            return Page();
        }
    }
}