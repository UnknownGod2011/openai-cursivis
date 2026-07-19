using Microsoft.Win32;

namespace Cursivis.Windows.Platform.Startup;

public sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Cursivis";

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string command &&
                   !string.IsNullOrWhiteSpace(command);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows startup registration requires Windows.");
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException("The current-user startup registry key is unavailable.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The Cursivis executable path is unavailable.");
        key.SetValue(
            ValueName,
            $"\"{executable}\" --background",
            RegistryValueKind.String);
    }
}
