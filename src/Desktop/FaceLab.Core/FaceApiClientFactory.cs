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

using Azure.Core;

namespace FaceLab.Core;

/// <summary>
/// Builds a <see cref="FaceApiClient"/> per run, so each run gets its own HttpClient
/// (base address follows the endpoint under test) and its own trace list.
/// </summary>
public sealed class FaceApiClientFactory : IFaceApiClientFactory
{
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<TokenCredential>? _credentialFactory;

    public FaceApiClientFactory(Func<HttpClient> httpClientFactory, Func<TokenCredential>? credentialFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        _credentialFactory = credentialFactory;
    }

    public IFaceApiClient Create(FaceLabOptions options)
    {
        var credential = options.AuthMode == FaceAuthMode.DefaultAzureCredential
            ? (_credentialFactory ?? throw new InvalidOperationException(
                "No token credential factory was configured, so DefaultAzureCredential auth is unavailable."))()
            : null;

        return new FaceApiClient(_httpClientFactory(), options, credential);
    }
}
