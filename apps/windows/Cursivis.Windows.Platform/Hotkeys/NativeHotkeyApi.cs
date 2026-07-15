using System.Runtime.InteropServices;

namespace Cursivis.Windows.Platform.Hotkeys;

public enum NativeHotkeyFailure
{
    None,
    Conflict,
    NotRegistered,
    OperatingSystemError,
}

public readonly record struct NativeHotkeyOperationResult(
    bool Succeeded,
    NativeHotkeyFailure Failure,
    int NativeErrorCode)
{
    public static NativeHotkeyOperationResult Success { get; } = new(true, NativeHotkeyFailure.None, 0);
}

public interface INativeHotkeyApi
{
    NativeHotkeyOperationResult Register(
        nint windowHandle,
        int registrationId,
        HotkeyChord chord);

    NativeHotkeyOperationResult Unregister(
        nint windowHandle,
        int registrationId);
}

public sealed class Win32NativeHotkeyApi : INativeHotkeyApi
{
    private const uint NoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const int ErrorHotkeyNotRegistered = 1419;

    public NativeHotkeyOperationResult Register(
        nint windowHandle,
        int registrationId,
        HotkeyChord chord)
    {
        EnsureWindows();
        if (RegisterHotKey(
                windowHandle,
                registrationId,
                (uint)chord.Modifiers | NoRepeat,
                chord.VirtualKey))
        {
            return NativeHotkeyOperationResult.Success;
        }

        var error = Marshal.GetLastWin32Error();
        return new NativeHotkeyOperationResult(
            false,
            error == ErrorHotkeyAlreadyRegistered
                ? NativeHotkeyFailure.Conflict
                : NativeHotkeyFailure.OperatingSystemError,
            error);
    }

    public NativeHotkeyOperationResult Unregister(
        nint windowHandle,
        int registrationId)
    {
        EnsureWindows();
        if (UnregisterHotKey(windowHandle, registrationId))
        {
            return NativeHotkeyOperationResult.Success;
        }

        var error = Marshal.GetLastWin32Error();
        return new NativeHotkeyOperationResult(
            false,
            error == ErrorHotkeyNotRegistered
                ? NativeHotkeyFailure.NotRegistered
                : NativeHotkeyFailure.OperatingSystemError,
            error);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Global hotkeys require Windows.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint windowHandle,
        int registrationId,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int registrationId);
}
