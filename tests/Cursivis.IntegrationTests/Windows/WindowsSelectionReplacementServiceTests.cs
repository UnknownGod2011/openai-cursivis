using Cursivis.Application.Context;
using Cursivis.Domain.Context;
using Cursivis.Windows.Platform.Foreground;
using Cursivis.Windows.Platform.Selection;

namespace Cursivis.IntegrationTests.Windows;

public sealed class WindowsSelectionReplacementServiceTests
{
    [Fact]
    public async Task ReplaceAsync_SelectionChanged_RejectsBeforeClipboardMutation()
    {
        var service = new WindowsSelectionReplacementService(
            new FixedForegroundProvider(),
            [new StubReader(TextSelectionReadResult.Captured(
                "changed text",
                ContextSource.UserInterfaceAutomation))]);

        TextReplacementResult result = await service.ReplaceAsync(
            CreateContext("original text"),
            "replacement",
            CancellationToken.None);

        Assert.Equal(TextReplacementStatus.StaleTarget, result.Status);
        Assert.Contains("changed", result.SafeDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceAsync_SelectionUnavailable_RejectsBeforeClipboardMutation()
    {
        var service = new WindowsSelectionReplacementService(
            new FixedForegroundProvider(),
            [new StubReader(TextSelectionReadResult.Failed(
                TextSelectionReadStatus.NoSelection,
                ContextSource.UserInterfaceAutomation,
                "No selection."))]);

        TextReplacementResult result = await service.ReplaceAsync(
            CreateContext("original text"),
            "replacement",
            CancellationToken.None);

        Assert.Equal(TextReplacementStatus.UnsupportedTarget, result.Status);
    }

    [Fact]
    public async Task InsertAsync_TargetChanged_RejectsBeforeClipboardMutation()
    {
        var service = new WindowsTextInsertionService(
            new FixedForegroundProvider(windowHandle: new nint(2)));

        TextInsertionResult result = await service.InsertAsync(
            CreateContext("original text"),
            "inserted text",
            CancellationToken.None);

        Assert.Equal(TextInsertionStatus.StaleTarget, result.Status);
    }

    private static ContextSnapshot CreateContext(string text) => ContextSnapshot.FromText(
        ContextKind.Text,
        ContextSource.UserInterfaceAutomation,
        new TargetIdentity("notepad", "1"),
        text,
        DateTimeOffset.UtcNow,
        TimeSpan.FromMinutes(5));

    private sealed class FixedForegroundProvider(nint? windowHandle = null)
        : IForegroundWindowIdentityProvider
    {
        public ForegroundWindowIdentity GetCurrent() =>
            new(7, "notepad", "Draft", windowHandle ?? new nint(1));
    }

    private sealed class StubReader(TextSelectionReadResult result) : ITextSelectionReader
    {
        public Task<TextSelectionReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
