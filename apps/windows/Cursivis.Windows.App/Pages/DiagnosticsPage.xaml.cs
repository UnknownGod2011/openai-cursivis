using System;
using System.Globalization;
using System.Reflection;

using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Cursivis.Windows.App.Pages;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsPage()
    {
        VersionText = GetVersionText();
        BuildCommitText = ResourceText.Get("BuildCommitNotEmbedded");
        DiagnosticsText = CreateSanitizedDiagnostics();
        InitializeComponent();
    }

    public string VersionText { get; }

    public string BuildCommitText { get; }

    public string DiagnosticsText { get; private set; }

    private void OnRefreshClicked(object sender, RoutedEventArgs args)
    {
        DiagnosticsText = CreateSanitizedDiagnostics();
        DiagnosticsTextBox.Text = DiagnosticsText;
        DiagnosticsActionStatus.Title = ResourceText.Get("DiagnosticsRefreshedTitle");
        DiagnosticsActionStatus.Message = ResourceText.Get("DiagnosticsRefreshedMessage");
        DiagnosticsActionStatus.Severity = InfoBarSeverity.Success;
        DiagnosticsActionStatus.IsOpen = true;
    }

    private void OnCopyClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            DataPackage package = new();
            package.SetText(DiagnosticsTextBox.Text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            DiagnosticsActionStatus.Title = ResourceText.Get("DiagnosticsCopiedTitle");
            DiagnosticsActionStatus.Message = ResourceText.Get("DiagnosticsCopiedMessage");
            DiagnosticsActionStatus.Severity = InfoBarSeverity.Success;
        }
        catch (Exception)
        {
            DiagnosticsActionStatus.Title = ResourceText.Get("DiagnosticsCopyFailedTitle");
            DiagnosticsActionStatus.Message = ResourceText.Get("DiagnosticsCopyFailedMessage");
            DiagnosticsActionStatus.Severity = InfoBarSeverity.Error;
        }

        DiagnosticsActionStatus.IsOpen = true;
    }

    private static string GetVersionText()
    {
        Version? version = typeof(DiagnosticsPage).Assembly.GetName().Version;
        return version?.ToString(3) ?? ResourceText.Get("VersionUnavailable");
    }

    private static string CreateSanitizedDiagnostics()
    {
        string generatedAt = DateTimeOffset.Now.ToString("G", CultureInfo.CurrentCulture);
        return string.Join(
            Environment.NewLine,
            ResourceText.Get("DiagnosticsReportTitle"),
            $"{ResourceText.Get("DiagnosticsGeneratedAtLabel")}: {generatedAt}",
            $"{ResourceText.Get("DiagnosticsReportVersionLabel")}: {GetVersionText()}",
            $"{ResourceText.Get("DiagnosticsReportOpenAiLabel")}: {ResourceText.Get("StatusNotConfigured")}",
            $"{ResourceText.Get("DiagnosticsReportBrowserLabel")}: {ResourceText.Get("StatusDisconnected")}",
            $"{ResourceText.Get("DiagnosticsReportHotkeysLabel")}: {ResourceText.Get("StatusNotRegistered")}",
            $"{ResourceText.Get("DiagnosticsReportMicrophoneLabel")}: {ResourceText.Get("StatusNotTested")}",
            $"{ResourceText.Get("DiagnosticsReportSecretsLabel")}: {ResourceText.Get("DiagnosticsSecretsExcluded")}");
    }
}
