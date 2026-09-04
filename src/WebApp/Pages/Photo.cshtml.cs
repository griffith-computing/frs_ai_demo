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

using Azure.Storage.Blobs;
using FrsAiDemo.WebApp.Models;
using FrsAiDemo.WebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FrsAiDemo.WebApp.Pages;

public sealed class PhotoModel(
    IFaceReviewRepository repository,
    BlobServiceClient blobServiceClient,
    IConfiguration configuration) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string personId, string sightingKey, CancellationToken cancellationToken)
    {
        var person = await repository.GetPersonAsync(personId, cancellationToken);
        var sighting = person?.RecognitionHistory.FirstOrDefault(item => SightingKeys.Create(personId, item) == sightingKey);
        if (sighting is null || !Uri.TryCreate(sighting.BlobUrl, UriKind.Absolute, out var blobUri))
        {
            return NotFound();
        }

        var segments = blobUri.AbsolutePath.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        var configuredContainer = configuration["PhotosContainerName"] ?? "photos";
        if (segments.Length != 2 || !string.Equals(segments[0], configuredContainer, StringComparison.Ordinal))
        {
            return NotFound();
        }

        var blobClient = blobServiceClient.GetBlobContainerClient(configuredContainer).GetBlobClient(Uri.UnescapeDataString(segments[1]));
        var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        var contentType = download.Value.Details.ContentType
            ?? (Path.GetExtension(segments[1]).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
        return File(download.Value.Content, contentType);
    }
}