using Cursivis.Application.Context;
using Cursivis.Application.OpenAI;
using Cursivis.Contracts.OpenAI;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;

namespace Cursivis.UnitTests;

public sealed class ContextTriggerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ValidStructuredResult_ReturnsTypedSmartResult()
    {
        var gateway = new StubResponsesGateway(SuccessJson(confidence: 0.91));
        var service = new ContextTriggerService(gateway, new FixedTimeProvider(Now));

        ContextTriggerExecutionResult result = await service.ExecuteAsync(
            CreateInput(),
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.Completed, result.Status);
        Assert.Equal("A concise reply.", result.Result?.FinalContent);
        Assert.Equal("gpt-5.6-terra", gateway.LastRequest?.Model);
        Assert.Equal("cursivis_smart_result", gateway.LastRequest?.SchemaName);
        Assert.Contains("additionalProperties", gateway.LastRequest?.JsonSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_LowConfidence_RequestsGuidedModeWithoutDiscardingResult()
    {
        var service = new ContextTriggerService(
            new StubResponsesGateway(SuccessJson(confidence: 0.35)),
            new FixedTimeProvider(Now));

        ContextTriggerExecutionResult result = await service.ExecuteAsync(
            CreateInput(),
            ModelCatalog.Economy,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.NeedsGuidedMode, result.Status);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedStructuredResult_ReturnsSafeFailure()
    {
        var service = new ContextTriggerService(
            new StubResponsesGateway("{\"schemaVersion\":1,\"unexpected\":true}"),
            new FixedTimeProvider(Now));

        ContextTriggerExecutionResult result = await service.ExecuteAsync(
            CreateInput(),
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.MalformedResponse, result.Status);
        Assert.Equal(OpenAiFailureKind.MalformedResponse, result.Failure?.Kind);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderFailure_PreservesClassifiedSafeFailure()
    {
        var failure = new OpenAiFailure(OpenAiFailureKind.RateLimit, "Rate limited.", true);
        var service = new ContextTriggerService(
            new StubResponsesGateway(failure),
            new FixedTimeProvider(Now));

        ContextTriggerExecutionResult result = await service.ExecuteAsync(
            CreateInput(),
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.ProviderFailed, result.Status);
        Assert.Same(failure, result.Failure);
    }

    [Fact]
    public async Task ExecuteGuidedAsync_ValidOperation_UsesSameContextFingerprintWithoutConfidenceFallback()
    {
        var gateway = new StubResponsesGateway(SuccessJson(confidence: 0.20));
        var service = new ContextTriggerService(gateway, new FixedTimeProvider(Now));
        ContextSnapshot context = CreateContext();

        ContextTriggerExecutionResult result = await service.ExecuteGuidedAsync(
            ContextExecutionInput.FromText(context),
            GuidedOperation.Summarize,
            customInstruction: null,
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.Completed, result.Status);
        Assert.Equal(context.Fingerprint.Value, gateway.LastRequest?.OperationId);
        Assert.Contains("Requested operation: Summarize", gateway.LastRequest?.UserContent, StringComparison.Ordinal);
        Assert.Contains(context.Text!, gateway.LastRequest?.UserContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteGuidedAsync_CustomTaskWithoutInstruction_RejectsRequestBeforeGateway()
    {
        var gateway = new StubResponsesGateway(SuccessJson(confidence: 0.90));
        var service = new ContextTriggerService(gateway, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteGuidedAsync(
            CreateInput(),
            GuidedOperation.CustomTask,
            " ",
            ModelCatalog.Balanced,
            CancellationToken.None));

        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_ImageContext_UsesTypedResponsesVisionInput()
    {
        var gateway = new StubResponsesGateway(SuccessJson(confidence: 0.90));
        var service = new ContextTriggerService(gateway, new FixedTimeProvider(Now));
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
        ContextExecutionInput input = CreateImageInput(png);

        ContextTriggerExecutionResult result = await service.ExecuteAsync(
            input,
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.Completed, result.Status);
        Assert.Equal("image/png", gateway.LastRequest?.Image?.MediaType);
        Assert.True(gateway.LastRequest?.Image?.EncodedBytes.Span.SequenceEqual(png));
        Assert.Contains("captured image", gateway.LastRequest?.UserContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteGuidedAsync_ImageContext_ReusesFingerprintAndImagePayload()
    {
        var gateway = new StubResponsesGateway(SuccessJson(confidence: 0.90));
        var service = new ContextTriggerService(gateway, new FixedTimeProvider(Now));
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 8, 7, 6, 5];
        ContextExecutionInput input = CreateImageInput(png);

        ContextTriggerExecutionResult result = await service.ExecuteGuidedAsync(
            input,
            GuidedOperation.ExtractText,
            customInstruction: null,
            ModelCatalog.Balanced,
            CancellationToken.None);

        Assert.Equal(ContextTriggerExecutionStatus.Completed, result.Status);
        Assert.Equal(input.Context.Fingerprint.Value, gateway.LastRequest?.OperationId);
        Assert.NotNull(input.Image);
        Assert.True(gateway.LastRequest?.Image?.EncodedBytes.Span.SequenceEqual(png));
        Assert.Contains("attached user-captured image", gateway.LastRequest?.UserContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FromImage_MismatchedPayload_RejectsBeforeModelRequest()
    {
        ContextExecutionInput valid = CreateImageInput([1, 2, 3, 4]);
        var mismatched = new ContextImagePayload([4, 3, 2, 1], "image/png", 1, 1);

        Assert.Throws<ArgumentException>(() => ContextExecutionInput.FromImage(valid.Context, mismatched));
    }

    private static ContextSnapshot CreateContext() => ContextSnapshot.FromText(
        ContextKind.Text,
        ContextSource.UserInterfaceAutomation,
        new TargetIdentity("notepad", "00000001"),
        "Please reply to this email.",
        Now,
        TimeSpan.FromMinutes(5));

    private static ContextExecutionInput CreateInput() => ContextExecutionInput.FromText(CreateContext());

    private static ContextExecutionInput CreateImageInput(byte[] bytes)
    {
        byte[] digest = System.Security.Cryptography.SHA256.HashData(bytes);
        ContextSnapshot context = ContextSnapshot.FromImageDigest(
            ContextSource.RegionCapture,
            new TargetIdentity("desktop", "00000001"),
            digest,
            Now,
            TimeSpan.FromMinutes(5));
        return ContextExecutionInput.FromImage(
            context,
            new ContextImagePayload(bytes, "image/png", 1, 1));
    }

    private static string SuccessJson(double confidence) => $$"""
        {
          "schemaVersion": 1,
          "contextType": "email",
          "intent": "reply",
          "confidence": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "finalContent": "A concise reply.",
          "suggestedAction": {
            "type": "replace_selection",
            "summary": "Replace the selected draft"
          },
          "riskHint": "low",
          "confirmationHint": "none"
        }
        """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubResponsesGateway : IResponsesGateway
    {
        private readonly StructuredResponseResult _result;

        public StubResponsesGateway(string json)
            : this(StructuredResponseResult.Success(json, ModelCatalog.Balanced.Value, "response-1"))
        {
        }

        public StubResponsesGateway(OpenAiFailure failure)
            : this(StructuredResponseResult.Failed(failure))
        {
        }

        private StubResponsesGateway(StructuredResponseResult result)
        {
            _result = result;
        }

        public StructuredResponseRequest? LastRequest { get; private set; }

        public Task<StructuredResponseResult> CreateStructuredResponseAsync(
            StructuredResponseRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }

        public Task<ModelAvailabilityResult> CheckModelAvailabilityAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelAvailabilityResult(model, true, null, Now));
    }
}
