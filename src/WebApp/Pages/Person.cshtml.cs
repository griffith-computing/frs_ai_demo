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

public sealed class PersonModel(IFaceReviewRepository repository) : PageModel
{
    public FaceRecord PersonRecord { get; private set; } = null!;
    public IReadOnlyList<SightingView> Sightings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        var person = await repository.GetPersonAsync(id, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        PersonRecord = person;
        var reviewerObjectId = GetReviewerObjectId();
        var sightings = new List<SightingView>();
        foreach (var sighting in person.RecognitionHistory.OrderByDescending(item => item.TimestampUtc))
        {
            var key = SightingKeys.Create(person.PersonId, sighting);
            var review = await repository.GetReviewAsync(person.PersonId, key, reviewerObjectId, cancellationToken);
            sightings.Add(new SightingView(key, sighting, review));
        }
        Sightings = sightings;
        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(
        string id,
        string sightingKey,
        ReviewDecision decision,
        string? note,
        CancellationToken cancellationToken)
    {
        var person = await repository.GetPersonAsync(id, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }
        if (!person.RecognitionHistory.Any(sighting => SightingKeys.Create(id, sighting) == sightingKey))
        {
            return BadRequest();
        }
        if (note?.Length > 500)
        {
            ModelState.AddModelError(nameof(note), "Notes cannot exceed 500 characters.");
            return await OnGetAsync(id, cancellationToken);
        }

        await repository.UpsertReviewAsync(
            new ReviewInput { PersonId = id, SightingKey = sightingKey, Decision = decision, Note = note },
            GetReviewerObjectId(),
            User.Identity?.Name ?? "Reviewer",
            cancellationToken);
        TempData["ReviewSaved"] = "Review saved.";
        return RedirectToPage(new { id });
    }

    private string GetReviewerObjectId() =>
        User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
        ?? User.FindFirstValue("oid")
        ?? throw new InvalidOperationException("The signed-in user token does not contain an object ID.");

    public sealed record SightingView(string Key, RecognitionEvent Sighting, ReviewRecord? Review);
}