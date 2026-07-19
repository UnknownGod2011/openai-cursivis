using Cursivis.Application.Realtime;
using Cursivis.Windows.App.Helpers;
using Cursivis.Windows.Platform.Overlays;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace Cursivis.Windows.App;

public sealed partial class LiveModeOverlayWindow : Window
{
    private const int LogicalWidth = 388;
    private const int LogicalHeight = 144;
    private const int CornerRadius = 22;
    private readonly TransparentWindowBackdrop _transparentBackdrop;
    private readonly NativeOverlayWindow _overlay;

    public LiveModeOverlayWindow()
    {
        InitializeComponent();
        Title = "Cursivis Live Mode";
        _transparentBackdrop = new TransparentWindowBackdrop(this);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);

        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _overlay = new NativeOverlayWindow(handle, noActivate: true);
        Closed += OnClosed;
    }

    public event EventHandler? StopRequested;

    public bool IsVisible => _overlay.IsVisible;

    public void ShowSnapshot(LiveModeSnapshot snapshot, OverlayRectangle orbBounds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        StatusText.Text = snapshot.Status;
        UserTranscriptText.Text = string.IsNullOrWhiteSpace(snapshot.UserTranscript)
            ? UserFallback(snapshot.State)
            : snapshot.UserTranscript;
        AssistantTranscriptText.Text = snapshot.State == LiveModeState.Error
            ? snapshot.SafeError ?? "Live Mode unavailable."
            : string.IsNullOrWhiteSpace(snapshot.AssistantTranscript)
                ? AssistantFallback(snapshot)
                : snapshot.AssistantTranscript;
        AudioLevelFill.Width = 72 * Math.Clamp(snapshot.AudioLevel, 0, 1);
        AutomationProperties.SetName(
            OverlayRoot,
            $"Cursivis Live Mode. {snapshot.Status}. {UserTranscriptText.Text}. {AssistantTranscriptText.Text}");

        if (!_overlay.IsVisible)
        {
            OverlayRectangle bounds = PlaceBesideOrb(orbBounds);
            AppWindow.Show(activateWindow: false);
            _overlay.Show(bounds, topmost: true, noActivate: true, CornerRadius);
            _ = DispatcherQueue.TryEnqueue(_overlay.EnforceBorderlessStyle);
        }
    }

    public void SetRequestedTheme(ElementTheme theme) => OverlayRoot.RequestedTheme = theme;

    public void Hide() => _overlay.Hide();

    private OverlayRectangle PlaceBesideOrb(OverlayRectangle orbBounds)
    {
        const int gap = 12;
        OverlayRectangle measured = NativeWindowPositioner.GetRestoredPlacement(
            _overlay.WindowHandle,
            LogicalWidth,
            LogicalHeight,
            new OverlayPoint(orbBounds.Right + gap, orbBounds.Y));
        int centeredY = orbBounds.Y + ((orbBounds.Height - measured.Height) / 2);
        OverlayRectangle right = NativeWindowPositioner.GetRestoredPlacement(
            _overlay.WindowHandle,
            LogicalWidth,
            LogicalHeight,
            new OverlayPoint(orbBounds.Right + gap, centeredY));
        if (right.X >= orbBounds.Right)
        {
            return right;
        }

        return NativeWindowPositioner.GetRestoredPlacement(
            _overlay.WindowHandle,
            LogicalWidth,
            LogicalHeight,
            new OverlayPoint(orbBounds.X - measured.Width - gap, centeredY));
    }

    private static string UserFallback(LiveModeState state) => state switch
    {
        LiveModeState.Connecting => "Preparing microphone…",
        LiveModeState.Listening => "Listening…",
        LiveModeState.UserSpeaking => "Listening…",
        _ => "—",
    };

    private static string AssistantFallback(LiveModeSnapshot snapshot) => snapshot.State switch
    {
        LiveModeState.Connecting => "Connecting to OpenAI Realtime…",
        LiveModeState.Thinking => "Thinking…",
        LiveModeState.ExecutingTool => snapshot.Status,
        LiveModeState.Error => snapshot.SafeError ?? "Live Mode unavailable.",
        _ => "Ready",
    };

    private void OnStopClicked(object sender, RoutedEventArgs args) =>
        StopRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnClosed;
        _transparentBackdrop.Dispose();
        _overlay.Dispose();
    }
}
