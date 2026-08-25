using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class IndexModel(IFaceReviewRepository repository) : PageModel
{
    public IReadOnlyList<FaceRecord> People { get; private set; } = [];
    public string? NextToken { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? personId, string? continuationToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(personId))
        {
            return RedirectToPage("/Person", new { id = personId.Trim() });
        }

        var page = await repository.GetPeopleAsync(20, continuationToken, cancellationToken);
        People = page.Items;
        NextToken = page.ContinuationToken;
        return Page();
    }
}