using FaceLab.Core;
using FaceLab.Core.Data;

namespace FaceLab.Shared.Services;

public interface IFaceLabWorkbench
{
    FaceLabOptions Options { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task<CallTrace> TestConnectionAsync(CancellationToken cancellationToken);

    Task<ImageRecord> SaveImageAsync(
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ImageRecord>> GetImagesAsync(int take, CancellationToken cancellationToken);

    Task<RunRecord> RunAsync(int imageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunRecord>> GetRunsAsync(int take, CancellationToken cancellationToken);

    Task<RunRecord?> GetRunAsync(int runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PersonRecord>> GetPeopleAsync(CancellationToken cancellationToken);
}
