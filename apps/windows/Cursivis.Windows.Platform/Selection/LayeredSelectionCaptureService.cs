using System.Diagnostics;
using Cursivis.Application.Context;
using Cursivis.Domain.Context;
using Cursivis.Windows.Platform.Foreground;

namespace Cursivis.Windows.Platform.Selection;

public sealed class LayeredSelectionCaptureService : ISelectionCaptureService
{
    private readonly IForegroundWindowIdentityProvider _foreground;
    private readonly IReadOnlyList<ITextSelectionReader> _readers;
    private readonly TimeProvider _timeProvider;

    public LayeredSelectionCaptureService(
        IForegroundWindowIdentityProvider foreground,
        IEnumerable<ITextSelectionReader> readers,
        TimeProvider? timeProvider = null)
    {
        _foreground = foreground ?? throw new ArgumentNullException(nameof(foreground));
        ArgumentNullException.ThrowIfNull(readers);
        _readers = readers.ToArray();
        if (_readers.Count == 0)
        {
            throw new ArgumentException("At least one selection reader is required.", nameof(readers));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SelectionCaptureResult> CaptureAsync(
        TimeSpan contextLifetime,
        CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        ForegroundWindowIdentity? initial = _foreground.GetCurrent();
        if (initial is null || initial.WindowHandle == nint.Zero)
        {
            return SelectionCaptureResult.Failed(
                SelectionCaptureStatus.Unavailable,
                Stopwatch.GetElapsedTime(started),
                "The foreground application could not be identified.");
        }

        foreach (ITextSelectionReader reader in _readers)
        {
            // Clipboard fallback must remain on the caller's STA. WinUI invokes this
            // service from its UI thread, where OLE clipboard and WinRT clipboard
            // operations are initialized and safe to use.
            TextSelectionReadResult read = reader is IForegroundBoundTextSelectionReader boundReader
                ? await boundReader.ReadForWindowAsync(initial.WindowHandle, cancellationToken)
                : await reader.ReadAsync(cancellationToken);
            if (read.Status == TextSelectionReadStatus.Cancelled)
            {
                return SelectionCaptureResult.Failed(
                    SelectionCaptureStatus.Cancelled,
                    Stopwatch.GetElapsedTime(started),
                    read.SafeDetail);
            }

            if (read.Status != TextSelectionReadStatus.Captured || string.IsNullOrWhiteSpace(read.Text))
            {
                continue;
            }

            ForegroundWindowIdentity? current = _foreground.GetCurrent();
            if (!IsSameTarget(initial, current))
            {
                return SelectionCaptureResult.Failed(
                    SelectionCaptureStatus.ForegroundChanged,
                    Stopwatch.GetElapsedTime(started),
                    "The foreground application changed while Cursivis captured the selection.");
            }

            string text = read.Text.Trim();
            if (text.Length > ContextTriggerService.MaximumSelectionCharacters)
            {
                return SelectionCaptureResult.Failed(
                    SelectionCaptureStatus.Unavailable,
                    Stopwatch.GetElapsedTime(started),
                    "The selected text exceeds the current 50,000-character limit.");
            }

            string applicationId = string.IsNullOrWhiteSpace(initial.ProcessName)
                ? $"pid-{initial.ProcessId}"
                : initial.ProcessName;
            var target = new TargetIdentity(
                applicationId,
                $"{initial.WindowHandle.ToInt64():X}");
            ContextSnapshot context = ContextSnapshot.FromText(
                TextContextKindClassifier.Classify(text),
                read.Source,
                target,
                text,
                _timeProvider.GetUtcNow(),
                contextLifetime);
            return SelectionCaptureResult.Captured(context, Stopwatch.GetElapsedTime(started));
        }

        return SelectionCaptureResult.Failed(
            SelectionCaptureStatus.NoSelection,
            Stopwatch.GetElapsedTime(started),
            "No selected text was detected.");
    }

    private static bool IsSameTarget(
        ForegroundWindowIdentity expected,
        ForegroundWindowIdentity? actual) =>
        actual is not null &&
        expected.ProcessId == actual.ProcessId &&
        expected.WindowHandle == actual.WindowHandle;
}
