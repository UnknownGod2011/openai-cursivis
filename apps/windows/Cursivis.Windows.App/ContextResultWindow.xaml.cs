using System.Numerics;
using Cursivis.Application.Context;
using Cursivis.Application.Presentation;
using Cursivis.Domain.Interaction;
using Cursivis.Infrastructure.Storage.Persistence;
using Cursivis.Windows.App.Helpers;
using Cursivis.Windows.Platform.Overlays;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Cursivis.Windows.App;

public sealed partial class ContextResultWindow : Window
{
    private const int MinimumLogicalWidth = 780;
    private const int MinimumLogicalHeight = 300;
    private const int CornerRadius = 28;
    private readonly NativeOverlayWindow _overlay;
    private readonly InputNonClientPointerSource _nonClientPointerSource;
    private readonly OverlaySizeStore _sizeStore;
    private OverlaySize? _savedLogicalSize;
    private DetectedColor? _detectedColor;
    private ResultPanelPresentation _presentation = ResultPanelPresentation.Failure(
        "Waiting for context",
        "Select content and invoke Cursivis.");

    public ContextResultWindow()
    {
        InitializeComponent();
        Title = ResourceText.Get("ContextResultWindowTitle");
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);

        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _overlay = new NativeOverlayWindow(
            handle,
            noActivate: true,
            resizable: true,
            minimumLogicalWidth: MinimumLogicalWidth,
            minimumLogicalHeight: MinimumLogicalHeight);
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        _sizeStore = new OverlaySizeStore(
            CursivisStoragePaths.ForCurrentUser().ResultPanelSizeFile);
        _savedLogicalSize = _sizeStore.TryLoad();
        _overlay.MoveOrResizeCompleted += OnOverlayMoveOrResizeCompleted;
        OverlayRoot.ActualThemeChanged += OnActualThemeChanged;
        HeaderDragSurface.SizeChanged += OnHeaderDragSurfaceSizeChanged;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        ApplyPresentation(_presentation);
    }

    public event EventHandler? UndoRequested;

    public event EventHandler? InsertRequested;

    public event EventHandler? CopyRequested;

    public event EventHandler? ReplaceRequested;

    public event EventHandler? TakeActionRequested;

    public event EventHandler? MoreOptionsRequested;

    public event EventHandler? RefineRequested;

    public event EventHandler? SettingsRequested;

    public event Action<ElementTheme>? ThemeRequested;

    public event EventHandler? CloseRequested;

    public event Action<bool>? OverlayVisibilityChanged;

    public bool IsVisible => _overlay.IsVisible;

    public bool IsInteracting => _overlay.IsInteracting;

    public OverlayRectangle OverlayBounds => _overlay.GetBounds();

    public void ShowResult(SmartResult result, bool guidedMode)
    {
        _detectedColor = null;
        _presentation = ResultPanelPresentation.FromResult(result, guidedMode);
        if (guidedMode)
        {
            _presentation = _presentation.WithNotice(
                ResultPanelStatus.Notice,
                ResourceText.Get("ContextResultGuidedNoticeTitle"),
                ResourceText.Get("ContextResultGuidedNoticeMessage"));
        }

        ApplyPresentation(_presentation);
        ShowWithoutActivation();
    }

    public void ShowColorResult(SmartResult result, DetectedColor color)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(color);
        _detectedColor = color;
        _presentation = ResultPanelPresentation.FromResult(result, guidedMode: false) with
        {
            OperationLabel = ResourceText.Get("ContextResultColorDetectedOperation"),
            CanInsert = false,
            CanReplace = false,
            CanTakeAction = false,
            CanRefine = false,
        };
        ApplyPresentation(_presentation);
        ShowWithoutActivation();
    }

    public void ShowFailure(string heading, string message)
    {
        _detectedColor = null;
        _presentation = ResultPanelPresentation.Failure(heading, message);
        ApplyPresentation(_presentation);
        ShowWithoutActivation();
    }

    public void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        _presentation = _presentation.WithNotice(
            severity == InfoBarSeverity.Error ? ResultPanelStatus.Error : ResultPanelStatus.Notice,
            title,
            message);
        ApplyPresentation(_presentation);
    }

    public void SetRequestedTheme(ElementTheme theme)
    {
        OverlayRoot.RequestedTheme = theme;
        UpdateThemeIcon();
    }

    public void Hide()
    {
        bool wasVisible = _overlay.IsVisible;
        _overlay.Hide();
        _overlay.SetNoActivate(true);
        if (wasVisible)
        {
            OverlayVisibilityChanged?.Invoke(false);
        }
    }

    private void ApplyPresentation(ResultPanelPresentation presentation)
    {
        OperationLabelText.Text = presentation.OperationLabel;
        ContextLabelText.Text = presentation.ContextLabel;
        ResultText.Text = presentation.Content;
        bool showsColor = _detectedColor is not null;
        ColorPreviewColumn.Width = new GridLength(showsColor ? 104 : 0);
        ColorPreviewBorder.Visibility = showsColor ? Visibility.Visible : Visibility.Collapsed;
        if (_detectedColor is { } color)
        {
            ColorPreviewBorder.Background = new SolidColorBrush(
                global::Windows.UI.Color.FromArgb(255, color.Red, color.Green, color.Blue));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                ColorPreviewBorder,
                $"{color.ApproximateName}, {color.Hex}");
        }
        StreamingRing.IsActive = presentation.Status == ResultPanelStatus.Streaming;
        StreamingRing.Visibility = StreamingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;

        UndoButton.IsEnabled = presentation.CanUndo;
        InsertButton.IsEnabled = presentation.CanInsert;
        CopyButton.IsEnabled = presentation.CanCopy;
        ReplaceButton.IsEnabled = presentation.CanReplace;
        TakeActionButton.IsEnabled = presentation.CanTakeAction;
        RefineButton.IsEnabled = presentation.CanRefine;

        bool hasNotice = !string.IsNullOrWhiteSpace(presentation.NoticeTitle) &&
                         !string.IsNullOrWhiteSpace(presentation.NoticeMessage);
        NoticeBorder.Visibility = hasNotice ? Visibility.Visible : Visibility.Collapsed;
        NoticeTitleText.Text = presentation.NoticeTitle ?? string.Empty;
        NoticeMessageText.Text = presentation.NoticeMessage ?? string.Empty;
        bool isError = presentation.Status == ResultPanelStatus.Error;
        NoticeBorder.Background = GetThemeBrush(isError ? "OverlayErrorBrush" : "OverlayNoticeBrush");
        NoticeBorder.BorderBrush = GetThemeBrush(
            isError ? "OverlayErrorBorderBrush" : "OverlayHairlineBrush");
        NoticeIcon.Foreground = GetThemeBrush(
            isError ? "OverlayErrorTextBrush" : "OverlayMutedTextBrush");
        NoticeTitleText.Foreground = GetThemeBrush(
            isError ? "OverlayErrorTextBrush" : "OverlayTextBrush");
        NoticeMessageText.Foreground = GetThemeBrush(
            isError ? "OverlayErrorTextBrush" : "OverlayMutedTextBrush");
        NoticeIcon.Glyph = isError ? "\uE783" : "\uE946";
    }

    private void ShowWithoutActivation()
    {
        bool wasVisible = _overlay.IsVisible;
        OverlayRectangle bounds;
        if (_overlay.IsVisible)
        {
            bounds = NativeWindowPositioner.ClampCurrentPlacement(
                _overlay.WindowHandle,
                _overlay.GetBounds());
        }
        else
        {
            ResultPanelLogicalSize calculated = ResultPanelSizeCalculator.Calculate(
                _presentation.Content,
                !string.IsNullOrWhiteSpace(_presentation.NoticeMessage));
            OverlaySize logicalSize = _savedLogicalSize ?? new OverlaySize(
                calculated.Width,
                calculated.Height);
            logicalSize = new OverlaySize(
                Math.Max(MinimumLogicalWidth, logicalSize.Width),
                Math.Max(MinimumLogicalHeight, logicalSize.Height));
            bounds = NativeWindowPositioner.GetNearCursorPlacement(
                _overlay.WindowHandle,
                logicalSize.Width,
                logicalSize.Height);
        }

        AppWindow.Show(activateWindow: false);
        _overlay.Show(bounds, topmost: true, noActivate: false, CornerRadius);
        UpdateResizeRegions();
        if (!wasVisible)
        {
            OverlayVisibilityChanged?.Invoke(true);
        }

        StartEntranceAnimation();
    }

    private void StartEntranceAnimation()
    {
        var settings = new UISettings();
        if (!settings.AnimationsEnabled)
        {
            RootShell.Opacity = 1;
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(RootShell);
        Compositor compositor = visual.Compositor;
        visual.Opacity = 1;
        visual.Offset = Vector3.Zero;

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(110);
        Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 8, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.2f, 0.8f),
            new Vector2(0.2f, 1f)));
        rise.Duration = TimeSpan.FromMilliseconds(125);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation(nameof(Visual.Offset), rise);
    }

    private void OnOverlayPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (args.GetCurrentPoint(OverlayRoot).Properties.IsLeftButtonPressed && _overlay.IsNoActivate)
        {
            _ = _overlay.ActivateForInteraction();
        }
    }

    private void OnUndoClicked(object sender, RoutedEventArgs args) =>
        UndoRequested?.Invoke(this, EventArgs.Empty);

    private void OnInsertClicked(object sender, RoutedEventArgs args) =>
        InsertRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClicked(object sender, RoutedEventArgs args) =>
        CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnReplaceClicked(object sender, RoutedEventArgs args) =>
        ReplaceRequested?.Invoke(this, EventArgs.Empty);

    private void OnTakeActionClicked(object sender, RoutedEventArgs args) =>
        TakeActionRequested?.Invoke(this, EventArgs.Empty);

    private void OnMoreOptionsClicked(object sender, RoutedEventArgs args) =>
        MoreOptionsRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefineClicked(object sender, RoutedEventArgs args) =>
        RefineRequested?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClicked(object sender, RoutedEventArgs args) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnThemeClicked(object sender, RoutedEventArgs args)
    {
        ElementTheme requested = OverlayRoot.ActualTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
        SetRequestedTheme(requested);
        ThemeRequested?.Invoke(requested);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs args) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CopyButton.IsEnabled)
        {
            args.Handled = true;
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnReplaceAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ReplaceButton.IsEnabled)
        {
            args.Handled = true;
            ReplaceRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCloseAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateThemeIcon();
        ApplyPresentation(_presentation);
    }

    private Brush GetThemeBrush(string resourceKey)
    {
        string themeKey = new AccessibilitySettings().HighContrast
            ? "HighContrast"
            : OverlayRoot.ActualTheme == ElementTheme.Dark ? "Default" : "Light";
        IList<ResourceDictionary> dictionaries =
            Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries;
        for (int index = dictionaries.Count - 1; index >= 0; index--)
        {
            ResourceDictionary dictionary = dictionaries[index];
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out object? themeValue) &&
                themeValue is ResourceDictionary themeDictionary &&
                themeDictionary.TryGetValue(resourceKey, out object? resource) &&
                resource is Brush brush)
            {
                return brush;
            }
        }

        throw new InvalidOperationException($"Overlay theme resource '{resourceKey}' is unavailable.");
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            _overlay.UpdateRoundedRegion(CornerRadius);
            UpdateResizeRegions();
        }
    }

    private void UpdateResizeRegions()
    {
        int width = Math.Max(1, AppWindow.Size.Width);
        int height = Math.Max(1, AppWindow.Size.Height);
        double scale = OverlayRoot.XamlRoot?.RasterizationScale ?? 1d;
        int edge = Math.Clamp((int)Math.Round(10d * scale), 8, 20);

        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.LeftBorder,
            [new RectInt32(0, 0, edge, height)]);
        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.RightBorder,
            [new RectInt32(Math.Max(0, width - edge), 0, edge, height)]);
        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.TopBorder,
            [new RectInt32(0, 0, width, edge)]);
        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.BottomBorder,
            [new RectInt32(0, Math.Max(0, height - edge), width, edge)]);

        if (HeaderDragSurface.ActualWidth > 0 && HeaderDragSurface.ActualHeight > 0)
        {
            Point origin = HeaderDragSurface
                .TransformToVisual(OverlayRoot)
                .TransformPoint(new Point());
            _nonClientPointerSource.SetRegionRects(
                NonClientRegionKind.Caption,
                [new RectInt32(
                    (int)Math.Round(origin.X * scale),
                    (int)Math.Round(origin.Y * scale),
                    Math.Max(1, (int)Math.Round(HeaderDragSurface.ActualWidth * scale)),
                    Math.Max(1, (int)Math.Round(HeaderDragSurface.ActualHeight * scale))) ]);
        }
    }

    private void OnHeaderDragSurfaceSizeChanged(object sender, SizeChangedEventArgs args) =>
        UpdateResizeRegions();

    private async void OnOverlayMoveOrResizeCompleted(object? sender, EventArgs args)
    {
        try
        {
            OverlayRectangle current = _overlay.GetBounds();
            OverlayRectangle clamped = NativeWindowPositioner.ClampCurrentPlacement(
                _overlay.WindowHandle,
                current);
            if (clamped != current)
            {
                _overlay.Show(
                    clamped,
                    topmost: true,
                    noActivate: _overlay.IsNoActivate,
                    CornerRadius);
            }

            _savedLogicalSize = NativeWindowPositioner.GetLogicalSize(
                _overlay.WindowHandle,
                clamped);
            await _sizeStore.SaveAsync(_savedLogicalSize.Value);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Glyph = OverlayRoot.ActualTheme == ElementTheme.Dark ? "\uE706" : "\uE793";
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_overlay.IsVisible)
        {
            OverlayVisibilityChanged?.Invoke(false);
        }

        OverlayRoot.ActualThemeChanged -= OnActualThemeChanged;
        HeaderDragSurface.SizeChanged -= OnHeaderDragSurfaceSizeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        _nonClientPointerSource.ClearAllRegionRects();
        _overlay.MoveOrResizeCompleted -= OnOverlayMoveOrResizeCompleted;
        Closed -= OnClosed;
        _overlay.Dispose();
    }
}
