using System;
using System.IO;

using Microsoft.UI.Xaml;

namespace Cursivis.Windows.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "startup-error.txt"),
                exception.ToString());
            throw;
        }
    }
}
