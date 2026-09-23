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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FaceLab.Core.Data;

/// <summary>
/// Local SQLite store for the desktop test bench: imported images (bytes included), every run
/// with its configuration snapshot, per-face outcomes, raw Face API call traces, and the
/// locally tracked person directory.
/// </summary>
public sealed class FaceLabDbContext : DbContext
{
    public FaceLabDbContext(DbContextOptions<FaceLabDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImageRecord> Images => Set<ImageRecord>();

    public DbSet<RunRecord> Runs => Set<RunRecord>();

    public DbSet<FaceResultRecord> FaceResults => Set<FaceResultRecord>();

    public DbSet<CallTraceRecord> CallTraces => Set<CallTraceRecord>();

    public DbSet<PersonRecord> People => Set<PersonRecord>();

    public DbSet<ManagedIdentityRecord> ManagedIdentities => Set<ManagedIdentityRecord>();

    public DbSet<EnrollmentEvidenceRecord> EnrollmentEvidence => Set<EnrollmentEvidenceRecord>();

    public DbSet<ConfigProfile> ConfigProfiles => Set<ConfigProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImageRecord>(entity =>
        {
            entity.HasIndex(i => i.Sha256).IsUnique();
            entity.HasMany(i => i.Runs)
                .WithOne(r => r.Image!)
                .HasForeignKey(r => r.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunRecord>(entity =>
        {
            entity.HasIndex(r => r.StartedUtc);
            entity.HasMany(r => r.Faces)
                .WithOne(f => f.Run!)
                .HasForeignKey(f => f.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(r => r.Traces)
                .WithOne(t => t.Run!)
                .HasForeignKey(t => t.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PersonRecord>(entity => entity.HasKey(p => p.PersonId));

        modelBuilder.Entity<ManagedIdentityRecord>(entity =>
        {
            entity.HasKey(p => p.PersonId);
            entity.HasIndex(p => new { p.State, p.ExpiresUtc });
            entity.HasMany(p => p.Evidence)
                .WithOne(e => e.ManagedIdentity!)
                .HasForeignKey(e => e.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EnrollmentEvidenceRecord>(entity =>
        {
            entity.HasIndex(e => new { e.PersonId, e.ImageSha256 }).IsUnique();
        });

        modelBuilder.Entity<ConfigProfile>(entity => entity.HasIndex(p => p.Name).IsUnique());

        ApplyDateTimeOffsetConversions(modelBuilder);
    }

    /// <summary>
    /// SQLite cannot order by a DateTimeOffset, so every timestamp is stored as Unix milliseconds.
    /// All timestamps in this store are UTC, so nothing is lost by normalizing the offset.
    /// </summary>
    private static void ApplyDateTimeOffsetConversions(ModelBuilder modelBuilder)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value));

        var nullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value == null ? null : value.Value.ToUnixTimeMilliseconds(),
            value => value == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(converter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableConverter);
                }
            }
        }
    }
}
