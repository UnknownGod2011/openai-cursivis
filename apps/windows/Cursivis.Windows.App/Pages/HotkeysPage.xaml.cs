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
        bool dictationActive = runtime.SmartDictationHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        bool liveModeActive = runtime.LiveModeHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        bool quickTaskActive = runtime.QuickTaskHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        bool settingsActive = runtime.SettingsHotkeyStatus.Status is
            HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
        if (contextActive)
        {
            ContextTriggerRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.ContextTriggerCommand);
            ContextTriggerRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (cancelActive)
        {
            CancelRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.CancelCommand);
            CancelRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (takeActionActive)
        {
            TakeActionRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.DirectTakeActionCommand);
            TakeActionRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (dictationActive)
        {
            DictationRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.SmartDictationCommand);
            DictationRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (liveModeActive)
        {
            LiveModeRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.RealtimeLiveModeCommand);
            LiveModeRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (quickTaskActive)
        {
            QuickTaskRecorder.ActiveChord = runtime.QuickTaskActiveChord;
            QuickTaskRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (settingsActive)
        {
            SettingsRecorder.ActiveChord = runtime.GetActiveHotkeyChord(AppRuntime.OpenSettingsCommand);
            SettingsRecorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
        }

        if (contextActive && cancelActive && takeActionActive && dictationActive && liveModeActive && quickTaskActive && settingsActive)
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

    private async void OnCandidateCaptured(object? sender, HotkeyCandidateEventArgs args)
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

        AppRuntime? runtime = App.CurrentRuntime;
        string? command = GetCommand(recorder);
        if (runtime is null || command is null)
        {
            HotkeyPageStatus.Text = ResourceText.Get("HotkeyCandidatePageStatus");
            return;
        }

        try
        {
            HotkeyUpdateResult result = await runtime.UpdateHotkeyAsync(command, args.Chord);
            if (result.Status is HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive)
            {
                recorder.ActiveChord = result.ActiveChord?.ToString() ?? args.Chord;
                recorder.StatusText = ResourceText.Get("HotkeyRegisteredStatus");
                HotkeyPageStatus.Text = ResourceText.Get("HotkeyRegistrationSucceededPageStatus");
                return;
            }

            recorder.ActiveChord = runtime.GetActiveHotkeyChord(command);
            recorder.StatusText = ResourceText.Get("QuickTaskHotkeyConflict");
            HotkeyPageStatus.Text = ResourceText.Get("HotkeyRegistrationFailedPageStatus");
        }
        catch (ArgumentException)
        {
            recorder.SetExternalValidation(ResourceText.Get("HotkeyReservedConflict"));
            HotkeyPageStatus.Text = ResourceText.Get("HotkeyRegistrationFailedPageStatus");
        }
    }

    private async void OnRestoreDefaultsClicked(object sender, RoutedEventArgs args)
    {
        (HotkeyRecorder Recorder, string Chord)[] defaults =
        [
            (ContextTriggerRecorder, "Ctrl+Alt+O"),
            (LiveModeRecorder, "Ctrl+Alt+P"),
            (TakeActionRecorder, "Ctrl+Alt+I"),
            (DictationRecorder, "Ctrl+Alt+U"),
            (QuickTaskRecorder, "Ctrl+Alt+Y"),
            (CancelRecorder, "Ctrl+Alt+Escape"),
            (SettingsRecorder, "Ctrl+Alt+S"),
        ];
        AppRuntime? runtime = App.CurrentRuntime;
        bool allRegistered = runtime is not null;
        foreach ((HotkeyRecorder recorder, string chord) in defaults)
        {
            recorder.RestoreConfiguredChord(chord);
            string? command = GetCommand(recorder);
            if (runtime is null || command is null)
            {
                allRegistered = false;
                continue;
            }

            HotkeyUpdateResult result = await runtime.UpdateHotkeyAsync(command, chord);
            bool registered = result.Status is HotkeyUpdateStatus.Success or HotkeyUpdateStatus.AlreadyActive;
            allRegistered &= registered;
            recorder.ActiveChord = result.ActiveChord?.ToString() ?? runtime.GetActiveHotkeyChord(command);
            recorder.StatusText = registered
                ? ResourceText.Get("HotkeyRegisteredStatus")
                : ResourceText.Get("QuickTaskHotkeyConflict");
        }

        HotkeyPageStatus.Text = ResourceText.Get(
            allRegistered
                ? "HotkeyDefaultsRegisteredStatus"
                : "HotkeyRegistrationFailedPageStatus");
    }

    private string? GetCommand(HotkeyRecorder recorder)
    {
        if (ReferenceEquals(recorder, ContextTriggerRecorder))
        {
            return AppRuntime.ContextTriggerCommand;
        }

        if (ReferenceEquals(recorder, LiveModeRecorder))
        {
            return AppRuntime.RealtimeLiveModeCommand;
        }

        if (ReferenceEquals(recorder, TakeActionRecorder))
        {
            return AppRuntime.DirectTakeActionCommand;
        }

        if (ReferenceEquals(recorder, DictationRecorder))
        {
            return AppRuntime.SmartDictationCommand;
        }

        if (ReferenceEquals(recorder, QuickTaskRecorder))
        {
            return AppRuntime.QuickTaskCommand;
        }

        if (ReferenceEquals(recorder, CancelRecorder))
        {
            return AppRuntime.CancelCommand;
        }

        return ReferenceEquals(recorder, SettingsRecorder)
            ? AppRuntime.OpenSettingsCommand
            : null;
    }
}
