using System.Numerics;
using Cursivis.Application.Presentation;
using Cursivis.Domain.Context;
using Cursivis.Domain.Interaction;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Windows.App.Helpers;
using Cursivis.Windows.Platform.Overlays;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Cursivis.Windows.App;

public sealed partial class ContextOrbWindow : Window
{
    private const int CompactLogicalWidth = 372;
    private const int CompactLogicalHeight = 68;
    private const int GuidedLogicalWidth = 448;
    private const int GuidedLogicalHeight = 392;
    private const int CornerRadius = 34;
    private readonly NativeOverlayWindow _overlay;
    private readonly OverlayPlacementStore _placementStore;
    private OverlayPoint? _placementPoint;
    private bool _userPlaced;
    private int _logicalWidth = CompactLogicalWidth;
    private int _logicalHeight = CompactLogicalHeight;

    public ContextOrbWindow()
    {
        InitializeComponent();
        Title = ResourceText.Get("ContextOrbWindowTitle");

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);

        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _overlay = new NativeOverlayWindow(handle, noActivate: true);
        _placementStore = new OverlayPlacementStore(
            CursivisStoragePaths.ForCurrentUser().OverlayPlacementFile);
        _placementPoint = _placementStore.TryLoad();
        _userPlaced = _placementPoint is not null;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        ApplyState(OrbPresentationState.Idle, "Ready", "Drag to place · microphone available");
    }

    public event EventHandler? CancelRequested;

    public event EventHandler? MicrophoneRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler<GuidedOperationRequestedEventArgs>? GuidedOperationRequested;

    public bool IsVisible => _overlay.IsVisible;

    public OverlayRectangle OverlayBounds => _overlay.GetBounds();

    public void ShowState(OrbPresentationState state, string status, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        SetGuidedPanelVisible(false);
        ApplyState(state, status, detail);
        ShowWithoutActivation();
    }

    public void ShowGuidedOptions(ContextKind contextKind)
    {
        BuildGuidedOptions(GuidedOperationCatalog.For(contextKind));
        SetGuidedPanelVisible(true);
        ApplyState(
            OrbPresentationState.Guiding,
            ResourceText.Get("ContextOrbGuidedStatus"),
            ResourceText.Get("ContextOrbGuidedDetail"));
        ShowWithoutActivation();
        _ = _overlay.ActivateForInteraction();
        FocusFirstGuidedControl();
    }

    public void SetRequestedTheme(ElementTheme theme) => OverlayRoot.RequestedTheme = theme;

    public void Hide()
    {
        SetGuidedPanelVisible(false);
        _overlay.Hide();
    }

    private void ApplyState(OrbPresentationState state, string status, string detail)
    {
        StatusText.Text = status.Trim();
        DetailText.Text = detail.Trim();
        StateIcon.Glyph = GetGlyph(state);
        MicrophoneButton.IsEnabled = state is OrbPresentationState.Idle or OrbPresentationState.Done;
        CancelButton.IsEnabled = state is not OrbPresentationState.Idle and not OrbPresentationState.Done;
        SetStateVisual(state);
        AutomationProperties.SetName(OverlayRoot, $"Cursivis {status}. {detail}");
        UpdateMotion(state);
    }

    private void ShowWithoutActivation()
    {
        OverlayRectangle bounds;
        if (_overlay.IsVisible)
        {
            OverlayRectangle current = _overlay.GetBounds();
            bounds = NativeWindowPositioner.GetRestoredPlacement(
                _overlay.WindowHandle,
                _logicalWidth,
                _logicalHeight,
                new OverlayPoint(current.X, current.Y));
        }
        else if (_userPlaced && _placementPoint is OverlayPoint restored)
        {
            bounds = NativeWindowPositioner.GetRestoredPlacement(
                _overlay.WindowHandle,
                _logicalWidth,
                _logicalHeight,
                restored);
        }
        else
        {
            bounds = NativeWindowPositioner.GetNearCursorPlacement(
                _overlay.WindowHandle,
                _logicalWidth,
                _logicalHeight);
        }

        AppWindow.Show(activateWindow: false);
        _overlay.Show(bounds, topmost: true, noActivate: true, CornerRadius);
        StartEntranceAnimation();
    }

    private void UpdateMotion(OrbPresentationState state)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(StateRing);
        visual.StopAnimation(nameof(Visual.Scale));
        visual.Scale = Vector3.One;

        if (state is not (
                OrbPresentationState.Listening or
                OrbPresentationState.Thinking or
                OrbPresentationState.Generating or
                OrbPresentationState.Speaking or
                OrbPresentationState.Guiding or
                OrbPresentationState.Executing) ||
            !new UISettings().AnimationsEnabled)
        {
            return;
        }

        Compositor compositor = visual.Compositor;
        Vector3KeyFrameAnimation pulse = compositor.CreateVector3KeyFrameAnimation();
        pulse.InsertKeyFrame(0, Vector3.One);
        pulse.InsertKeyFrame(0.5f, new Vector3(1.1f, 1.1f, 1));
        pulse.InsertKeyFrame(1, Vector3.One);
        pulse.Duration = TimeSpan.FromMilliseconds(state == OrbPresentationState.Listening ? 760 : 1100);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.CenterPoint = new Vector3(24, 24, 0);
        visual.StartAnimation(nameof(Visual.Scale), pulse);
    }

    private void StartEntranceAnimation()
    {
        if (!new UISettings().AnimationsEnabled)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(RootShell);
        Compositor compositor = visual.Compositor;
        visual.Opacity = 1;
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0);
        animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(90);
        visual.StartAnimation(nameof(Visual.Opacity), animation);
    }

    private async void OnDragSurfacePointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(DragSurface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        args.Handled = true;
        _ = _overlay.ActivateForInteraction();
        _overlay.BeginDrag();
        OverlayRectangle current = _overlay.GetBounds();
        OverlayRectangle clamped = NativeWindowPositioner.GetRestoredPlacement(
            _overlay.WindowHandle,
            _logicalWidth,
            _logicalHeight,
            new OverlayPoint(current.X, current.Y));
        _overlay.Show(clamped, topmost: true, noActivate: true, CornerRadius);
        _userPlaced = true;
        _placementPoint = new OverlayPoint(clamped.X, clamped.Y);
        try
        {
            await _placementStore.SaveAsync(_placementPoint.Value);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnMicrophoneClicked(object sender, RoutedEventArgs args) =>
        MicrophoneRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClicked(object sender, RoutedEventArgs args) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCancelClicked(object sender, RoutedEventArgs args) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnGuidedOperationClicked(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: GuidedOperation operation })
        {
            GuidedOperationRequested?.Invoke(
                this,
                new GuidedOperationRequestedEventArgs(operation, customInstruction: null));
        }
    }

    private void OnRunCustomTaskClicked(object sender, RoutedEventArgs args) =>
        RunCustomTask();

    private void OnCustomTaskTextChanged(object sender, TextChangedEventArgs args) =>
        RunCustomTaskButton.IsEnabled = !string.IsNullOrWhiteSpace(CustomTaskTextBox.Text);

    private void OnCustomTaskKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && RunCustomTaskButton.IsEnabled)
        {
            args.Handled = true;
            RunCustomTask();
        }
    }

    private void OnCancelAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        AppWindow.Changed -= OnAppWindowChanged;
        Closed -= OnClosed;
        _overlay.Dispose();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            _overlay.UpdateRoundedRegion(CornerRadius);
        }
    }

    private void SetStateVisual(OrbPresentationState state)
    {
        Border[] visuals =
        [
            IdleStateVisual,
            ListeningStateVisual,
            ThinkingStateVisual,
            GeneratingStateVisual,
            SpeakingStateVisual,
            GuidingStateVisual,
            ExecutingStateVisual,
            DoneStateVisual,
            CancelledStateVisual,
            ErrorStateVisual,
        ];

        for (int index = 0; index < visuals.Length; index++)
        {
            visuals[index].Visibility = index == (int)state ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static string GetGlyph(OrbPresentationState state) => state switch
    {
        OrbPresentationState.Idle => "\uE768",
        OrbPresentationState.Listening => "\uE720",
        OrbPresentationState.Thinking => "\uE895",
        OrbPresentationState.Generating => "\uE9D9",
        OrbPresentationState.Speaking => "\uE767",
        OrbPresentationState.Guiding => "\uE8F1",
        OrbPresentationState.Executing => "\uE90F",
        OrbPresentationState.Done => "\uE73E",
        OrbPresentationState.Cancelled => "\uE711",
        OrbPresentationState.Error => "\uE783",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private void BuildGuidedOptions(IReadOnlyList<GuidedOperation> operations)
    {
        GuidedOptionsGrid.Children.Clear();
        GuidedOptionsGrid.RowDefinitions.Clear();
        GuidedOptionsGrid.ColumnDefinitions.Clear();
        GuidedOptionsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        GuidedOptionsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        GuidedOperation[] visible = operations
            .Where(static operation => operation != GuidedOperation.CustomTask)
            .ToArray();
        int rowCount = (visible.Length + 1) / 2;
        for (int row = 0; row < rowCount; row++)
        {
            GuidedOptionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int index = 0; index < visible.Length; index++)
        {
            GuidedOperation operation = visible[index];
            var button = new Button
            {
                Content = ResourceText.Get($"GuidedOperation{operation}"),
                Tag = operation,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["OverlayPillButtonStyle"],
            };
            AutomationProperties.SetAutomationId(button, $"GuidedOperation{operation}");
            AutomationProperties.SetName(button, ResourceText.Get($"GuidedOperation{operation}"));
            button.Click += OnGuidedOperationClicked;
            Grid.SetRow(button, index / 2);
            Grid.SetColumn(button, index % 2);
            GuidedOptionsGrid.Children.Add(button);
        }
    }

    private void SetGuidedPanelVisible(bool visible)
    {
        GuidedPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _logicalWidth = visible ? GuidedLogicalWidth : CompactLogicalWidth;
        _logicalHeight = visible ? GuidedLogicalHeight : CompactLogicalHeight;
        if (!visible)
        {
            CustomTaskTextBox.Text = string.Empty;
        }
    }

    private void FocusFirstGuidedControl()
    {
        if (GuidedOptionsGrid.Children.FirstOrDefault() is Control first)
        {
            _ = first.Focus(FocusState.Programmatic);
            return;
        }

        _ = CustomTaskTextBox.Focus(FocusState.Programmatic);
    }

    private void RunCustomTask()
    {
        string instruction = CustomTaskTextBox.Text.Trim();
        if (instruction.Length == 0)
        {
            return;
        }

        GuidedOperationRequested?.Invoke(
            this,
            new GuidedOperationRequestedEventArgs(GuidedOperation.CustomTask, instruction));
    }
}

public sealed class GuidedOperationRequestedEventArgs(
    GuidedOperation operation,
    string? customInstruction) : EventArgs
{
    public GuidedOperation Operation { get; } = operation;

    public string? CustomInstruction { get; } = customInstruction;
}
