using System.Diagnostics;
using System.Runtime.InteropServices;
using KeySounds.Core.Input;

namespace KeySounds.Platform.Windows.Input;

internal static class RawInputParser
{
    internal static bool TryParse(ReadOnlySpan<byte> bytes, out KeyboardInputEvent value)
    {
        value = default;
        var headerSize = Marshal.SizeOf<NativeMethods.RawInputHeader>();
        if (bytes.Length < headerSize) return false;
        var header = MemoryMarshal.Read<NativeMethods.RawInputHeader>(bytes);
        if (header.type != NativeMethods.RimTypeKeyboard) return false;
        var keyboardSize = Marshal.SizeOf<NativeMethods.RawKeyboard>();
        if (bytes.Length < checked(headerSize + keyboardSize)) return false;
        var keyboard = MemoryMarshal.Read<NativeMethods.RawKeyboard>(bytes[headerSize..]);
        var action = (keyboard.flags & NativeMethods.RiKeyBreak) != 0 ? KeyboardAction.KeyUp : KeyboardAction.KeyDown;
        var extended = (keyboard.flags & (NativeMethods.RiKeyE0 | NativeMethods.RiKeyE1)) != 0;
        value = new(keyboard.makeCode, keyboard.virtualKey, action, header.device, extended, Stopwatch.GetTimestamp());
        return true;
    }
}
