using System.Runtime.InteropServices;

namespace KeySounds.Platform.Windows.Input;

internal static partial class NativeMethods
{
    internal const uint WmInput = 0x00FF, WmInputDeviceChange = 0x00FE, WmClose = 0x0010, WmDestroy = 0x0002, WmQuit = 0x0012;
    internal const uint RidInput = 0x10000003, RidevRemove = 0x00000001, RidevInputSink = 0x00000100, RidevDeviceNotify = 0x00002000;
    internal const uint RidiDeviceName = 0x20000007, RidiDeviceInfo = 0x2000000B;
    internal const ushort RimTypeKeyboard = 1, RiKeyBreak = 1, RiKeyE0 = 2, RiKeyE1 = 4;
    internal const int ErrorClassAlreadyExists = 1410;

    [StructLayout(LayoutKind.Sequential)] internal struct WndClass { public uint style; public nint wndProc; public int clsExtra, wndExtra; public nint instance, icon, cursor, background, menuName, className; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public Point point; public uint privateValue; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputDevice { public ushort usagePage, usage; public uint flags; public nint target; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputHeader { public uint type, size; public nint device; public nuint wParam; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawKeyboard { public ushort makeCode, flags, reserved, virtualKey; public uint message, extraInformation; }
    [StructLayout(LayoutKind.Sequential)] internal struct RawInputDeviceList { public nint device; public uint type; }
    [StructLayout(LayoutKind.Sequential)] internal struct RidDeviceInfoKeyboard { public uint type, subType, keyboardMode, numberOfFunctionKeys, numberOfIndicators, numberOfKeysTotal; }
    [StructLayout(LayoutKind.Explicit)] internal struct RidDeviceInfoUnion { [FieldOffset(0)] public RidDeviceInfoKeyboard keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct RidDeviceInfo { public uint size, type; public RidDeviceInfoUnion data; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassW", SetLastError = true)] internal static partial ushort RegisterClass(ref WndClass value);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] internal static partial nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] internal static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] internal static partial uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial uint GetRawInputDeviceList([Out] RawInputDeviceList[]? list, ref uint count, uint size);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] internal static partial int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static partial nint DispatchMessage(ref Message message);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool TranslateMessage(ref Message message);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int exitCode);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint GetModuleHandle(string? moduleName);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnregisterClass(string className, nint instance);
}
