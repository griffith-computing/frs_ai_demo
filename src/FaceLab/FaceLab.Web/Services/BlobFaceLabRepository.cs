using System.Security.Cryptography;
using Azure.Storage.Blobs;
using FaceLab.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FaceLab.Web.Services;

public sealed class BlobFaceLabRepository : IFaceLabRepository
{
    private readonly IDbContextFactory<FaceLabDbContext> _contextFactory;
    private readonly BlobContainerClient _containerClient;

    public BlobFaceLabRepository(
        IDbContextFactory<FaceLabDbContext> contextFactory,
        BlobContainerClient containerClient)
    {
        _contextFactory = contextFactory;
        _containerClient = containerClient;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task<ImageRecord> SaveImageAsync(
        string fileName,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var blobName = GetBlobName(hash, fileName);
        var blobClient = _containerClient.GetBlobClient(blobName);
        await using (var stream = new MemoryStream(bytes))
        {
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Images.FirstOrDefaultAsync(i => i.Sha256 == hash, cancellationToken);
        if (existing is not null)
        {
            existing.Bytes = bytes;
            return existing;
        }

        var image = new ImageRecord
        {
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Sha256 = hash,
            ByteCount = bytes.Length,
            Bytes = Array.Empty<byte>(),
            ImportedUtc = DateTimeOffset.UtcNow
        };

        db.Images.Add(image);
        await db.SaveChangesAsync(cancellationToken);
        image.Bytes = bytes;
        return image;
    }

    public async Task<ImageRecord?> GetImageAsync(int imageId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var image = await db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (image is null)
        {
            return null;
        }

        image.Bytes = await DownloadImageBytesAsync(image, cancellationToken);
        return image;
    }

    public async Task<IReadOnlyList<ImageRecord>> GetImagesAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Images
            .AsNoTracking()
            .OrderByDescending(i => i.ImportedUtc)
            .Take(take)
            .Select(i => new ImageRecord
            {
                Id = i.Id,
                FileName = i.FileName,
                ContentType = i.ContentType,
                Sha256 = i.Sha256,
                ByteCount = i.ByteCount,
                ImportedUtc = i.ImportedUtc,
                Bytes = Array.Empty<byte>()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RunRecord> SaveRunAsync(RunRecord run, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        run.Image = null;
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<IReadOnlyList<RunRecord>> GetRunsAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs
            .AsNoTracking()
            .Include(r => r.Faces)
            .OrderByDescending(r => r.StartedUtc)
            .ThenByDescending(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<RunRecord?> GetRunAsync(int runId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs
            .AsNoTracking()
            .Include(r => r.Faces)
            .Include(r => r.Traces)
            .Include(r => r.Image)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }

    public async Task RecordSightingAsync(
        string personId,
        string dynamicPersonGroupId,
        double confidence,
        DateTimeOffset seenUtc,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var person = await db.People.FirstOrDefaultAsync(p => p.PersonId == personId, cancellationToken);
        if (person is null)
        {
            person = new PersonRecord
            {
                PersonId = personId,
                DynamicPersonGroupId = dynamicPersonGroupId,
                DisplayName = displayName,
                FirstSeenUtc = seenUtc,
                LastSeenUtc = seenUtc,
                SightingCount = 1,
                LastConfidence = confidence
            };
            db.People.Add(person);
        }
        else
        {
            person.LastSeenUtc = seenUtc;
            person.SightingCount++;
            person.LastConfidence = confidence;
            person.DynamicPersonGroupId = dynamicPersonGroupId;
            person.DisplayName ??= displayName;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PersonRecord>> GetPeopleAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.People
            .AsNoTracking()
            .OrderByDescending(p => p.LastSeenUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConfigProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ConfigProfiles
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConfigProfile> SaveProfileAsync(string name, string json, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.ConfigProfiles.FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
        if (profile is null)
        {
            profile = new ConfigProfile { Name = name, Json = json, UpdatedUtc = DateTimeOffset.UtcNow };
            db.ConfigProfiles.Add(profile);
        }
        else
        {
            profile.Json = json;
            profile.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task DeleteProfileAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.ConfigProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return;
        }

        db.ConfigProfiles.Remove(profile);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    private async Task<byte[]> DownloadImageBytesAsync(ImageRecord image, CancellationToken cancellationToken)
    {
        var blobClient = _containerClient.GetBlobClient(GetBlobName(image.Sha256, image.FileName));
        var response = await blobClient.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    private static string GetBlobName(string sha256, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension)
            ? $"{sha256.ToLowerInvariant()}"
            : $"{sha256.ToLowerInvariant()}{extension.ToLowerInvariant()}";
    }
}
