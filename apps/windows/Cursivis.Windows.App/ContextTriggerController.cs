using System.Diagnostics;
using Cursivis.Application.Context;
using Cursivis.Application.Operations;
using Cursivis.Application.Presentation;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Domain.Models;
using Cursivis.Windows.App.Helpers;
using Cursivis.Windows.Platform.Foreground;
using Microsoft.UI.Xaml.Controls;

namespace Cursivis.Windows.App;

public sealed class ContextTriggerController : IDisposable
{
    private static readonly TimeSpan ContextLifetime = TimeSpan.FromMinutes(5);
    private readonly ISelectionCaptureService _capture;
    private readonly IContextTriggerService _trigger;
    private readonly ISelectionReplacementService _replacement;
    private readonly ITextInsertionService _insertion;
    private readonly IResultClipboardService _clipboard;
    private readonly IForegroundWindowActivator _activator;
    private readonly ContextOrbWindow _orb;
    private readonly ContextResultWindow _resultWindow;
    private readonly OperationCancellationRegistry _operations = new();
    private readonly ContextInteractionSession _session = new();
    private readonly ResultAutoCopyCoordinator _autoCopy;
    private readonly ModelIdentifier _model;
    private bool _disposed;

    public ContextTriggerController(
        ISelectionCaptureService capture,
        IContextTriggerService trigger,
        ISelectionReplacementService replacement,
        ITextInsertionService insertion,
        IResultClipboardService clipboard,
        IForegroundWindowActivator activator,
        ContextOrbWindow orb,
        ContextResultWindow resultWindow,
        ModelIdentifier model)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        _replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
        _insertion = insertion ?? throw new ArgumentNullException(nameof(insertion));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _autoCopy = new ResultAutoCopyCoordinator(_clipboard);
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
        _orb = orb ?? throw new ArgumentNullException(nameof(orb));
        _resultWindow = resultWindow ?? throw new ArgumentNullException(nameof(resultWindow));
        _model = model;

        _resultWindow.CopyRequested += OnCopyRequested;
        _resultWindow.InsertRequested += OnInsertRequested;
        _resultWindow.ReplaceRequested += OnReplaceRequested;
        _resultWindow.TakeActionRequested += OnTakeActionRequested;
        _resultWindow.MoreOptionsRequested += OnMoreOptionsRequested;
        _resultWindow.RefineRequested += OnMoreOptionsRequested;
        _resultWindow.CloseRequested += OnCloseRequested;
        _orb.CancelRequested += OnOrbCancelRequested;
        _orb.GuidedOperationRequested += OnGuidedOperationRequested;
    }

    public TimeSpan LastOrbPresentationLatency { get; private set; }

    public TimeSpan LastCaptureLatency { get; private set; }

    public TimeSpan LastModelLatency { get; private set; }

    public void Invoke()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = RunSafelyAsync();
    }

    public void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        _operations.CancelAll();
        _session.Clear();
        _resultWindow.Hide();
        _orb.ShowState(
            OrbPresentationState.Cancelled,
            ResourceText.Get("ContextOrbCancelledStatus"),
            ResourceText.Get("ContextOrbCancelledDetail"));
        _ = HideOrbAfterAsync(TimeSpan.FromMilliseconds(700));
    }

    public void DismissTransientResult()
    {
        if (_disposed)
        {
            return;
        }

        _operations.CancelAll();
        _resultWindow.Hide();
        _orb.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operations.Dispose();
        _resultWindow.CopyRequested -= OnCopyRequested;
        _resultWindow.InsertRequested -= OnInsertRequested;
        _resultWindow.ReplaceRequested -= OnReplaceRequested;
        _resultWindow.TakeActionRequested -= OnTakeActionRequested;
        _resultWindow.MoreOptionsRequested -= OnMoreOptionsRequested;
        _resultWindow.RefineRequested -= OnMoreOptionsRequested;
        _resultWindow.CloseRequested -= OnCloseRequested;
        _orb.CancelRequested -= OnOrbCancelRequested;
        _orb.GuidedOperationRequested -= OnGuidedOperationRequested;
    }

    private async Task RunSafelyAsync()
    {
        try
        {
            await RunAsync();
        }
        catch (OperationCanceledException)
        {
            // The initiating cancellation path owns the visible state. Emergency
            // cancel shows a bounded confirmation; outside dismissal already hid it.
        }
        catch (Exception)
        {
            _orb.Hide();
            _resultWindow.ShowFailure(
                ResourceText.Get("ContextResultUnexpectedFailureTitle"),
                ResourceText.Get("ContextResultUnexpectedFailureMessage"));
        }
    }

    private async Task RunAsync()
    {
        _operations.CancelAll();
        Guid operationValue = Guid.NewGuid();
        using OperationLease operation = _operations.Begin(operationValue);
        _session.Clear();
        _resultWindow.Hide();

        long invoked = Stopwatch.GetTimestamp();
        _orb.ShowState(
            OrbPresentationState.Thinking,
            ResourceText.Get("ContextOrbCapturingStatus"),
            ResourceText.Get("ContextOrbCancelHintText"));
        LastOrbPresentationLatency = Stopwatch.GetElapsedTime(invoked);

        SelectionCaptureResult capture = await _capture.CaptureAsync(ContextLifetime, operation.Token);
        LastCaptureLatency = capture.Duration;
        if (capture.Status != SelectionCaptureStatus.Captured || capture.Context is null)
        {
            if (capture.Status == SelectionCaptureStatus.Cancelled)
            {
                _orb.Hide();
                return;
            }

            _orb.Hide();
            _resultWindow.ShowFailure(
                ResourceText.Get("ContextResultNoSelectionTitle"),
                GetCaptureFailureMessage(capture.Status));
            return;
        }

        _session.Begin(capture.Context);
        _orb.ShowState(
            OrbPresentationState.Generating,
            GetCapturedContextStatus(capture.Context.Kind),
            ResourceText.Get("ContextOrbSmartModeDetail"));

        ContextTriggerExecutionResult execution = await _trigger.ExecuteAsync(
            capture.Context,
            _model,
            operation.Token);
        LastModelLatency = execution.Duration;
        if (execution.Status == ContextTriggerExecutionStatus.Cancelled)
        {
            _orb.Hide();
            return;
        }

        if (execution.Result is null)
        {
            _orb.Hide();
            _resultWindow.ShowFailure(
                ResourceText.Get("ContextResultProviderFailureTitle"),
                GetProviderFailureMessage(execution));
            return;
        }

        if (execution.Status == ContextTriggerExecutionStatus.NeedsGuidedMode)
        {
            _session.SetResult(capture.Context.Fingerprint, execution.Result);
            _orb.ShowGuidedOptions(capture.Context.Kind);
            return;
        }

        await PresentCompletedResultAsync(
            new OperationId(operationValue),
            capture.Context,
            execution.Result,
            operation.Token);
    }

    private async void OnCopyRequested(object? sender, EventArgs args)
    {
        ContextSessionAccess session = _session.GetCurrent();
        if (session.LatestResult is null)
        {
            return;
        }

        ResultClipboardWriteResult copy = await _clipboard.CopyTextAsync(session.LatestResult.FinalContent);
        ShowCopyNotice(copy);
    }

    private async void OnReplaceRequested(object? sender, EventArgs args)
    {
        try
        {
            await ReplaceAsync();
        }
        catch (OperationCanceledException)
        {
            // Preserve the state selected by Cancel or transient dismissal.
        }
        catch (Exception)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultReplaceFailedTitle"),
                ResourceText.Get("ContextResultReplaceFailedMessage"),
                InfoBarSeverity.Error);
        }
    }

    private async void OnInsertRequested(object? sender, EventArgs args)
    {
        try
        {
            await InsertAsync();
        }
        catch (OperationCanceledException)
        {
            // Preserve the state selected by Cancel or transient dismissal.
        }
        catch (Exception)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultInsertFailedTitle"),
                ResourceText.Get("ContextResultInsertFailedMessage"),
                InfoBarSeverity.Error);
        }
    }

    private void OnTakeActionRequested(object? sender, EventArgs args)
    {
        switch (_session.GetCurrent().LatestResult?.SuggestedAction.Type)
        {
            case SuggestedActionType.Copy:
                OnCopyRequested(sender, args);
                break;
            case SuggestedActionType.InsertText:
                OnInsertRequested(sender, args);
                break;
            case SuggestedActionType.ReplaceSelection:
                OnReplaceRequested(sender, args);
                break;
        }
    }

    private void OnMoreOptionsRequested(object? sender, EventArgs args)
    {
        ContextSessionAccess session = _session.GetCurrent();
        if (session.Status != ContextSessionAccessStatus.Available || session.Context is null)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultContextExpiredTitle"),
                ResourceText.Get("ContextResultContextExpiredMessage"),
                InfoBarSeverity.Error);
            return;
        }

        _orb.ShowGuidedOptions(session.Context.Kind);
    }

    private async void OnGuidedOperationRequested(
        object? sender,
        GuidedOperationRequestedEventArgs args)
    {
        try
        {
            await RunGuidedAsync(args.Operation, args.CustomInstruction);
        }
        catch (OperationCanceledException)
        {
            // Preserve the state selected by Cancel or transient dismissal.
        }
        catch (ArgumentException)
        {
            _orb.ShowState(
                OrbPresentationState.Error,
                ResourceText.Get("ContextResultProviderFailureTitle"),
                ResourceText.Get("ContextResultProviderFailureMessage"));
        }
        catch (Exception)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultUnexpectedFailureTitle"),
                ResourceText.Get("ContextResultUnexpectedFailureMessage"),
                InfoBarSeverity.Error);
        }
    }

    private async Task RunGuidedAsync(
        GuidedOperation guidedOperation,
        string? customInstruction)
    {
        ContextSessionAccess session = _session.GetCurrent();
        if (session.Status != ContextSessionAccessStatus.Available || session.Context is null)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultContextExpiredTitle"),
                ResourceText.Get("ContextResultContextExpiredMessage"),
                InfoBarSeverity.Error);
            return;
        }

        ContextSnapshot context = session.Context;
        _operations.CancelAll();
        Guid operationValue = Guid.NewGuid();
        using OperationLease operation = _operations.Begin(operationValue);
        _orb.ShowState(
            OrbPresentationState.Generating,
            ResourceText.Get("ContextOrbAnalyzingStatus"),
            ResourceText.Get("ContextOrbCancelHintText"));

        ContextTriggerExecutionResult execution = await _trigger.ExecuteGuidedAsync(
            context,
            guidedOperation,
            customInstruction,
            _model,
            operation.Token);
        LastModelLatency = execution.Duration;
        if (execution.Status == ContextTriggerExecutionStatus.Cancelled)
        {
            _orb.Hide();
            return;
        }

        if (execution.Result is null)
        {
            _orb.Hide();
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultProviderFailureTitle"),
                GetProviderFailureMessage(execution),
                InfoBarSeverity.Error);
            return;
        }

        await PresentCompletedResultAsync(
            new OperationId(operationValue),
            context,
            execution.Result,
            operation.Token);
    }

    private async Task PresentCompletedResultAsync(
        OperationId operationId,
        ContextSnapshot context,
        SmartResult result,
        CancellationToken cancellationToken)
    {
        ContextSessionAccess session = _session.GetCurrent();
        if (session.Context?.Fingerprint != context.Fingerprint)
        {
            return;
        }

        _session.SetResult(context.Fingerprint, result);
        _orb.ShowState(
            OrbPresentationState.Done,
            ResourceText.Get("ContextOrbReadyStatus"),
            ResourceText.Get("ContextOrbReadyDetail"));
        _resultWindow.ShowResult(result, guidedMode: false);

        ResultClipboardWriteResult copy = await _autoCopy.CopyFinalOnceAsync(
            operationId,
            result,
            cancellationToken);
        ShowCopyNotice(copy);
        _orb.Hide();
    }

    private void ShowCopyNotice(ResultClipboardWriteResult copy)
    {
        if (copy.Status == ResultClipboardWriteStatus.Cancelled)
        {
            return;
        }

        _resultWindow.ShowNotice(
            copy.Status == ResultClipboardWriteStatus.Copied
                ? ResourceText.Get("ContextResultCopiedTitle")
                : ResourceText.Get("ContextResultClipboardUnavailableTitle"),
            copy.Status == ResultClipboardWriteStatus.Copied
                ? ResourceText.Get("ContextResultCopiedMessage")
                : ResourceText.Get("ContextResultClipboardUnavailableMessage"),
            copy.Status == ResultClipboardWriteStatus.Copied
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Error);
    }

    private async Task ReplaceAsync()
    {
        ContextSessionAccess session = _session.GetCurrent();
        ContextSnapshot? currentContext = session.Context;
        SmartResult? currentResult = session.LatestResult;
        if (currentContext is null || currentResult is null ||
            !long.TryParse(
                currentContext.Target.WindowId,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out long handleValue))
        {
            return;
        }

        _resultWindow.Hide();
        _orb.ShowState(
            OrbPresentationState.Executing,
            ResourceText.Get("ContextOrbReplacingStatus"),
            ResourceText.Get("ContextOrbCancelHintText"));

        nint targetHandle = new(handleValue);
        if (!_activator.TryActivate(targetHandle))
        {
            _orb.Hide();
            _resultWindow.ShowResult(currentResult, guidedMode: false);
            _resultWindow.ShowNotice(
                ResourceText.Get("ContextResultReplaceFailedTitle"),
                ResourceText.Get("ContextResultTargetUnavailableMessage"),
                InfoBarSeverity.Error);
            return;
        }

        await Task.Delay(80);
        TextReplacementResult replacement = await _replacement.ReplaceAsync(
            currentContext,
            currentResult.FinalContent);
        if (replacement.Status == TextReplacementStatus.Replaced)
        {
            _orb.ShowState(
                OrbPresentationState.Done,
                ResourceText.Get("ContextOrbReplacedStatus"),
                ResourceText.Get("ContextOrbReplacedDetail"));
            _session.Clear();
            await HideOrbAfterAsync(TimeSpan.FromMilliseconds(900));
            return;
        }

        _orb.Hide();
        _resultWindow.ShowResult(currentResult, guidedMode: false);
        _resultWindow.ShowNotice(
            ResourceText.Get("ContextResultReplaceFailedTitle"),
            replacement.Status == TextReplacementStatus.StaleTarget
                ? ResourceText.Get("ContextResultTargetUnavailableMessage")
                : ResourceText.Get("ContextResultReplaceFailedMessage"),
            InfoBarSeverity.Error);
    }

    private async Task InsertAsync()
    {
        ContextSessionAccess session = _session.GetCurrent();
        ContextSnapshot? currentContext = session.Context;
        SmartResult? currentResult = session.LatestResult;
        if (currentContext is null || currentResult is null ||
            !long.TryParse(
                currentContext.Target.WindowId,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out long handleValue))
        {
            return;
        }

        _resultWindow.Hide();
        _orb.ShowState(
            OrbPresentationState.Executing,
            ResourceText.Get("ContextOrbInsertingStatus"),
            ResourceText.Get("ContextOrbCancelHintText"));

        nint targetHandle = new(handleValue);
        if (!_activator.TryActivate(targetHandle))
        {
            ShowInsertionFailure(currentResult, targetUnavailable: true);
            return;
        }

        await Task.Delay(80);
        TextInsertionResult insertion = await _insertion.InsertAsync(
            currentContext,
            currentResult.FinalContent);
        if (insertion.Status == TextInsertionStatus.Inserted)
        {
            _orb.ShowState(
                OrbPresentationState.Done,
                ResourceText.Get("ContextOrbInsertedStatus"),
                ResourceText.Get("ContextOrbInsertedDetail"));
            _session.Clear();
            await HideOrbAfterAsync(TimeSpan.FromMilliseconds(900));
            return;
        }

        ShowInsertionFailure(
            currentResult,
            insertion.Status == TextInsertionStatus.StaleTarget);
    }

    private void ShowInsertionFailure(SmartResult currentResult, bool targetUnavailable)
    {
        _orb.Hide();
        _resultWindow.ShowResult(currentResult, guidedMode: false);
        _resultWindow.ShowNotice(
            ResourceText.Get("ContextResultInsertFailedTitle"),
            targetUnavailable
                ? ResourceText.Get("ContextResultTargetUnavailableMessage")
                : ResourceText.Get("ContextResultInsertFailedMessage"),
            InfoBarSeverity.Error);
    }

    private void OnCloseRequested(object? sender, EventArgs args)
    {
        nint targetHandle = GetCurrentTargetHandle();
        _resultWindow.Hide();
        _orb.Hide();
        if (targetHandle != nint.Zero)
        {
            _ = _activator.TryActivate(targetHandle);
        }
    }

    private void OnOrbCancelRequested(object? sender, EventArgs args) => Cancel();

    private static string GetCapturedContextStatus(ContextKind kind) => kind switch
    {
        ContextKind.Image => ResourceText.Get("ContextOrbImageCapturedStatus"),
        ContextKind.Code => ResourceText.Get("ContextOrbCodeCapturedStatus"),
        _ => ResourceText.Get("ContextOrbTextCapturedStatus"),
    };

    private nint GetCurrentTargetHandle()
    {
        ContextSnapshot? context = _session.GetCurrent().Context;
        return context is not null && long.TryParse(
            context.Target.WindowId,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out long value)
            ? new nint(value)
            : nint.Zero;
    }

    private async Task HideOrbAfterAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        if (!_disposed)
        {
            _orb.Hide();
        }
    }

    private static string GetCaptureFailureMessage(SelectionCaptureStatus status) => status switch
    {
        SelectionCaptureStatus.ForegroundChanged => ResourceText.Get("ContextResultForegroundChangedMessage"),
        SelectionCaptureStatus.Unavailable => ResourceText.Get("ContextResultCaptureUnavailableMessage"),
        SelectionCaptureStatus.TimedOut => ResourceText.Get("ContextResultCaptureTimedOutMessage"),
        _ => ResourceText.Get("ContextResultNoSelectionMessage"),
    };

    private static string GetProviderFailureMessage(ContextTriggerExecutionResult execution)
    {
        if (execution.Status == ContextTriggerExecutionStatus.MalformedResponse)
        {
            return ResourceText.Get("ContextResultMalformedResponseMessage");
        }

        return execution.Failure?.Kind switch
        {
            Cursivis.Contracts.OpenAI.OpenAiFailureKind.Authentication =>
                ResourceText.Get("ContextResultAuthenticationMessage"),
            Cursivis.Contracts.OpenAI.OpenAiFailureKind.RateLimit =>
                ResourceText.Get("ContextResultRateLimitedMessage"),
            Cursivis.Contracts.OpenAI.OpenAiFailureKind.ModelUnavailable =>
                ResourceText.Get("ContextResultModelUnavailableMessage"),
            Cursivis.Contracts.OpenAI.OpenAiFailureKind.Network =>
                ResourceText.Get("ContextResultNetworkMessage"),
            _ => ResourceText.Get("ContextResultProviderFailureMessage"),
        };
    }
}
