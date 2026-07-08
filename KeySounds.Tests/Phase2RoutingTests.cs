using System.Diagnostics;
using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using Xunit;

namespace KeySounds.Tests;

public sealed class Phase2RoutingTests
{
    [Theory]
    [InlineData(0x1E, false, KeySoundGroup.Alpha)]
    [InlineData(0x02, false, KeySoundGroup.NumberRow)]
    [InlineData(0x1C, false, KeySoundGroup.Enter)]
    [InlineData(0x1C, true, KeySoundGroup.Numpad)]
    [InlineData(0x48, true, KeySoundGroup.Arrow)]
    [InlineData(0x48, false, KeySoundGroup.Numpad)]
    public void ResolvesPhysicalKeyGroups(int scan, bool extended, KeySoundGroup expected) =>
        Assert.Equal(expected, new KeyGroupResolver().Resolve(new((ushort)scan, extended)));

    [Fact]
    public void RoutesKeyUpWhenConfigured()
    {
        var profile = Phase2Defaults.CreateProfile(); profile.PlayOnKeyUp = true;
        profile.Groups[KeySoundGroup.Alpha].KeyUpSoundSetId = "click";
        var router = Create(profile);
        Assert.NotNull(router.Route(Event(KeyboardAction.KeyDown)));
        Assert.NotNull(router.Route(Event(KeyboardAction.KeyUp)));
    }

    [Fact]
    public void ThrottlesHeldKeyRepeats()
    {
        var profile = Phase2Defaults.CreateProfile(); profile.RepeatMode = RepeatSoundMode.Throttled; profile.RepeatMinimumIntervalMilliseconds = 80;
        var router = Create(profile); var first = Stopwatch.GetTimestamp();
        Assert.NotNull(router.Route(Event(KeyboardAction.KeyDown, first)));
        Assert.Null(router.Route(Event(KeyboardAction.KeyDown, first + Stopwatch.Frequency / 100)));
        Assert.NotNull(router.Route(Event(KeyboardAction.KeyDown, first + Stopwatch.Frequency / 10)));
    }

    [Fact]
    public void ProfileCompilerClampsUnsafeValues()
    {
        var profile = Phase2Defaults.CreateProfile(); profile.MasterVolume = 4; profile.RepeatMinimumIntervalMilliseconds = 1;
        profile.Groups[KeySoundGroup.Alpha].PitchVariationSemitones = 20; profile.Groups[KeySoundGroup.Alpha].VolumeVariation = 2;
        var compiled = ProfileCompiler.Compile(profile);
        Assert.Equal(1, compiled.MasterGain); Assert.Equal(20, compiled.RepeatMinimumIntervalMilliseconds);
        Assert.Equal(3, compiled.Groups[KeySoundGroup.Alpha].PitchVariation); Assert.Equal(.2f, compiled.Groups[KeySoundGroup.Alpha].VolumeVariation);
    }

    [Fact]
    public void AvoidImmediateRepeatDoesNotSelectSameFileTwice()
    {
        var selector = new SoundVariationSelector(); string[] files = ["a", "b", "c"];
        var previous = selector.Select("set", files, VariationSelectionMode.AvoidImmediateRepeat);
        for (var index = 0; index < 20; index++) { var next = selector.Select("set", files, VariationSelectionMode.AvoidImmediateRepeat); Assert.NotEqual(previous, next); previous = next; }
    }

    private static KeyboardSoundRouter Create(SoundProfile profile) => new(new(ProfileCompiler.Compile(profile)), new(), new(), new BuiltInSoundSetCatalog());
    private static KeyboardInputEvent Event(KeyboardAction action, long? timestamp = null) => new(0x1E, 0, action, 1, false, timestamp ?? Stopwatch.GetTimestamp());
}
