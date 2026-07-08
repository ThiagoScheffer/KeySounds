using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;

namespace KeySounds.Application;

public static class Phase2Defaults
{
    public static SoundProfile CreateProfile()
    {
        var profile = new SoundProfile { Id = "default", Name = "Default Click" };
        foreach (var group in Enum.GetValues<KeySoundGroup>())
            profile.Groups[group] = new KeyGroupMapping { SoundPackId = "builtin", KeyDownSoundSetId = "click" };
        return profile;
    }

    public static KeyboardSoundRouter CreateRouter(ActiveProfileService? active = null) =>
        new(active ?? new ActiveProfileService(ProfileCompiler.Compile(CreateProfile())), new KeyGroupResolver(), new SoundVariationSelector(), new BuiltInSoundSetCatalog());
}
