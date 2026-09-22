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

using FaceLab.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FaceLab.Core.Tests;

/// <summary>Backs the repository with a throwaway SQLite file, mirroring how the MAUI app runs.</summary>
public sealed class TempSqliteDatabase : IDbContextFactory<FaceLabDbContext>, IDisposable
{
    private readonly string _path;
    private readonly DbContextOptions<FaceLabDbContext> _options;

    public TempSqliteDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"facelab-tests-{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<FaceLabDbContext>()
            .UseSqlite($"Data Source={_path}")
            .Options;
    }

    public FaceLabDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        using (var db = CreateDbContext())
        {
            db.Database.EnsureDeleted();
        }

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

public sealed class FaceLabRepositoryTests : IDisposable
{
    private readonly TempSqliteDatabase _database = new();
    private readonly FaceLabRepository _repository;

    public FaceLabRepositoryTests()
    {
        _repository = new FaceLabRepository(_database);
        _repository.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SaveImageAsync_stores_the_bytes_and_a_sha256()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var image = await _repository.SaveImageAsync("face.jpg", "image/jpeg", bytes, CancellationToken.None);

        Assert.True(image.Id > 0);
        Assert.Equal(4, image.ByteCount);
        Assert.Equal(64, image.Sha256.Length);

        var reloaded = await _repository.GetImageAsync(image.Id, CancellationToken.None);
        Assert.Equal(bytes, reloaded!.Bytes);
        Assert.Equal("image/jpeg", reloaded.ContentType);
    }

    [Fact]
    public async Task SaveImageAsync_reuses_the_existing_row_for_identical_bytes()
    {
        var first = await _repository.SaveImageAsync("a.jpg", "image/jpeg", new byte[] { 7, 7 }, CancellationToken.None);
        var second = await _repository.SaveImageAsync("b.jpg", "image/jpeg", new byte[] { 7, 7 }, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("a.jpg", second.FileName);
        Assert.Single(await _repository.GetImagesAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task SaveRunAsync_persists_faces_and_traces_with_the_run()
    {
        var image = await _repository.SaveImageAsync("face.jpg", "image/jpeg", new byte[] { 1 }, CancellationToken.None);

        var run = new RunRecord
        {
            ImageId = image.Id,
            ConfigSnapshotJson = TestOptions.Create().ToSnapshotJson(),
            Status = RunStatuses.Completed,
            DetectedFaceCount = 1,
            Faces = { new FaceResultRecord { FaceId = "face-1", Outcome = FaceOutcomes.Matched, MatchedPersonId = "p1", Confidence = 0.9 } },
            Traces = { new CallTraceRecord { Operation = "detect faces", Method = "POST", Url = "https://face.example.com/detect", StatusCode = 200 } }
        };

        var saved = await _repository.SaveRunAsync(run, CancellationToken.None);

        var reloaded = await _repository.GetRunAsync(saved.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("face-1", Assert.Single(reloaded!.Faces).FaceId);
        Assert.Equal("detect faces", Assert.Single(reloaded.Traces).Operation);
        Assert.Equal("face.jpg", reloaded.Image!.FileName);
        Assert.Equal(0.6, reloaded.Config.ConfidenceThreshold);
    }

    [Fact]
    public async Task GetRunsAsync_returns_newest_runs_first()
    {
        var image = await _repository.SaveImageAsync("face.jpg", "image/jpeg", new byte[] { 1 }, CancellationToken.None);

        await _repository.SaveRunAsync(
            new RunRecord { ImageId = image.Id, StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-10) },
            CancellationToken.None);
        var newest = await _repository.SaveRunAsync(
            new RunRecord { ImageId = image.Id, StartedUtc = DateTimeOffset.UtcNow },
            CancellationToken.None);

        var runs = await _repository.GetRunsAsync(10, CancellationToken.None);

        Assert.Equal(2, runs.Count);
        Assert.Equal(newest.Id, runs[0].Id);
    }

    [Fact]
    public async Task RecordSightingAsync_creates_then_increments_a_person()
    {
        var first = DateTimeOffset.UtcNow.AddMinutes(-5);
        var second = DateTimeOffset.UtcNow;

        await _repository.RecordSightingAsync("person-1", "group", 0.8, first, "Ada", CancellationToken.None);
        await _repository.RecordSightingAsync("person-1", "group", 0.95, second, null, CancellationToken.None);

        var person = Assert.Single(await _repository.GetPeopleAsync(CancellationToken.None));
        Assert.Equal(2, person.SightingCount);
        Assert.Equal(0.95, person.LastConfidence);
        Assert.Equal("Ada", person.DisplayName);
        Assert.Equal(first.ToUnixTimeSeconds(), person.FirstSeenUtc.ToUnixTimeSeconds());
        Assert.Equal(second.ToUnixTimeSeconds(), person.LastSeenUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task SaveProfileAsync_inserts_then_updates_by_name()
    {
        await _repository.SaveProfileAsync("prod-like", """{"ConfidenceThreshold":0.6}""", CancellationToken.None);
        var updated = await _repository.SaveProfileAsync("prod-like", """{"ConfidenceThreshold":0.9}""", CancellationToken.None);

        var profile = Assert.Single(await _repository.GetProfilesAsync(CancellationToken.None));
        Assert.Equal(updated.Id, profile.Id);
        Assert.Contains("0.9", profile.Json);
    }

    [Fact]
    public async Task DeleteProfileAsync_removes_the_profile_and_ignores_unknown_ids()
    {
        var profile = await _repository.SaveProfileAsync("temp", "{}", CancellationToken.None);

        await _repository.DeleteProfileAsync(profile.Id, CancellationToken.None);
        await _repository.DeleteProfileAsync(9999, CancellationToken.None);

        Assert.Empty(await _repository.GetProfilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResetAsync_clears_every_table()
    {
        var image = await _repository.SaveImageAsync("face.jpg", "image/jpeg", new byte[] { 1 }, CancellationToken.None);
        await _repository.SaveRunAsync(new RunRecord { ImageId = image.Id }, CancellationToken.None);
        await _repository.RecordSightingAsync("person-1", "group", 0.8, DateTimeOffset.UtcNow, null, CancellationToken.None);

        await _repository.ResetAsync(CancellationToken.None);

        Assert.Empty(await _repository.GetImagesAsync(10, CancellationToken.None));
        Assert.Empty(await _repository.GetRunsAsync(10, CancellationToken.None));
        Assert.Empty(await _repository.GetPeopleAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Deleting_a_run_cascades_to_its_faces_and_traces()
    {
        var image = await _repository.SaveImageAsync("face.jpg", "image/jpeg", new byte[] { 1 }, CancellationToken.None);
        var run = await _repository.SaveRunAsync(
            new RunRecord
            {
                ImageId = image.Id,
                Faces = { new FaceResultRecord { FaceId = "face-1" } },
                Traces = { new CallTraceRecord { Operation = "detect faces", Method = "POST", Url = "https://face.example.com" } }
            },
            CancellationToken.None);

        await using (var db = _database.CreateDbContext())
        {
            db.Runs.Remove(await db.Runs.FirstAsync(r => r.Id == run.Id));
            await db.SaveChangesAsync();
        }

        await using var verify = _database.CreateDbContext();
        Assert.Empty(verify.FaceResults);
        Assert.Empty(verify.CallTraces);
    }
}
