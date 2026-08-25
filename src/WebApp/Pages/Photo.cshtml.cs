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