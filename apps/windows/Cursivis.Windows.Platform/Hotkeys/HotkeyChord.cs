namespace Cursivis.Windows.Platform.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

public enum HotkeyChordValidationCode
{
    Valid,
    MissingModifier,
    InvalidVirtualKey,
    ModifierOnly,
    ReservedCombination,
}

public readonly record struct HotkeyChord
{
    private const HotkeyModifiers AllowedModifiers =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.Windows;

    public HotkeyChord(HotkeyModifiers modifiers, uint virtualKey)
    {
        var validation = Validate(modifiers, virtualKey);
        if (validation != HotkeyChordValidationCode.Valid)
        {
            throw new ArgumentException(
                $"The hotkey chord is invalid: {validation}.",
                nameof(virtualKey));
        }

        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public HotkeyModifiers Modifiers { get; }

    public uint VirtualKey { get; }

    public static HotkeyChordValidationCode Validate(
        HotkeyModifiers modifiers,
        uint virtualKey)
    {
        if (modifiers == HotkeyModifiers.None || (modifiers & ~AllowedModifiers) != 0)
        {
            return HotkeyChordValidationCode.MissingModifier;
        }

        if (virtualKey is 0 or > 0xFF)
        {
            return HotkeyChordValidationCode.InvalidVirtualKey;
        }

        if (virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        {
            return HotkeyChordValidationCode.ModifierOnly;
        }

        const uint virtualKeyDelete = 0x2E;
        const uint virtualKeyF4 = 0x73;
        const uint virtualKeyL = 0x4C;

        if ((modifiers.HasFlag(HotkeyModifiers.Control) &&
             modifiers.HasFlag(HotkeyModifiers.Alt) &&
             virtualKey == virtualKeyDelete) ||
            (modifiers == HotkeyModifiers.Alt && virtualKey == virtualKeyF4) ||
            (modifiers.HasFlag(HotkeyModifiers.Windows) && virtualKey == virtualKeyL))
        {
            return HotkeyChordValidationCode.ReservedCombination;
        }

        return HotkeyChordValidationCode.Valid;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(VirtualKey));
        return string.Join('+', parts);
    }

    private static string FormatVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 || virtualKey is >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x6F}";
        }

        return virtualKey switch
        {
            0x1B => "Escape",
            0x20 => "Space",
            0x09 => "Tab",
            _ => $"VK_{virtualKey:X2}",
        };
    }
}
