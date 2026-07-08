using KeySounds.Core.Input;

namespace KeySounds.Application.Routing;

public sealed class KeyGroupResolver
{
    public KeySoundGroup Resolve(KeyIdentifier key)
    {
        var scan = key.ScanCode;
        if (scan is >= 0x10 and <= 0x19 or >= 0x1E and <= 0x26 or >= 0x2C and <= 0x32) return KeySoundGroup.Alpha;
        if (scan is >= 0x02 and <= 0x0B) return KeySoundGroup.NumberRow;
        if (scan is 0x2A or 0x36 or 0x1D or 0x38) return KeySoundGroup.Modifier;
        if (scan == 0x39) return KeySoundGroup.Spacebar;
        if (scan == 0x1C) return key.IsExtended ? KeySoundGroup.Numpad : KeySoundGroup.Enter;
        if (scan == 0x0E) return KeySoundGroup.Backspace;
        if (scan == 0x0F) return KeySoundGroup.Tab;
        if (scan == 0x01) return KeySoundGroup.Escape;
        if (scan is >= 0x3B and <= 0x44 or 0x57 or 0x58) return KeySoundGroup.Function;
        if (key.IsExtended && scan is 0x48 or 0x4B or 0x4D or 0x50) return KeySoundGroup.Arrow;
        if (key.IsExtended && scan is 0x47 or 0x49 or 0x4F or 0x51 or 0x52 or 0x53) return KeySoundGroup.Navigation;
        if (!key.IsExtended && scan is >= 0x47 and <= 0x53 or 0x37 or 0x4A or 0x4E) return KeySoundGroup.Numpad;
        return KeySoundGroup.Other;
    }
}
