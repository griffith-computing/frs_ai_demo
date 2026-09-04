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