using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Cursivis.Windows.Platform.Overlays;

/// <summary>
/// Applies utility-overlay HWND behavior without coupling presentation state to WinUI.
/// </summary>
public sealed class NativeOverlayWindow : IDisposable
{
    private const int ExtendedStyleIndex = -20;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long ExtendedStyleAppWindow = 0x00040000L;
    private const long ExtendedStyleNoActivate = 0x08000000L;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const uint SetWindowPositionShowWindow = 0x0040;
    private const uint SetWindowPositionHideWindow = 0x0080;
    private const int ShowNoActivate = 4;
    private const int HideWindow = 0;
    private const uint WindowMessageNonClientHitTest = 0x0084;
    private const uint WindowMessageNonClientCalculateSize = 0x0083;
    private const uint WindowMessageNonClientLeftButtonDown = 0x00A1;
    private const uint WindowMessageSystemCommand = 0x0112;
    private const uint WindowMessageGetMinMaxInfo = 0x0024;
    private const uint WindowMessageEnterSizeMove = 0x0231;
    private const uint WindowMessageExitSizeMove = 0x0232;
    private const int HitTestClient = 1;
    private const int HitTestCaption = 2;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;
    private const int SystemCommandSize = 0xF000;
    private const uint MonitorDefaultToNearest = 2;
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const nuint OverlaySubclassId = 0x43555253;
    private static readonly nint Topmost = new(-1);
    private static readonly nint NotTopmost = new(-2);
    private readonly nint _windowHandle;
    private readonly SubclassProcedure? _subclassProcedure;
    private readonly bool _isResizable;
    private readonly int _minimumLogicalWidth;
    private readonly int _minimumLogicalHeight;
    private int _lastHitTest = HitTestClient;
    private bool _isInteracting;
    private bool _disposed;

    public NativeOverlayWindow(
        nint windowHandle,
        bool noActivate,
        bool resizable = false,
        int minimumLogicalWidth = 0,
        int minimumLogicalHeight = 0)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("An overlay HWND is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _isResizable = resizable;
        _minimumLogicalWidth = Math.Max(1, minimumLogicalWidth);
        _minimumLogicalHeight = Math.Max(1, minimumLogicalHeight);
        ConfigureUtilityStyles(noActivate);
        SuppressDwmBorder();
        if (_isResizable)
        {
            _subclassProcedure = WindowSubclassProcedure;
            if (!SetWindowSubclass(
                    _windowHandle,
                    _subclassProcedure,
                    OverlaySubclassId,
                    nint.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The overlay resize handler could not be installed.");
            }

            RefreshFrame();
        }
    }

    public event EventHandler? MoveOrResizeCompleted;

    public nint WindowHandle => _windowHandle;

    public bool IsNoActivate { get; private set; }

    public bool IsVisible => !_disposed && IsWindowVisible(_windowHandle);

    public bool IsInteracting => _isInteracting;

    public void SetNoActivate(bool noActivate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ConfigureUtilityStyles(noActivate);
    }

    public void Show(OverlayRectangle bounds, bool topmost, bool noActivate, int cornerRadius)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetNoActivate(noActivate);
        ApplyRoundedRegion(bounds.Width, bounds.Height, cornerRadius);

        // Showing an overlay should never take focus merely because it appeared.
        // WS_EX_NOACTIVATE remains a separate choice for status-only overlays.
        uint flags = SetWindowPositionShowWindow | SetWindowPositionNoActivate;

        if (!SetWindowPos(
                _windowHandle,
                topmost ? Topmost : NotTopmost,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The overlay could not be positioned.");
        }

        _ = ShowWindow(_windowHandle, ShowNoActivate);
    }

    public void UpdateRoundedRegion(int cornerRadius)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (GetWindowRect(_windowHandle, out NativeRectangle rectangle))
        {
            ApplyRoundedRegion(
                Math.Max(1, rectangle.Right - rectangle.Left),
                Math.Max(1, rectangle.Bottom - rectangle.Top),
                cornerRadius);
        }
    }

    public void BeginDrag()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _lastHitTest = HitTestCaption;
        _ = ReleaseCapture();
        _ = SendMessage(_windowHandle, 0x00A1, new nint(HitTestCaption), nint.Zero);
    }

    public bool ActivateForInteraction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetNoActivate(false);
        return SetForegroundWindow(_windowHandle);
    }

    public OverlayRectangle GetBounds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!GetWindowRect(_windowHandle, out NativeRectangle rectangle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The overlay bounds could not be read.");
        }

        return new(
            rectangle.Left,
            rectangle.Top,
            Math.Max(1, rectangle.Right - rectangle.Left),
            Math.Max(1, rectangle.Bottom - rectangle.Top));
    }

    public void Hide()
    {
        if (_disposed)
        {
            return;
        }

        _ = SetWindowPos(
            _windowHandle,
            NotTopmost,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove |
            SetWindowPositionNoSize |
            SetWindowPositionNoActivate |
            SetWindowPositionHideWindow);
        _ = ShowWindow(_windowHandle, HideWindow);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Hide();
        if (_subclassProcedure is not null)
        {
            _ = RemoveWindowSubclass(
                _windowHandle,
                _subclassProcedure,
                OverlaySubclassId);
        }

        _disposed = true;
    }

    private nint WindowSubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nint referenceData)
    {
        try
        {
            switch (message)
            {
                case WindowMessageNonClientCalculateSize when _isResizable:
                    return nint.Zero;
                case WindowMessageNonClientHitTest when _isResizable:
                    int hitTest = GetResizeHitTest(lParam);
                    if (hitTest != HitTestClient)
                    {
                        _lastHitTest = hitTest;
                        return new nint(hitTest);
                    }

                    break;
                case WindowMessageGetMinMaxInfo when _isResizable:
                    ApplyResizeConstraints(lParam);
                    return nint.Zero;
                case WindowMessageNonClientLeftButtonDown when
                    _isResizable && IsResizeHitTest((int)wParam) && IsNoActivate:
                    SetNoActivate(false);
                    _ = SetForegroundWindow(_windowHandle);
                    _ = ReleaseCapture();
                    _ = SendMessage(
                        windowHandle,
                        WindowMessageSystemCommand,
                        new nint(SystemCommandSize | GetResizeDirection((int)wParam)),
                        lParam);
                    return nint.Zero;
                case WindowMessageEnterSizeMove:
                    _isInteracting = true;
                    break;
                case WindowMessageExitSizeMove:
                    _isInteracting = false;
                    MoveOrResizeCompleted?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception)
        {
            _isInteracting = false;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private int GetResizeHitTest(nint lParam)
    {
        if (!GetWindowRect(_windowHandle, out NativeRectangle rectangle))
        {
            return HitTestClient;
        }

        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xFFFF));
        int y = unchecked((short)((packed >> 16) & 0xFFFF));
        int dpi = (int)Math.Max(96u, GetDpiForWindow(_windowHandle));
        int edge = Math.Max(6, (int)Math.Round(8d * dpi / 96d));
        int corner = Math.Max(edge * 2, (int)Math.Round(18d * dpi / 96d));

        bool left = x >= rectangle.Left && x < rectangle.Left + edge;
        bool right = x <= rectangle.Right && x > rectangle.Right - edge;
        bool top = y >= rectangle.Top && y < rectangle.Top + edge;
        bool bottom = y <= rectangle.Bottom && y > rectangle.Bottom - edge;
        bool nearLeftCorner = x < rectangle.Left + corner;
        bool nearRightCorner = x > rectangle.Right - corner;
        bool nearTopCorner = y < rectangle.Top + corner;
        bool nearBottomCorner = y > rectangle.Bottom - corner;

        if ((left && nearTopCorner) || (top && nearLeftCorner))
        {
            return HitTestTopLeft;
        }

        if ((right && nearTopCorner) || (top && nearRightCorner))
        {
            return HitTestTopRight;
        }

        if ((left && nearBottomCorner) || (bottom && nearLeftCorner))
        {
            return HitTestBottomLeft;
        }

        if ((right && nearBottomCorner) || (bottom && nearRightCorner))
        {
            return HitTestBottomRight;
        }

        if (left)
        {
            return HitTestLeft;
        }

        if (right)
        {
            return HitTestRight;
        }

        if (top)
        {
            return HitTestTop;
        }

        return bottom ? HitTestBottom : HitTestClient;
    }

    private static bool IsResizeHitTest(int hitTest) => hitTest is
        HitTestLeft or
        HitTestRight or
        HitTestTop or
        HitTestTopLeft or
        HitTestTopRight or
        HitTestBottom or
        HitTestBottomLeft or
        HitTestBottomRight;

    private static int GetResizeDirection(int hitTest) => hitTest switch
    {
        HitTestLeft => 1,
        HitTestRight => 2,
        HitTestTop => 3,
        HitTestTopLeft => 4,
        HitTestTopRight => 5,
        HitTestBottom => 6,
        HitTestBottomLeft => 7,
        HitTestBottomRight => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(hitTest)),
    };

    private void ApplyResizeConstraints(nint lParam)
    {
        MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        uint dpi = Math.Max(96u, GetDpiForWindow(_windowHandle));
        info.MinimumTrackSize.X = Math.Max(
            1,
            (int)Math.Round(_minimumLogicalWidth * dpi / 96d));
        info.MinimumTrackSize.Y = Math.Max(
            1,
            (int)Math.Round(_minimumLogicalHeight * dpi / 96d));

        nint monitor = MonitorFromWindow(_windowHandle, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = MonitorInfo.Create();
        if (monitor != nint.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            info.MaximumTrackSize.X = Math.Max(
                info.MinimumTrackSize.X,
                monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left);
            info.MaximumTrackSize.Y = Math.Max(
                info.MinimumTrackSize.Y,
                monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top);
        }

        Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
    }

    private void ConfigureUtilityStyles(bool noActivate)
    {
        nint stylePointer = GetWindowLongPtr(_windowHandle, ExtendedStyleIndex);
        long style = stylePointer.ToInt64();
        style |= ExtendedStyleToolWindow;
        style &= ~ExtendedStyleAppWindow;
        if (noActivate)
        {
            style |= ExtendedStyleNoActivate;
        }
        else
        {
            style &= ~ExtendedStyleNoActivate;
        }

        _ = SetWindowLongPtr(_windowHandle, ExtendedStyleIndex, new nint(style));
        int error = Marshal.GetLastWin32Error();
        if (!SetWindowPos(
                _windowHandle,
                nint.Zero,
                0,
                0,
                0,
                0,
                SetWindowPositionNoMove |
                SetWindowPositionNoSize |
                SetWindowPositionNoActivate |
                SetWindowPositionFrameChanged) && error != 0)
        {
            throw new Win32Exception(error, "The overlay utility styles could not be applied.");
        }

        IsNoActivate = noActivate;
    }

    private void RefreshFrame()
    {
        _ = SetWindowPos(
            _windowHandle,
            nint.Zero,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove |
            SetWindowPositionNoSize |
            SetWindowPositionNoActivate |
            SetWindowPositionFrameChanged);
    }

    private void SuppressDwmBorder()
    {
        uint borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));
    }

    private void ApplyRoundedRegion(int width, int height, int cornerRadius)
    {
        int radius = Math.Clamp(cornerRadius, 1, Math.Min(width, height) / 2);
        nint region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The overlay shape could not be created.");
        }

        if (SetWindowRgn(_windowHandle, region, true) == 0)
        {
            _ = DeleteObject(region);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The overlay shape could not be applied.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaximumSize;
        public NativePoint MaximumPosition;
        public NativePoint MinimumTrackSize;
        public NativePoint MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;

        public static MonitorInfo Create() => new() { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
    }

    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nint referenceData);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId,
        nint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint windowHandle, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);
}
