using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KeySounds.Platform.Windows.Input;

public sealed record KeyboardNativeMetadata(string? DevicePath, ushort? VendorId, ushort? ProductId, ushort UsagePage = 1, ushort Usage = 6, string? Manufacturer = null, string? ProductName = null);
public interface IKeyboardDeviceMetadataProvider
{
    ValueTask<KeyboardNativeMetadata?> QueryAsync(nint handle, CancellationToken token = default);
    ValueTask<IReadOnlyList<nint>> EnumerateAsync(CancellationToken token = default);
}

public sealed class WindowsKeyboardDeviceMetadataProvider : IKeyboardDeviceMetadataProvider
{
    private static readonly Regex Vid = new("VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Pid = new("PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public ValueTask<KeyboardNativeMetadata?> QueryAsync(nint handle, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); uint chars = 0;
        if (NativeMethods.GetRawInputDeviceInfo(handle, NativeMethods.RidiDeviceName, 0, ref chars) == uint.MaxValue || chars is 0 or > 32768) return ValueTask.FromResult<KeyboardNativeMetadata?>(null);
        var pointer = Marshal.AllocHGlobal(checked((int)(chars + 1) * 2));
        try
        {
            var actual = chars; if (NativeMethods.GetRawInputDeviceInfo(handle, NativeMethods.RidiDeviceName, pointer, ref actual) == uint.MaxValue || actual > chars) return ValueTask.FromResult<KeyboardNativeMetadata?>(null);
            var path = Marshal.PtrToStringUni(pointer, checked((int)actual))?.TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(path)) return ValueTask.FromResult<KeyboardNativeMetadata?>(null);
            ushort? vid = Parse(Vid, path), pid = Parse(Pid, path); return ValueTask.FromResult<KeyboardNativeMetadata?>(new(path, vid, pid));
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public ValueTask<IReadOnlyList<nint>> EnumerateAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); var size = (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint count = 0; if (NativeMethods.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
            var list = new NativeMethods.RawInputDeviceList[count]; var actual = count; var result = NativeMethods.GetRawInputDeviceList(list, ref actual, size);
            if (result != uint.MaxValue) return ValueTask.FromResult<IReadOnlyList<nint>>(list.Take((int)Math.Min(actual, (uint)list.Length)).Where(item => item.type == NativeMethods.RimTypeKeyboard).Select(item => item.device).ToArray());
        }
        return ValueTask.FromResult<IReadOnlyList<nint>>([]);
    }
    private static ushort? Parse(Regex regex, string value) => regex.Match(value) is { Success: true } match && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : null;
}
