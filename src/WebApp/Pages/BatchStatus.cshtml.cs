//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// This sample is not supported under any Microsoft standard support program or
// service. It is provided to you solely for the purpose of illustration and is
// intended to be modified, tested, and validated by the customer prior to any
// production use. The entire risk arising out of the use or performance of this
// code remains with the customer.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

using System.Security.Claims;
using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class BatchStatusModel(IFaceReviewRepository repository) : PageModel
{
    public UploadBatchRecord Batch { get; private set; } = null!;
    public IReadOnlyList<UploadRecord> Uploads { get; private set; } = [];
    public UploadStatusCounts Counts { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(id, cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }
        Batch = batch;
        Uploads = await repository.GetBatchUploadsAsync(id, cancellationToken);
        Counts = UploadStatusCounts.From(Uploads);
        return Page();
    }

    public async Task<IActionResult> OnGetStateAsync(string id, CancellationToken cancellationToken)
    {
        var batch = await GetOwnedBatchAsync(id, cancellationToken);
        if (batch is null)
        {
            return NotFound();
        }
        var uploads = await repository.GetBatchUploadsAsync(id, cancellationToken);
        return new JsonResult(CreateState(batch, uploads));
    }

    internal static object CreateState(UploadBatchRecord batch, IReadOnlyList<UploadRecord> uploads)
    {
        var counts = UploadStatusCounts.From(uploads);
        var complete = batch.SubmissionCompleted
            && uploads.Count >= batch.ExpectedFileCount
            && counts.Terminal == uploads.Count;
        return new
        {
            batch.Id,
            batch.ExpectedFileCount,
            batch.SubmissionCompleted,
            counts,
            complete,
            uploads = uploads.OrderBy(x => x.OriginalFileName).Select(x => new
            {
                x.Id,
                x.OriginalFileName,
                x.Status,
                x.DetectedFaceCount,
                x.FailureSummary
            })
        };
    }

    private async Task<UploadBatchRecord?> GetOwnedBatchAsync(string id, CancellationToken cancellationToken)
    {
        var batch = await repository.GetBatchAsync(id, cancellationToken);
        var owner = User.FindFirstValue("oid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return batch?.OwnerObjectId == owner ? batch : null;
    }
}
