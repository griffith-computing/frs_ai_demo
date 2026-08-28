using System.Net;
using System.Text;
using Azure.Core;
using FrsAiDemo.PhotoUploadHarness;

namespace FrsAiDemo.PhotoUploadHarness.Tests;

public sealed class PhotoUploaderClientTests
{
    [Fact]
    public void Constructor_RemoteEndpointWithoutClientId_Throws()
    {
        using var httpClient = new HttpClient();
        var options = new HarnessOptions { BaseUrl = "https://function.example/api/photos" };

        var exception = Assert.Throws<ArgumentException>(() => new PhotoUploaderClient(httpClient, options));

        Assert.Contains("EntraClientId", exception.Message);
    }

    [Fact]
    public async Task UploadAsync_RemoteEndpoint_AddsEntraBearerToken()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var credential = new RecordingCredential();
        var options = new HarnessOptions
        {
            BaseUrl = "https://function.example/api/photos",
            EntraClientId = "11111111-2222-3333-4444-555555555555"
        };
        var uploader = new PhotoUploaderClient(httpClient, options, credential);
        var file = CreatePhoto();

        try
        {
            var result = await uploader.UploadAsync(file, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Bearer", handler.AuthorizationScheme);
            Assert.Equal("test-token", handler.AuthorizationParameter);
            Assert.Equal(["api://11111111-2222-3333-4444-555555555555/.default"], credential.Scopes);
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public async Task UploadAsync_LocalEndpoint_DoesNotAcquireOrSendToken()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var credential = new RecordingCredential();
        var options = new HarnessOptions { BaseUrl = "http://localhost:7071/api/photos" };
        var uploader = new PhotoUploaderClient(httpClient, options, credential);
        var file = CreatePhoto();

        try
        {
            var result = await uploader.UploadAsync(file, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Null(handler.AuthorizationScheme);
            Assert.Empty(credential.Scopes);
        }
        finally
        {
            file.Delete();
        }
    }

    private static FileInfo CreatePhoto()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harness-test-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, [0xff, 0xd8, 0xff]);
        return new FileInfo(path);
    }

    private sealed class RecordingCredential : TokenCredential
    {
        public string[] Scopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            return new AccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    """{"uploadId":"upload-1","blobUrl":"https://storage.test/photos/upload-1.jpg"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
