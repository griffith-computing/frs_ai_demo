using System.Net;
using Azure.Core;
using Azure.Identity;
using FaceLab.Core;
using FaceLab.Core.Data;
using FaceLab.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FaceLab;

public static class MauiProgram
{
    public const string DatabaseFileName = "facelab-blazor.db";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.Configure<FaceLabOptions>(options =>
        {
            options.AuthMode = FaceAuthMode.DefaultAzureCredential;
        });

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
        builder.Services.AddDbContextFactory<FaceLabDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All
        };

        builder.Services.AddSingleton<IFaceApiClientFactory>(_ => new FaceApiClientFactory(
            () => new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) },
            () => (TokenCredential)new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeManagedIdentityCredential = true
            })));
        builder.Services.AddScoped<IFaceLabRepository, FaceLabRepository>();
        builder.Services.AddScoped<IFaceRunner, FaceRunner>();
        builder.Services.AddScoped<IFaceLabWorkbench, FaceLabWorkbench>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
