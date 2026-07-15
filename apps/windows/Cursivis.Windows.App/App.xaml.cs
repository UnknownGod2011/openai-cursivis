using System;
using System.IO;

using Microsoft.UI.Xaml;

namespace Cursivis.Windows.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private AppRuntime? _runtime;

    public static AppRuntime? CurrentRuntime => (Microsoft.UI.Xaml.Application.Current as App)?._runtime;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Closed += OnMainWindowClosed;
            _window.Activate();
            nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _runtime = await AppRuntime.CreateAsync(windowHandle);
#if DEBUG
            string? visualValidation = Environment.GetEnvironmentVariable("CURSIVIS_VISUAL_VALIDATION");
            if (!string.IsNullOrWhiteSpace(visualValidation))
            {
                _window.AppWindow.Hide();
                _runtime.ShowVisualValidation(visualValidation);
            }
#endif
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "startup-error.txt"),
                $"{exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
            _runtime = null;
        }
    }
}
