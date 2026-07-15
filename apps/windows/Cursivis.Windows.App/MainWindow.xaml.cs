using Cursivis.Windows.App.Helpers;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Cursivis.Windows.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = ResourceText.Get("WindowTitle");
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(1180, 800));
        RootFrame.Content = new MainPage();
    }
}
