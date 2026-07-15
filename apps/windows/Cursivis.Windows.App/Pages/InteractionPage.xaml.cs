using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cursivis.Windows.App.Pages;

public sealed partial class InteractionPage : Page
{
    private bool _isInitialized;

    public InteractionPage()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs args)
    {
        if (!_isInitialized)
        {
            return;
        }

        SaveState.IsDirty = true;
        SaveState.StateText = ResourceText.Get("SaveStateStorageUnavailable");
    }

    private void OnResetOrbPositionClicked(object sender, RoutedEventArgs args)
    {
        SaveState.IsDirty = true;
        SaveState.StateText = ResourceText.Get("OrbPositionResetStaged");
    }

    private void OnDiscardRequested(object? sender, System.EventArgs args)
    {
        SmartModeOption.IsChecked = true;
        AlwaysShowOrbToggle.IsOn = false;
        KeepResultsOpenToggle.IsOn = true;
        ActiveWindowCaptureOption.IsChecked = true;
        SaveState.IsDirty = false;
        SaveState.StateText = ResourceText.Get("SaveStateNoChanges");
    }
}
