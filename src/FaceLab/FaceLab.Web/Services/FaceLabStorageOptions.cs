namespace FaceLab.Web.Services;

public sealed class FaceLabStorageOptions
{
    public string AccountName { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "facelab-images";

    public string? ConnectionString { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ContainerName))
        {
            throw new InvalidOperationException("FaceLabStorage:ContainerName configuration is required.");
        }

        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(AccountName))
        {
            throw new InvalidOperationException(
                "FaceLabStorage:AccountName is required unless FaceLabStorage:ConnectionString is configured.");
        }
    }
}
