using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;

namespace Cursivis.Windows.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = ResourceText.Get("WindowTitle");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        NativeWindowPositioner.FitAndCenter(AppWindow, 1180, 800);
        RootFrame.Content = new MainPage();
    }

    internal void RefreshRuntimeStatus() =>
        (RootFrame.Content as MainPage)?.RefreshRuntimeStatus();
}
