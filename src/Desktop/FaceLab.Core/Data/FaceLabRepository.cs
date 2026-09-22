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

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace FaceLab.Core.Data;

public interface IFaceLabRepository
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<ImageRecord> SaveImageAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken);
    Task<ImageRecord?> GetImageAsync(int imageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImageRecord>> GetImagesAsync(int take, CancellationToken cancellationToken);
    Task<RunRecord> SaveRunAsync(RunRecord run, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunRecord>> GetRunsAsync(int take, CancellationToken cancellationToken);
    Task<RunRecord?> GetRunAsync(int runId, CancellationToken cancellationToken);
    Task RecordSightingAsync(string personId, string dynamicPersonGroupId, double confidence, DateTimeOffset seenUtc, string? displayName, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersonRecord>> GetPeopleAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ConfigProfile>> GetProfilesAsync(CancellationToken cancellationToken);
    Task<ConfigProfile> SaveProfileAsync(string name, string json, CancellationToken cancellationToken);
    Task DeleteProfileAsync(int profileId, CancellationToken cancellationToken);
    Task ResetAsync(CancellationToken cancellationToken);
}

public sealed class FaceLabRepository : IFaceLabRepository
{
    private readonly IDbContextFactory<FaceLabDbContext> _contextFactory;

    public FaceLabRepository(IDbContextFactory<FaceLabDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    public async Task<ImageRecord> SaveImageAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Images.FirstOrDefaultAsync(i => i.Sha256 == hash, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var image = new ImageRecord
        {
            FileName = fileName,
            ContentType = contentType,
            Sha256 = hash,
            ByteCount = bytes.Length,
            Bytes = bytes,
            ImportedUtc = DateTimeOffset.UtcNow
        };

        db.Images.Add(image);
        await db.SaveChangesAsync(cancellationToken);
        return image;
    }

    public async Task<ImageRecord?> GetImageAsync(int imageId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Images.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken);
    }

    public async Task<IReadOnlyList<ImageRecord>> GetImagesAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Images
            .AsNoTracking()
            .OrderByDescending(i => i.ImportedUtc)
            .Take(take)
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

    /// <summary>Drops and recreates the local database - the test bench is scratch data.</summary>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}
