using Cursivis.Application.Context;
using Cursivis.Domain.Actions;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;

namespace Cursivis.UnitTests;

public sealed class ContextInteractionSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetCurrent_AfterResultUpdate_ReusesOriginalContextAndFingerprint()
    {
        var time = new MutableTimeProvider(Now);
        var session = new ContextInteractionSession(time);
        ContextSnapshot context = CreateContext();
        SmartResult result = CreateResult("First result");

        session.Begin(context);
        session.SetResult(context.Fingerprint, result);
        ContextSessionAccess access = session.GetCurrent();

        Assert.Equal(ContextSessionAccessStatus.Available, access.Status);
        Assert.Same(context, access.Context);
        Assert.Same(result, access.LatestResult);
        Assert.Equal(context.Fingerprint, session.Fingerprint);
    }

    [Fact]
    public void GetCurrent_AfterExpiry_ClearsSensitiveContext()
    {
        var time = new MutableTimeProvider(Now);
        var session = new ContextInteractionSession(time);
        session.Begin(CreateContext());
        time.Now = Now.AddMinutes(6);

        ContextSessionAccess access = session.GetCurrent();

        Assert.Equal(ContextSessionAccessStatus.Expired, access.Status);
        Assert.Null(access.Context);
        Assert.Null(session.Fingerprint);
        Assert.Equal(ContextSessionAccessStatus.Missing, session.GetCurrent().Status);
    }

    [Fact]
    public async Task CopyFinalOnceAsync_RepeatedCompletion_CopiesExactlyOnce()
    {
        var clipboard = new RecordingClipboardService(ResultClipboardWriteStatus.Copied);
        var coordinator = new ResultAutoCopyCoordinator(clipboard);
        OperationId operation = OperationId.New();
        SmartResult result = CreateResult("Copied once");

        ResultClipboardWriteResult first = await coordinator.CopyFinalOnceAsync(operation, result);
        ResultClipboardWriteResult second = await coordinator.CopyFinalOnceAsync(operation, result);

        Assert.Equal(ResultClipboardWriteStatus.Copied, first.Status);
        Assert.Same(first, second);
        Assert.Equal(1, clipboard.CallCount);
        Assert.Equal("Copied once", clipboard.LastText);
    }

    [Fact]
    public async Task CopyFinalOnceAsync_ClipboardFailure_IsStableAndDoesNotLoseResult()
    {
        var clipboard = new RecordingClipboardService(ResultClipboardWriteStatus.Failed);
        var coordinator = new ResultAutoCopyCoordinator(clipboard);
        SmartResult result = CreateResult("Still visible");

        ResultClipboardWriteResult copy = await coordinator.CopyFinalOnceAsync(
            OperationId.New(),
            result);

        Assert.Equal(ResultClipboardWriteStatus.Failed, copy.Status);
        Assert.Equal("Still visible", result.FinalContent);
        Assert.Equal(1, clipboard.CallCount);
    }

    private static ContextSnapshot CreateContext() => ContextSnapshot.FromText(
        ContextKind.Text,
        ContextSource.UserInterfaceAutomation,
        new TargetIdentity("notepad", "00000001"),
        "Selected context",
        Now,
        TimeSpan.FromMinutes(5));

    private static SmartResult CreateResult(string content) => new(
        ContextKind.Text,
        SmartIntent.Rewrite,
        0.9,
        content,
        SuggestedAction.None,
        RiskLevel.Low,
        ConfirmationHint.None);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingClipboardService(ResultClipboardWriteStatus status)
        : IResultClipboardService
    {
        public int CallCount { get; private set; }

        public string? LastText { get; private set; }

        public Task<ResultClipboardWriteResult> CopyTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastText = text;
            return Task.FromResult(new ResultClipboardWriteResult(status, status.ToString()));
        }
    }
}
