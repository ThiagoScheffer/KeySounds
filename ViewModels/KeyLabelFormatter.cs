using KeySounds.Core.Input;

namespace KeySounds.ViewModels;

public static class KeyLabelFormatter
{
    private static readonly IReadOnlyDictionary<ushort, string> Alpha = new Dictionary<ushort, string>
    { [0x10]="Q",[0x11]="W",[0x12]="E",[0x13]="R",[0x14]="T",[0x15]="Y",[0x16]="U",[0x17]="I",[0x18]="O",[0x19]="P",[0x1E]="A",[0x1F]="S",[0x20]="D",[0x21]="F",[0x22]="G",[0x23]="H",[0x24]="J",[0x25]="K",[0x26]="L",[0x2C]="Z",[0x2D]="X",[0x2E]="C",[0x2F]="V",[0x30]="B",[0x31]="N",[0x32]="M" };
    public static string Format(KeyIdentifier key)
    {
        if (Alpha.TryGetValue(key.ScanCode, out var alpha)) return alpha;
        if (key.ScanCode is >= 0x3B and <= 0x44) return $"F{key.ScanCode - 0x3A}";
        return (key.ScanCode, key.IsExtended) switch
        {
            (0x2A, _) => "Left Shift", (0x36, _) => "Right Shift", (0x1D, false) => "Left Ctrl", (0x1D, true) => "Right Ctrl",
            (0x38, false) => "Left Alt", (0x38, true) => "Right Alt", (0x1C, false) => "Enter", (0x1C, true) => "Numpad Enter",
            (0x39, _) => "Space", (0x4B, true) => "Arrow Left", (0x4D, true) => "Arrow Right", (0x48, true) => "Arrow Up", (0x50, true) => "Arrow Down",
            (0x01, _) => "Escape", (0x0F, _) => "Tab", (0x0E, _) => "Backspace", _ => $"ScanCode 0x{key.ScanCode:X2}{(key.IsExtended ? " (Extended)" : string.Empty)}"
        };
    }
}
