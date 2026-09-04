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