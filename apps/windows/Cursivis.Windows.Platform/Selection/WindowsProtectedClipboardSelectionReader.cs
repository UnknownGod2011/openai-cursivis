using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Cursivis.Domain.Context;
using Windows.ApplicationModel.DataTransfer;

namespace Cursivis.Windows.Platform.Selection;

public sealed class WindowsProtectedClipboardSelectionReader : ITextSelectionReader
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyC = 0x43;
    private static readonly TimeSpan ClipboardTimeout = TimeSpan.FromMilliseconds(750);

    public async Task<TextSelectionReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        uint baselineSequence = GetClipboardSequenceNumber();
        IDataObject? original = CaptureOriginalClipboard();
        uint copiedSequence = 0;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnClipboardChanged(object? sender, object args)
        {
            if (GetClipboardSequenceNumber() != baselineSequence)
            {
                changed.TrySetResult();
            }
        }

        Clipboard.ContentChanged += OnClipboardChanged;
        try
        {
            if (!SendCopyChord())
            {
                return Failed(TextSelectionReadStatus.Unavailable, "Windows could not send the copy command.");
            }

            try
            {
                await changed.Task.WaitAsync(ClipboardTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return Failed(TextSelectionReadStatus.NoSelection, "The foreground application did not publish a clipboard selection.");
            }
            catch (OperationCanceledException)
            {
                return Failed(TextSelectionReadStatus.Cancelled, "Clipboard selection detection was cancelled.");
            }

            copiedSequence = GetClipboardSequenceNumber();
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                return Failed(TextSelectionReadStatus.NoSelection, "The copied selection did not contain text.");
            }

            string text = await content.GetTextAsync().AsTask(cancellationToken);
            return string.IsNullOrWhiteSpace(text)
                ? Failed(TextSelectionReadStatus.NoSelection, "The copied selection was empty.")
                : TextSelectionReadResult.Captured(text, ContextSource.ProtectedClipboard);
        }
        catch (COMException)
        {
            return Failed(TextSelectionReadStatus.Unavailable, "The Windows clipboard was unavailable.");
        }
        finally
        {
            Clipboard.ContentChanged -= OnClipboardChanged;
            RestoreOriginalClipboard(original, copiedSequence);
        }
    }

    internal static IDataObject? CaptureOriginalClipboard()
    {
        int result = OleGetClipboard(out IDataObject? original);
        return result >= 0 ? original : null;
    }

    internal static void RestoreOriginalClipboard(IDataObject? original, uint copiedSequence)
    {
        try
        {
            if (copiedSequence != 0 && GetClipboardSequenceNumber() == copiedSequence)
            {
                _ = OleSetClipboard(original);
            }
        }
        finally
        {
            if (original is not null && Marshal.IsComObject(original))
            {
                _ = Marshal.FinalReleaseComObject(original);
            }
        }
    }

    private static bool SendCopyChord()
    {
        Input[] inputs =
        [
            Input.KeyDown(VirtualKeyControl),
            Input.KeyDown(VirtualKeyC),
            Input.KeyUp(VirtualKeyC),
            Input.KeyUp(VirtualKeyControl),
        ];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static TextSelectionReadResult Failed(TextSelectionReadStatus status, string detail) =>
        TextSelectionReadResult.Failed(status, ContextSource.ProtectedClipboard, detail);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Protected clipboard selection requires Windows.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;

        public static Input KeyDown(ushort virtualKey) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey } },
        };

        public static Input KeyUp(ushort virtualKey) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = KeyEventKeyUp },
            },
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard([MarshalAs(UnmanagedType.Interface)] out IDataObject? dataObject);

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard([MarshalAs(UnmanagedType.Interface)] IDataObject? dataObject);
}
