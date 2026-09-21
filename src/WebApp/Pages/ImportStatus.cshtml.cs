//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using System.Security.Claims;
using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class ImportStatusModel(IFaceReviewRepository repository) : PageModel
{
    public StorageImportRecord Import { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var import = await GetOwnedImportAsync(id, cancellationToken);
        if (import is null)
        {
            return NotFound();
        }
        Import = import;
        return Page();
    }

    public async Task<IActionResult> OnGetStateAsync(string id, CancellationToken cancellationToken)
    {
        var import = await GetOwnedImportAsync(id, cancellationToken);
        if (import is null)
        {
            return NotFound();
        }
        var batches = await repository.GetImportBatchesAsync(id, cancellationToken);
        var counts = await repository.GetImportStatusCountsAsync(id, cancellationToken);
        var enumerationComplete = import.Status is "Completed" or "Failed";
        var complete = enumerationComplete && counts.Terminal == counts.Total;
        return new JsonResult(new
        {
            import.Id,
            import.Status,
            import.DiscoveredFileCount,
            import.FailureSummary,
            counts,
            complete,
            batches = batches.Select(x => new { x.Id, x.ExpectedFileCount, x.SubmissionCompleted })
        });
    }

    private async Task<StorageImportRecord?> GetOwnedImportAsync(string id, CancellationToken cancellationToken)
    {
        var import = await repository.GetImportAsync(id, cancellationToken);
        var owner = User.FindFirstValue("oid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return import?.OwnerObjectId == owner ? import : null;
    }
}
