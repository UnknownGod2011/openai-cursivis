using System;
using System.Collections.Generic;
using System.Linq;

using Cursivis.Windows.App.Controls;
using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cursivis.Windows.App.Pages;

public sealed partial class HotkeysPage : Page
{
    public HotkeysPage()
    {
        InitializeComponent();
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
