using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Cursivis.Application.Context;
using Cursivis.Domain.Context;

namespace Cursivis.Windows.Platform.Selection;

public sealed class UiAutomationTextSelectionReader : ITextSelectionReader
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMilliseconds(750);
    private readonly SemaphoreSlim _singleRead = new(1, 1);

    public async Task<TextSelectionReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _singleRead.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return TextSelectionReadResult.Failed(
                TextSelectionReadStatus.Unavailable,
                ContextSource.UserInterfaceAutomation,
                "A previous UI Automation read is still completing.");
        }

        Task<TextSelectionReadResult> readTask = Task.Run(ReadCore, CancellationToken.None);
        _ = readTask.ContinueWith(
            static (_, state) => ((SemaphoreSlim)state!).Release(),
            _singleRead,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await readTask.WaitAsync(ReadTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return TextSelectionReadResult.Failed(
                TextSelectionReadStatus.TimedOut,
                ContextSource.UserInterfaceAutomation,
                "UI Automation selection detection timed out.");
        }
        catch (OperationCanceledException)
        {
            return TextSelectionReadResult.Failed(
                TextSelectionReadStatus.Cancelled,
                ContextSource.UserInterfaceAutomation,
                "Selection detection was cancelled.");
        }
    }

    private static TextSelectionReadResult ReadCore()
    {
        try
        {
            AutomationElement focused = AutomationElement.FocusedElement;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out object? patternObject) ||
                patternObject is not TextPattern textPattern ||
                textPattern.SupportedTextSelection == SupportedTextSelection.None)
            {
                return NoSelection("The focused control does not expose a selectable text pattern.");
            }

            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges.Length == 0)
            {
                return NoSelection("The focused control has no selected text range.");
            }

            var selected = new List<string>(ranges.Length);
            foreach (TextPatternRange range in ranges)
            {
                string value = range.GetText(ContextTriggerService.MaximumSelectionCharacters + 1);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    selected.Add(value);
                }
            }

            return selected.Count == 0
                ? NoSelection("The UI Automation selection was empty.")
                : TextSelectionReadResult.Captured(
                    string.Join(Environment.NewLine, selected),
                    ContextSource.UserInterfaceAutomation);
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or InvalidOperationException or
            COMException or UnauthorizedAccessException)
        {
            return TextSelectionReadResult.Failed(
                TextSelectionReadStatus.Unavailable,
                ContextSource.UserInterfaceAutomation,
                "UI Automation could not read the focused control.");
        }
    }

    private static TextSelectionReadResult NoSelection(string detail) =>
        TextSelectionReadResult.Failed(
            TextSelectionReadStatus.NoSelection,
            ContextSource.UserInterfaceAutomation,
            detail);
}
