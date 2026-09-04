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