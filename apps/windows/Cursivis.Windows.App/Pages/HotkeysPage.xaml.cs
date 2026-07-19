using System;
using System.Collections.Generic;
using System.Linq;

using Cursivis.Windows.App.Controls;
using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Cursivis.Windows.Platform.Hotkeys;

namespace Cursivis.Windows.App.Pages;

public sealed partial class HotkeysPage : Page
{
    public HotkeysPage()
    {
        InitializeComponent();
        ShowRuntimeRegistrations();
    }

    private void ShowRuntimeRegistrations()
    {
        AppRuntime? runtime = App.CurrentRuntime;
        if (runtime is null)
        {
            return;
        }

        bool contextActive = runtime.ContextHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        bool cancelActive = runtime.CancelHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        bool takeActionActive = runtime.DirectTakeActionHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        if (contextActive)
        {
            ContextTriggerRecorder.ActiveChord = "Ctrl+Alt+O";
            ContextTriggerRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (cancelActive)
        {
            CancelRecorder.ActiveChord = "Ctrl+Alt+Escape";
            CancelRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (takeActionActive)
        {
            TakeActionRecorder.ActiveChord = "Ctrl+Alt+I";
            TakeActionRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (contextActive && cancelActive && takeActionActive)
        {
            HotkeysHeader.StatusText = ResourceText.Get("HotkeysActiveStatus");
            HotkeysHeader.StatusDetail = ResourceText.Get("HotkeysActiveStatusDetail");
            RegistrationInfo.Severity = InfoBarSeverity.Success;
            RegistrationInfo.Title = ResourceText.Get("HotkeysActiveInfoTitle");
            RegistrationInfo.Message = ResourceText.Get("HotkeysActiveInfoMessage");
        }
    }

    private IReadOnlyList<HotkeyRecorder> Recorders =>
    [
        ContextTriggerRecorder,
        LiveModeRecorder,
        TakeActionRecorder,
        DictationRecorder,
        QuickTaskRecorder,
        CancelRecorder,
        SettingsRecorder,
    ];

    private void OnCandidateCaptured(object? sender, HotkeyCandidateEventArgs args)
    {
        if (sender is not HotkeyRecorder recorder)
        {
            return;
        }

        int duplicateCount = Recorders.Count(
            item => string.Equals(item.ConfiguredChord, args.Chord, StringComparison.Ordinal));
        if (duplicateCount > 1)
        {
            recorder.SetExternalValidation(ResourceText.Get("HotkeyDuplicateValidation"));
            HotkeyPageStatus.Text = ResourceText.Get("HotkeyDuplicatePageStatus");
            return;
        }

        HotkeyPageStatus.Text = ResourceText.Get("HotkeyCandidatePageStatus");
    }

    private void OnRestoreDefaultsClicked(object sender, RoutedEventArgs args)
    {
        ContextTriggerRecorder.RestoreConfiguredChord("Ctrl+Alt+O");
        LiveModeRecorder.RestoreConfiguredChord("Ctrl+Alt+P");
        TakeActionRecorder.RestoreConfiguredChord("Ctrl+Alt+I");
        DictationRecorder.RestoreConfiguredChord("Ctrl+Alt+U");
        QuickTaskRecorder.RestoreConfiguredChord("Ctrl+Alt+Y");
        CancelRecorder.RestoreConfiguredChord("Ctrl+Alt+Escape");
        SettingsRecorder.RestoreConfiguredChord("Ctrl+Alt+S");
        HotkeyPageStatus.Text = ResourceText.Get("HotkeyDefaultsStagedStatus");
    }
}
