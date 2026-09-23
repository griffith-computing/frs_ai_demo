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

namespace FaceLab.Core.Tests;

public sealed class FaceLabOptionsTests
{
    [Fact]
    public void Defaults_match_the_function_app_pipeline()
    {
        var options = new FaceLabOptions();

        Assert.Equal("face/v1.2-preview.1", options.ApiVersionSegment);
        Assert.Equal("detection_03", options.DetectionModel);
        Assert.Equal("recognition_04", options.RecognitionModel);
        Assert.Equal("frs-ai-demo-group", options.DynamicPersonGroupId);
        Assert.Equal(0.6, options.ConfidenceThreshold);
        Assert.Equal(2, options.MaxCandidatesReturned);
        Assert.Equal(10, options.IdentifyBatchSize);
        Assert.Equal(2, options.RequiredEnrollmentImages);
        Assert.Equal(0.8, options.ProvisionalVerificationThreshold);
        Assert.Equal(30, options.ProvisionalExpirationDays);
    }

    [Fact]
    public void Validate_accepts_a_complete_configuration()
    {
        Assert.Empty(TestOptions.Create().Validate());
    }

    [Fact]
    public void Validate_requires_an_endpoint()
    {
        var errors = TestOptions.Create(o => o.Endpoint = string.Empty).Validate();

        Assert.Contains(errors, e => e.Contains("Endpoint is required"));
    }

    [Fact]
    public void Validate_rejects_a_relative_endpoint()
    {
        var errors = TestOptions.Create(o => o.Endpoint = "not-a-uri").Validate();

        Assert.Contains(errors, e => e.Contains("absolute URI"));
    }

    [Fact]
    public void Validate_requires_a_subscription_key_only_in_key_mode()
    {
        var keyMode = TestOptions.Create(o => o.SubscriptionKey = string.Empty).Validate();
        Assert.Contains(keyMode, e => e.Contains("subscription key is required"));

        var credentialMode = TestOptions.Create(o =>
        {
            o.SubscriptionKey = string.Empty;
            o.AuthMode = FaceAuthMode.DefaultAzureCredential;
        }).Validate();
        Assert.Empty(credentialMode);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_rejects_an_out_of_range_confidence_threshold(double threshold)
    {
        var errors = TestOptions.Create(o => o.ConfidenceThreshold = threshold).Validate();

        Assert.Contains(errors, e => e.Contains("Confidence threshold"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Validate_enforces_the_face_api_identify_batch_limit(int batchSize)
    {
        var errors = TestOptions.Create(o => o.IdentifyBatchSize = batchSize).Validate();

        Assert.Contains(errors, e => e.Contains("Identify batch size"));
    }

    [Fact]
    public void Validate_rejects_an_unsafe_provisional_policy()
    {
        var errors = TestOptions.Create(options =>
        {
            options.MaxCandidatesReturned = 1;
            options.RequiredEnrollmentImages = 1;
            options.ProvisionalVerificationThreshold = 1.1;
            options.TemplateLearningThreshold = 0.5;
            options.ProvisionalExpirationDays = 0;
        }).Validate();

        Assert.Contains(errors, error => error.Contains("Max candidates"));
        Assert.Contains(errors, error => error.Contains("Required enrollment images"));
        Assert.Contains(errors, error => error.Contains("Provisional verification threshold"));
        Assert.Contains(errors, error => error.Contains("Template learning threshold"));
        Assert.Contains(errors, error => error.Contains("expiration"));
    }

    [Fact]
    public void BaseAddress_always_ends_with_a_single_slash()
    {
        Assert.Equal("https://face.example.com/", TestOptions.Create(o => o.Endpoint = "https://face.example.com").BaseAddress);
        Assert.Equal("https://face.example.com/", TestOptions.Create(o => o.Endpoint = "https://face.example.com///").BaseAddress);
    }

    [Fact]
    public void Snapshot_json_round_trips_without_the_subscription_key()
    {
        var options = TestOptions.Create(o =>
        {
            o.ConfidenceThreshold = 0.77;
            o.AuthMode = FaceAuthMode.DefaultAzureCredential;
            o.SubscriptionKey = "do-not-persist";
        });

        var json = options.ToSnapshotJson();
        Assert.DoesNotContain("do-not-persist", json);

        var restored = FaceLabOptions.FromSnapshotJson(json);
        Assert.Equal(0.77, restored.ConfidenceThreshold);
        Assert.Equal(FaceAuthMode.DefaultAzureCredential, restored.AuthMode);
        Assert.Equal(string.Empty, restored.SubscriptionKey);
    }

    [Fact]
    public void Clone_produces_an_independent_copy()
    {
        var options = TestOptions.Create();

        var clone = options.Clone();
        clone.ConfidenceThreshold = 0.1;

        Assert.NotSame(options, clone);
        Assert.Equal(0.6, options.ConfidenceThreshold);
    }

    [Fact]
    public void Operation_timings_are_clamped_to_a_positive_value()
    {
        var options = TestOptions.Create(o =>
        {
            o.OperationTimeoutSeconds = 0;
            o.OperationPollIntervalMilliseconds = 0;
        });

        Assert.Equal(TimeSpan.FromSeconds(1), options.OperationTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(1), options.OperationPollInterval);
    }
}
