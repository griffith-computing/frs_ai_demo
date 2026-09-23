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
    Task<IReadOnlyList<ManagedIdentityRecord>> GetManagedIdentitiesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedIdentityRecord>> GetUnexpiredProvisionalIdentitiesAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedIdentityRecord>> GetExpiredProvisionalIdentitiesAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<ManagedIdentityRecord?> GetManagedIdentityAsync(string personId, CancellationToken cancellationToken);
    Task<bool> HasEnrollmentEvidenceAsync(string personId, string imageSha256, CancellationToken cancellationToken);
    Task CreateProvisionalIdentityAsync(ManagedIdentityRecord identity, EnrollmentEvidenceRecord evidence, CancellationToken cancellationToken);
    Task<bool> TryAddEnrollmentEvidenceAsync(string personId, EnrollmentEvidenceRecord evidence, DateTimeOffset seenUtc, double confidence, CancellationToken cancellationToken);
    Task PromoteManagedIdentityAsync(string personId, CancellationToken cancellationToken);
    Task DeleteManagedIdentityAsync(string personId, CancellationToken cancellationToken);
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
        await UpgradeSchemaAsync(db, cancellationToken);
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

    public async Task<IReadOnlyList<ManagedIdentityRecord>> GetManagedIdentitiesAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ManagedIdentities
            .AsNoTracking()
            .OrderBy(p => p.State)
            .ThenByDescending(p => p.LastSeenUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedIdentityRecord>> GetUnexpiredProvisionalIdentitiesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ManagedIdentities
            .AsNoTracking()
            .Where(p => p.State == ManagedIdentityStates.Provisional && p.ExpiresUtc > now)
            .OrderByDescending(p => p.LastSeenUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedIdentityRecord>> GetExpiredProvisionalIdentitiesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ManagedIdentities
            .AsNoTracking()
            .Where(p => p.State == ManagedIdentityStates.Provisional && p.ExpiresUtc <= now)
            .OrderBy(p => p.ExpiresUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<ManagedIdentityRecord?> GetManagedIdentityAsync(
        string personId,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ManagedIdentities
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PersonId == personId, cancellationToken);
    }

    public async Task<bool> HasEnrollmentEvidenceAsync(
        string personId,
        string imageSha256,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.EnrollmentEvidence.AnyAsync(
            e => e.PersonId == personId && e.ImageSha256 == imageSha256,
            cancellationToken);
    }

    public async Task CreateProvisionalIdentityAsync(
        ManagedIdentityRecord identity,
        EnrollmentEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        identity.State = ManagedIdentityStates.Provisional;
        identity.EvidenceCount = 1;
        evidence.PersonId = identity.PersonId;
        identity.Evidence.Add(evidence);
        db.ManagedIdentities.Add(identity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAddEnrollmentEvidenceAsync(
        string personId,
        EnrollmentEvidenceRecord evidence,
        DateTimeOffset seenUtc,
        double confidence,
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.ManagedIdentities.FirstOrDefaultAsync(p => p.PersonId == personId, cancellationToken)
            ?? throw new InvalidOperationException($"Managed identity {personId} was not found.");

        if (await db.EnrollmentEvidence.AnyAsync(
                e => e.PersonId == personId && e.ImageSha256 == evidence.ImageSha256,
                cancellationToken))
        {
            return false;
        }

        evidence.PersonId = personId;
        db.EnrollmentEvidence.Add(evidence);
        identity.EvidenceCount++;
        identity.LastSeenUtc = seenUtc;
        identity.LastConfidence = confidence;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task PromoteManagedIdentityAsync(string personId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.ManagedIdentities.FirstOrDefaultAsync(p => p.PersonId == personId, cancellationToken)
            ?? throw new InvalidOperationException($"Managed identity {personId} was not found.");
        identity.State = ManagedIdentityStates.Active;
        identity.ExpiresUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteManagedIdentityAsync(string personId, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.ManagedIdentities.FirstOrDefaultAsync(p => p.PersonId == personId, cancellationToken);
        if (identity is null)
        {
            return;
        }

        db.ManagedIdentities.Remove(identity);
        await db.SaveChangesAsync(cancellationToken);
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
        await UpgradeSchemaAsync(db, cancellationToken);
    }

    private static async Task UpgradeSchemaAsync(FaceLabDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ManagedIdentities" (
                "PersonId" TEXT NOT NULL CONSTRAINT "PK_ManagedIdentities" PRIMARY KEY,
                "DynamicPersonGroupId" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "CreatedUtc" INTEGER NOT NULL,
                "LastSeenUtc" INTEGER NOT NULL,
                "ExpiresUtc" INTEGER NULL,
                "EvidenceCount" INTEGER NOT NULL,
                "LastConfidence" REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ManagedIdentities_State_ExpiresUtc"
                ON "ManagedIdentities" ("State", "ExpiresUtc");
            CREATE TABLE IF NOT EXISTS "EnrollmentEvidence" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_EnrollmentEvidence" PRIMARY KEY AUTOINCREMENT,
                "PersonId" TEXT NOT NULL,
                "ImageId" INTEGER NOT NULL,
                "ImageSha256" TEXT NOT NULL,
                "CapturedUtc" INTEGER NOT NULL,
                "Confidence" REAL NOT NULL,
                CONSTRAINT "FK_EnrollmentEvidence_ManagedIdentities_PersonId"
                    FOREIGN KEY ("PersonId") REFERENCES "ManagedIdentities" ("PersonId") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_EnrollmentEvidence_PersonId_ImageSha256"
                ON "EnrollmentEvidence" ("PersonId", "ImageSha256");
            """,
            cancellationToken);

        await EnsureColumnAsync(db, "FaceResults", "ProvisionalPersonId", "TEXT NULL", cancellationToken);
        await EnsureColumnAsync(db, "FaceResults", "DecisionReason", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        FaceLabDbContext db,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        await reader.DisposeAsync();
        if (!exists)
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
