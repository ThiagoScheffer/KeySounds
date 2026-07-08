using System.Runtime.InteropServices;
using KeySounds.Core.Input;
using KeySounds.Platform.Windows.Input;
using Xunit;

namespace KeySounds.Tests;

public sealed class RawInputParserTests
{
    [Fact] public void RejectsTruncatedHeader() => Assert.False(RawInputParser.TryParse(new byte[Marshal.SizeOf<NativeMethods.RawInputHeader>() - 1], out _));

    [Fact]
    public void RejectsTruncatedKeyboardPayload()
    {
        var bytes = new byte[Marshal.SizeOf<NativeMethods.RawInputHeader>() + Marshal.SizeOf<NativeMethods.RawKeyboard>() - 1];
        Write(bytes, new NativeMethods.RawInputHeader { type = NativeMethods.RimTypeKeyboard });
        Assert.False(RawInputParser.TryParse(bytes, out _));
    }

    [Fact]
    public void IgnoresNonKeyboardInput()
    {
        var bytes = new byte[Marshal.SizeOf<NativeMethods.RawInputHeader>()]; Write(bytes, new NativeMethods.RawInputHeader { type = 0 });
        Assert.False(RawInputParser.TryParse(bytes, out _));
    }

    [Fact]
    public void ParsesValidKeyboardInput()
    {
        var headerSize = Marshal.SizeOf<NativeMethods.RawInputHeader>();
        var bytes = new byte[headerSize + Marshal.SizeOf<NativeMethods.RawKeyboard>()];
        Write(bytes, new NativeMethods.RawInputHeader { type = NativeMethods.RimTypeKeyboard, device = 42 });
        Write(bytes.AsSpan(headerSize), new NativeMethods.RawKeyboard { makeCode = 30, virtualKey = 65, flags = NativeMethods.RiKeyE0 });
        Assert.True(RawInputParser.TryParse(bytes, out var value));
        Assert.Equal((ushort)30, value.ScanCode); Assert.Equal((nint)42, value.DeviceHandle); Assert.Equal(KeyboardAction.KeyDown, value.Action); Assert.True(value.IsExtended);
    }

    private static void Write<T>(Span<byte> destination, T value) where T : struct => MemoryMarshal.Write(destination, in value);
}
