using Cursivis.Contracts.OpenAI;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;

namespace Cursivis.Application.Context;

public enum SelectionCaptureStatus
{
    Captured,
    NoSelection,
    ForegroundChanged,
    TimedOut,
    Cancelled,
    Unavailable,
}

public sealed record SelectionCaptureResult(
    SelectionCaptureStatus Status,
    ContextSnapshot? Context,
    TimeSpan Duration,
    string SafeDetail)
{
    public static SelectionCaptureResult Captured(ContextSnapshot context, TimeSpan duration) =>
        new(SelectionCaptureStatus.Captured, context, duration, string.Empty);

    public static SelectionCaptureResult Failed(
        SelectionCaptureStatus status,
        TimeSpan duration,
        string safeDetail)
    {
        if (status == SelectionCaptureStatus.Captured)
        {
            throw new ArgumentException("A captured result requires context.", nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(safeDetail);
        return new SelectionCaptureResult(status, null, duration, safeDetail.Trim());
    }
}

public interface ISelectionCaptureService
{
    Task<SelectionCaptureResult> CaptureAsync(
        TimeSpan contextLifetime,
        CancellationToken cancellationToken = default);
}

public enum TextReplacementStatus
{
    Replaced,
    StaleTarget,
    UnsupportedTarget,
    Cancelled,
    Failed,
}

public sealed record TextReplacementResult(
    TextReplacementStatus Status,
    string SafeDetail);

public interface ISelectionReplacementService
{
    Task<TextReplacementResult> ReplaceAsync(
        ContextSnapshot expectedContext,
        string replacement,
        CancellationToken cancellationToken = default);
}

public enum TextInsertionStatus
{
    Inserted,
    StaleTarget,
    UnsupportedTarget,
    Cancelled,
    Failed,
}

public sealed record TextInsertionResult(
    TextInsertionStatus Status,
    string SafeDetail);

public interface ITextInsertionService
{
    Task<TextInsertionResult> InsertAsync(
        ContextSnapshot expectedContext,
        string text,
        CancellationToken cancellationToken = default);
}

public enum ContextTriggerExecutionStatus
{
    Completed,
    NeedsGuidedMode,
    ProviderFailed,
    MalformedResponse,
    Cancelled,
}

public sealed record ContextTriggerExecutionResult(
    ContextTriggerExecutionStatus Status,
    SmartResult? Result,
    OpenAiFailure? Failure,
    ModelIdentifier Model,
    TimeSpan Duration);

public interface IContextTriggerService
{
    Task<ContextTriggerExecutionResult> ExecuteAsync(
        ContextSnapshot context,
        ModelIdentifier model,
        CancellationToken cancellationToken = default);

    Task<ContextTriggerExecutionResult> ExecuteGuidedAsync(
        ContextSnapshot context,
        GuidedOperation operation,
        string? customInstruction,
        ModelIdentifier model,
        CancellationToken cancellationToken = default);
}

public enum ContextSessionAccessStatus
{
    Available,
    Missing,
    Expired,
}

public sealed record ContextSessionAccess(
    ContextSessionAccessStatus Status,
    ContextSnapshot? Context,
    SmartResult? LatestResult);

public enum ResultClipboardWriteStatus
{
    Copied,
    Failed,
    Cancelled,
}

public sealed record ResultClipboardWriteResult(
    ResultClipboardWriteStatus Status,
    string SafeDetail);

public interface IResultClipboardService
{
    Task<ResultClipboardWriteResult> CopyTextAsync(
        string text,
        CancellationToken cancellationToken = default);
}
