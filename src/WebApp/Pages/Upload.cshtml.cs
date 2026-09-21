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
using System.Security.Claims;
using FrsAiDemo.WebApp.Services;
using FrsAiDemo.WebApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace FrsAiDemo.WebApp.Pages;

public sealed class UploadModel(
    IUploadService uploadService,
    IFaceReviewRepository repository,
    IOptions<BulkUploadOptions> bulkOptions,
    ILogger<UploadModel> logger) : PageModel
{
    [BindProperty]
    public IFormFile? Photo { get; set; }
    public bool BulkEnabled => bulkOptions.Value.Enabled;
    public int MaxFiles => Math.Clamp(bulkOptions.Value.MaxFiles, 1, 100);
    public int MaxConcurrency => Math.Clamp(bulkOptions.Value.MaxConcurrency, 1, 10);
    public IReadOnlyList<StorageImportSource> Sources => bulkOptions.Value.GetSources();

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

    public async Task<IActionResult> OnPostStartBatchAsync(int fileCount, CancellationToken cancellationToken)
    {
        if (!BulkEnabled)
        {
            return NotFound();
        }
        if (fileCount is < 1 || fileCount > MaxFiles)
        {
            return BadRequest(new { error = $"Choose between 1 and {MaxFiles} images." });
        }

        var now = DateTimeOffset.UtcNow;
        var batch = new UploadBatchRecord
        {
            Id = $"batch-{Guid.NewGuid():N}",
            OwnerObjectId = GetOwnerObjectId(),
            ExpectedFileCount = fileCount,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await repository.CreateBatchAsync(batch, cancellationToken);
        return new JsonResult(new { batchId = batch.Id, maxConcurrency = MaxConcurrency });
    }

    public async Task<IActionResult> OnPostBatchFileAsync(
        string batchId,
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (!BulkEnabled)
        {
            return NotFound();
        }

        var batch = await repository.GetBatchAsync(batchId, cancellationToken);
        if (batch is null || batch.OwnerObjectId != GetOwnerObjectId() || batch.SubmissionCompleted)
        {
            return BadRequest(new { error = "The batch is not available for uploads." });
        }
        if (photo is null)
        {
            return BadRequest(new { error = "Choose a JPEG or PNG photo." });
        }
        if (!await repository.TryReserveBatchSlotAsync(batch.Id, cancellationToken))
        {
            return BadRequest(new { error = "The batch has already accepted its configured number of files." });
        }

        try
        {
            var result = await uploadService.UploadAsync(
                photo,
                cancellationToken,
                batchId: batch.Id,
                originalFileName: Path.GetFileName(photo.FileName));
            return new JsonResult(new { result.UploadId });
        }
        catch (ValidationException exception)
        {
            await uploadService.RecordFailedUploadAsync(
                batch.Id,
                photo.FileName,
                exception.Message,
                cancellationToken);
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to accept batch photo {FileName}", photo.FileName);
            const string summary = "The photo could not be queued for analysis.";
            await uploadService.RecordFailedUploadAsync(batch.Id, photo.FileName, summary, CancellationToken.None);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = summary });
        }
    }

    public async Task<IActionResult> OnPostFinishBatchAsync(string batchId, CancellationToken cancellationToken)
    {
        if (!BulkEnabled)
        {
            return NotFound();
        }
        var batch = await repository.GetBatchAsync(batchId, cancellationToken);
        if (batch is null || batch.OwnerObjectId != GetOwnerObjectId())
        {
            return NotFound();
        }
        var submitted = await repository.GetBatchUploadsAsync(batchId, cancellationToken);
        for (var index = submitted.Count; index < batch.ExpectedFileCount; index++)
        {
            await uploadService.RecordFailedUploadAsync(
                batchId,
                $"Unreceived file {index + 1}",
                "The upload request did not reach the server.",
                cancellationToken);
        }
        await repository.CompleteBatchSubmissionAsync(batchId, cancellationToken);
        return new JsonResult(new { statusUrl = Url.Page("/BatchStatus", new { id = batchId }) });
    }

    public async Task<IActionResult> OnPostStartImportAsync(
        string sourceKey,
        string mode,
        CancellationToken cancellationToken)
    {
        if (!BulkEnabled)
        {
            return NotFound();
        }

        var source = Sources.SingleOrDefault(x => string.Equals(x.Key, sourceKey, StringComparison.Ordinal));
        var modeAllowed = string.Equals(mode, "copy", StringComparison.OrdinalIgnoreCase)
            ? source?.AllowCopy == true
            : string.Equals(mode, "inplace", StringComparison.OrdinalIgnoreCase) && source?.AllowInPlace == true;
        if (source is null || !modeAllowed)
        {
            ModelState.AddModelError(string.Empty, "Choose an allowed storage source and import mode.");
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        var import = new StorageImportRecord
        {
            Id = $"import-{Guid.NewGuid():N}",
            OwnerObjectId = GetOwnerObjectId(),
            SourceKey = source.Key,
            SourceAccountName = source.AccountName,
            SourceContainerName = source.ContainerName,
            Mode = mode.ToLowerInvariant(),
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await repository.CreateImportAsync(import, cancellationToken);
        return RedirectToPage("/ImportStatus", new { id = import.Id });
    }

    private string GetOwnerObjectId() =>
        User.FindFirstValue("oid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user has no object identifier.");
}