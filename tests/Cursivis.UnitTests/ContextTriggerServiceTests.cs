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
            CreateContext(),
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
            CreateContext(),
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
            CreateContext(),
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
            CreateContext(),
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
            context,
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
            CreateContext(),
            GuidedOperation.CustomTask,
            " ",
            ModelCatalog.Balanced,
            CancellationToken.None));

        Assert.Null(gateway.LastRequest);
    }

    private static ContextSnapshot CreateContext() => ContextSnapshot.FromText(
        ContextKind.Text,
        ContextSource.UserInterfaceAutomation,
        new TargetIdentity("notepad", "00000001"),
        "Please reply to this email.",
        Now,
        TimeSpan.FromMinutes(5));

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
