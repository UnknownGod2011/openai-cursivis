using System;

using Cursivis.Windows.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cursivis.Windows.App.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    internal event EventHandler<SettingsSectionRequestedEventArgs>? NavigationRequested;

    private void OnConfigureOpenAiClicked(object sender, RoutedEventArgs args)
    {
        RequestNavigation(SettingsSection.OpenAI);
    }

    private void OnReviewHotkeysClicked(object sender, RoutedEventArgs args)
    {
        RequestNavigation(SettingsSection.Hotkeys);
    }

    private void OnSetUpBrowserClicked(object sender, RoutedEventArgs args)
    {
        RequestNavigation(SettingsSection.Browser);
    }

    private void RequestNavigation(SettingsSection section)
    {
        NavigationRequested?.Invoke(this, new SettingsSectionRequestedEventArgs(section));
    }
}
