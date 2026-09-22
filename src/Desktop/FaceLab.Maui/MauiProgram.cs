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

using System.Net;
using Azure.Core;
using Azure.Identity;
using FaceLab.Core;
using FaceLab.Core.Data;
using FaceLab.Maui.Services;
using FaceLab.Maui.ViewModels;
using FaceLab.Maui.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FaceLab.Maui;

public static class MauiProgram
{
    public const string DatabaseFileName = "facelab.db";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
        builder.Services.AddDbContextFactory<FaceLabDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        builder.Services.AddSingleton<IFaceLabRepository, FaceLabRepository>();
        builder.Services.AddSingleton<IConfigStore, ConfigStore>();

        // One handler shared by every run; each run still gets its own HttpClient so the
        // base address can follow the endpoint currently under test.
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

        builder.Services.AddSingleton<IFaceRunner, FaceRunner>();

        builder.Services.AddSingleton<ConfigViewModel>();
        builder.Services.AddSingleton<UploadViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<PeopleViewModel>();
        builder.Services.AddTransient<RunDetailViewModel>();

        builder.Services.AddSingleton<ConfigPage>();
        builder.Services.AddSingleton<UploadPage>();
        builder.Services.AddSingleton<HistoryPage>();
        builder.Services.AddSingleton<PeoplePage>();
        builder.Services.AddTransient<RunDetailPage>();

        return builder.Build();
    }
}
