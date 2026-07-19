using Cursivis.Application.Context;
using Cursivis.Application.Realtime;
using Cursivis.Windows.Platform.Foreground;
using Cursivis.Windows.Platform.Hotkeys;

namespace Cursivis.Windows.App;

internal sealed class LiveModeContextProvider(
    IHotkeyInputSettler inputSettler,
    ISelectionCaptureService selectionCapture,
    IForegroundWindowIdentityProvider foreground) : ILiveModeContextProvider
{
    private static readonly TimeSpan ContextLifetime = TimeSpan.FromMinutes(5);
    private readonly IHotkeyInputSettler _inputSettler = inputSettler
        ?? throw new ArgumentNullException(nameof(inputSettler));
    private readonly ISelectionCaptureService _selectionCapture = selectionCapture
        ?? throw new ArgumentNullException(nameof(selectionCapture));
    private readonly IForegroundWindowIdentityProvider _foreground = foreground
        ?? throw new ArgumentNullException(nameof(foreground));

    public async Task<LiveModeContext> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _inputSettler.WaitForModifiersReleasedAsync(cancellationToken);
        ForegroundWindowIdentity? active = _foreground.GetCurrent();
        SelectionCaptureResult capture = await _selectionCapture.CaptureAsync(
            ContextLifetime,
            cancellationToken);

        return capture.Status == SelectionCaptureStatus.Captured && capture.Context is not null
            ? new LiveModeContext(
                capture.Context.Text,
                capture.Context.Fingerprint.Value,
                capture.Context.Target.ApplicationId,
                active?.WindowTitle)
            : new LiveModeContext(
                null,
                null,
                active?.ProcessName,
                active?.WindowTitle);
    }
}
