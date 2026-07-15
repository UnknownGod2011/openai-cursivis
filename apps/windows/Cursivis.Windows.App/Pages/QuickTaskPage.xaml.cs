using System.ComponentModel;

using Cursivis.Windows.App.Helpers;
using Cursivis.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cursivis.Windows.App.Pages;

public sealed partial class QuickTaskPage : Page
{
    internal QuickTaskSettingsViewModel ViewModel { get; } = new();

    public QuickTaskPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSaveState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        UpdateSaveState();
    }

    private void OnApprovalChanged(object sender, RoutedEventArgs args)
    {
        ViewModel.IsApproved = ApprovalCheckBox.IsChecked == true;
        UpdateSaveState();
    }

    private void OnRestorePromptOptimizerClicked(object sender, RoutedEventArgs args)
    {
        ViewModel.RestorePromptOptimizer();
        ApprovalCheckBox.IsChecked = false;
    }

    private void OnDiscardRequested(object? sender, System.EventArgs args)
    {
        ViewModel.DiscardChanges();
        ApprovalCheckBox.IsChecked = false;
    }

    private void UpdateSaveState()
    {
        if (SaveState is null)
        {
            return;
        }

        SaveState.IsDirty = ViewModel.IsDirty;
        SaveState.CanSave = false;
        SaveState.StateText = !ViewModel.IsDefinitionValid
            ? ViewModel.ValidationSummary
            : ViewModel.IsApproved
                ? ResourceText.Get("QuickTaskSaveStorageUnavailable")
                : ResourceText.Get("QuickTaskApprovalRequired");

        ValidationInfo.Severity = ViewModel.IsDefinitionValid
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Error;
    }
}
