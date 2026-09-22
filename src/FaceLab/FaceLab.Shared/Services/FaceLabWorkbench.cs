using FaceLab.Core;
using FaceLab.Core.Data;
using Microsoft.Extensions.Options;

namespace FaceLab.Shared.Services;

public sealed class FaceLabWorkbench : IFaceLabWorkbench
{
    private readonly IFaceLabRepository _repository;
    private readonly IFaceRunner _runner;

    public FaceLabWorkbench(
        IFaceLabRepository repository,
        IFaceRunner runner,
        IOptions<FaceLabOptions> options)
    {
        _repository = repository;
        _runner = runner;
        Options = options.Value.Clone();
    }

    public FaceLabOptions Options { get; }

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _repository.InitializeAsync(cancellationToken);

    public Task<CallTrace> TestConnectionAsync(CancellationToken cancellationToken) =>
        _runner.TestConnectionAsync(Options.Clone(), cancellationToken);

    public async Task<ImageRecord> SaveImageAsync(
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return await _repository.SaveImageAsync(fileName, contentType, buffer.ToArray(), cancellationToken);
    }

    public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(int take, CancellationToken cancellationToken) =>
        _repository.GetImagesAsync(take, cancellationToken);

    public async Task<RunRecord> RunAsync(int imageId, CancellationToken cancellationToken)
    {
        var image = await _repository.GetImageAsync(imageId, cancellationToken)
            ?? throw new InvalidOperationException($"Image {imageId} was not found.");
        return await _runner.RunAsync(image, Options.Clone(), cancellationToken);
    }

    public Task<IReadOnlyList<RunRecord>> GetRunsAsync(int take, CancellationToken cancellationToken) =>
        _repository.GetRunsAsync(take, cancellationToken);

    public Task<RunRecord?> GetRunAsync(int runId, CancellationToken cancellationToken) =>
        _repository.GetRunAsync(runId, cancellationToken);

    public Task<IReadOnlyList<PersonRecord>> GetPeopleAsync(CancellationToken cancellationToken) =>
        _repository.GetPeopleAsync(cancellationToken);
}
