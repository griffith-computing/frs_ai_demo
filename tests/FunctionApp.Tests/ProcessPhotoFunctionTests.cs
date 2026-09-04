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

using FrsAiDemo.FunctionApp.Models;
using FrsAiDemo.FunctionApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrsAiDemo.FunctionApp.Tests;

public sealed class ProcessPhotoFunctionTests
{
    [Fact]
    public async Task ProcessUpload_NoFaces_SetsNoFacesWithoutWritingFaceRecord()
    {
        var faceApi = new FakeFaceApi { DetectedFaces = [] };
        var faceRepository = new FakeFaceRepository();
        var uploadRepository = new FakeUploadRepository();
        var function = CreateFunction(faceApi, faceRepository, uploadRepository);

        await function.ProcessUploadEventAsync(CreateUploadEvent(), CancellationToken.None);

        Assert.Equal((UploadStatuses.NoFaces, 0), Assert.Single(uploadRepository.Statuses));
        Assert.Empty(faceRepository.Created);
        Assert.Empty(faceRepository.Recorded);
    }

    [Fact]
    public async Task ProcessUpload_MatchedFace_RecordsDuplicateSighting()
    {
        var faceApi = new FakeFaceApi
        {
            DetectedFaces =
            [
                new DetectedFace
                {
                    FaceId = "face-1",
                    FaceRectangle = new FaceRectangle { Left = 1, Top = 2, Width = 3, Height = 4 }
                }
            ],
            IdentifyResults =
            [
                new IdentifyResult
                {
                    FaceId = "face-1",
                    Candidates = [new IdentifyCandidate { PersonId = "person-existing", Confidence = 0.91 }]
                }
            ]
        };
        var faceRepository = new FakeFaceRepository();
        var uploadRepository = new FakeUploadRepository();
        var function = CreateFunction(faceApi, faceRepository, uploadRepository);

        await function.ProcessUploadEventAsync(CreateUploadEvent(), CancellationToken.None);

        var recorded = Assert.Single(faceRepository.Recorded);
        Assert.Equal("person-existing", recorded.PersonId);
        Assert.Equal(0.91, recorded.Sighting.Confidence);
        Assert.Empty(faceRepository.Created);
        Assert.Empty(faceApi.EnrolledFaces);
        Assert.Equal((UploadStatuses.Completed, 1), Assert.Single(uploadRepository.Statuses));
    }

    [Fact]
    public async Task ProcessUpload_MultipleNewFaces_EnrollsEachDetectedRectangle()
    {
        var firstRectangle = new FaceRectangle { Left = 1, Top = 2, Width = 30, Height = 40 };
        var secondRectangle = new FaceRectangle { Left = 50, Top = 60, Width = 70, Height = 80 };
        var faceApi = new FakeFaceApi
        {
            DetectedFaces =
            [
                new DetectedFace { FaceId = "face-1", FaceRectangle = firstRectangle },
                new DetectedFace { FaceId = "face-2", FaceRectangle = secondRectangle }
            ],
            IdentifyResults = []
        };
        var faceRepository = new FakeFaceRepository();
        var uploadRepository = new FakeUploadRepository();
        var function = CreateFunction(faceApi, faceRepository, uploadRepository);

        await function.ProcessUploadEventAsync(CreateUploadEvent(), CancellationToken.None);

        Assert.Equal([firstRectangle, secondRectangle], faceApi.EnrolledFaces.Select(call => call.Rectangle));
        Assert.Equal(["person-1", "person-2"], faceApi.DynamicGroupMembers);
        Assert.Equal(2, faceRepository.Created.Count);
        Assert.All(faceRepository.Created, record => Assert.Equal("dynamic-group", record.RecognitionGroupId));
        Assert.Equal((UploadStatuses.Completed, 2), Assert.Single(uploadRepository.Statuses));
    }

    private static ProcessPhotoFunction CreateFunction(
        FakeFaceApi faceApi,
        FakeFaceRepository faceRepository,
        FakeUploadRepository uploadRepository)
    {
        return new ProcessPhotoFunction(
            new FakeBlobStorageService(),
            faceApi,
            faceRepository,
            uploadRepository,
            NullLogger<ProcessPhotoFunction>.Instance);
    }

    private static PhotoUploadedEvent CreateUploadEvent() => new()
    {
        UploadId = "upload-1",
        BlobUrl = "https://storage.test/photos/upload-1.jpg",
        ContainerName = "photos",
        BlobName = "upload-1.jpg",
        TimestampUtc = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)
    };

    private sealed class FakeBlobStorageService : IBlobStorageService
    {
        public Task<string> UploadPhotoAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> DownloadPhotoAsync(string containerName, string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task UploadPoisonMessageAsync(string blobName, string content, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeFaceApi : IFaceApiService
    {
        private int _nextPersonId;

        public string DynamicPersonGroupId => "dynamic-group";
        public IReadOnlyList<DetectedFace> DetectedFaces { get; init; } = [];
        public IReadOnlyList<IdentifyResult> IdentifyResults { get; init; } = [];
        public List<(string PersonId, FaceRectangle Rectangle)> EnrolledFaces { get; } = [];
        public List<string> DynamicGroupMembers { get; } = [];

        public Task<IReadOnlyList<DetectedFace>> DetectFacesAsync(Stream photo, CancellationToken cancellationToken)
        {
            photo.Dispose();
            return Task.FromResult(DetectedFaces);
        }

        public Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(IEnumerable<string> faceIds, CancellationToken cancellationToken) =>
            Task.FromResult(IdentifyResults);

        public Task EnsureDynamicPersonGroupExistsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> CreatePersonAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult($"person-{++_nextPersonId}");

        public Task AddPersonFaceAsync(
            string personId,
            Stream photo,
            FaceRectangle targetFace,
            CancellationToken cancellationToken)
        {
            Assert.Equal(0, photo.Position);
            EnrolledFaces.Add((personId, targetFace));
            photo.Dispose();
            return Task.CompletedTask;
        }

        public Task AddPersonToDynamicGroupAsync(string personId, CancellationToken cancellationToken)
        {
            DynamicGroupMembers.Add(personId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFaceRepository : ICosmosFaceRepository
    {
        public List<(string PersonId, string RecognitionGroupId, RecognitionEvent Sighting)> Created { get; } = [];
        public List<(string PersonId, RecognitionEvent Sighting)> Recorded { get; } = [];

        public Task<FaceRecord> CreateFaceRecordAsync(
            string personId,
            string recognitionGroupId,
            RecognitionEvent firstSighting,
            CancellationToken cancellationToken)
        {
            Created.Add((personId, recognitionGroupId, firstSighting));
            return Task.FromResult(new FaceRecord
            {
                Id = personId,
                PersonId = personId,
                PersonGroupId = recognitionGroupId,
                RecognitionHistory = [firstSighting]
            });
        }

        public Task<FaceRecord> RecordRecognitionAsync(
            string personId,
            RecognitionEvent sighting,
            CancellationToken cancellationToken)
        {
            Recorded.Add((personId, sighting));
            return Task.FromResult(new FaceRecord
            {
                Id = personId,
                PersonId = personId,
                PersonGroupId = "dynamic-group",
                RecognitionHistory = [sighting]
            });
        }
    }

    private sealed class FakeUploadRepository : IUploadRepository
    {
        public List<(string Status, int? Count)> Statuses { get; } = [];

        public Task<UploadRecord> CreateAsync(UploadRecord upload, CancellationToken cancellationToken) =>
            Task.FromResult(upload);

        public Task SetStatusAsync(
            string uploadId,
            string status,
            int? detectedFaceCount,
            string? failureSummary,
            CancellationToken cancellationToken)
        {
            Statuses.Add((status, detectedFaceCount));
            return Task.CompletedTask;
        }
    }
}
