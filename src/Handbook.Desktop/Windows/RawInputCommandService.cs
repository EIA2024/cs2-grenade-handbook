using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Handbook.Core;

namespace Handbook.Desktop;

// Copies only key/button transitions out of WM_INPUT. No mouse motion or device identity is retained.
internal static class RawPacketDecoder
{
    public static readonly int HeaderSize = 8 + 2 * IntPtr.Size;
    public static void Decode(ReadOnlySpan<byte> packet, Action<InputDevice, int, bool> emit)
    {
        if (packet.Length < HeaderSize) return;
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(packet);
        var data = packet[HeaderSize..];
        if (type == 1 && data.Length >= 16)
        {
            int scan = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            int key = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            if (key is 0 or 255) return;
            // Keep left/right modifier release states independent.
            key = key switch { 16 => scan == 0x36 ? 161 : 160, 17 => (flags & 2) != 0 ? 163 : 162, 18 => (flags & 2) != 0 ? 165 : 164, _ => key };
            emit(InputDevice.Keyboard, key, (flags & 1) == 0);
        }
        else if (type == 0 && data.Length >= 24)
        {
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if ((flags & 0x3ff) == 0) return;
            // lLastX/lLastY, wheel data, hDevice and timestamps are intentionally never read.
            Button(1, 2, 1); Button(4, 8, 2); Button(16, 32, 4); Button(64, 128, 5); Button(256, 512, 6);
            void Button(int press, int release, int code)
            { if ((flags & press) != 0) emit(InputDevice.Mouse, code, true); if ((flags & release) != 0) emit(InputDevice.Mouse, code, false); }
        }
    }
}

internal sealed class RawInputCommandService : IInputCommandService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceRegistration { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] DeviceRegistration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
    private readonly nint handle;
    private readonly HwndSource source;
    private readonly InputCommandMatcher matcher;
    // One bounded buffer owned by this service, reused synchronously on its window thread.
    private readonly nint buffer = Marshal.AllocHGlobal(1024);
    private readonly byte[] managedBuffer = new byte[1024];
    private bool disposed;
    private bool registered;
    public bool Active { get; private set; }
    public event Action<InputCommand>? Command;
    public event Action<string>? Failed;
    public RawInputCommandService(Window owner, IReadOnlyDictionary<InputCommand, InputBinding> bindings)
    {
        matcher = new(bindings); handle = new WindowInteropHelper(owner).EnsureHandle(); source = HwndSource.FromHwnd(handle)!;
        source.AddHook(HandleMessage); // Only this application's window messages, not a system hook.
    }
    private DeviceRegistration[] Devices(uint flags) =>
    [new() { UsagePage = 1, Usage = 6, Flags = flags, Target = flags == 1 ? 0 : handle },
     new() { UsagePage = 1, Usage = 2, Flags = flags, Target = flags == 1 ? 0 : handle }];
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Active) return;
        matcher.Reset();
        // Keep cleanup pending until Windows confirms removal, including a failed
        // multi-device registration attempt. Never lose the ability to retry Stop.
        registered = true;
        if (!RegisterRawInputDevices(Devices(0x100), 2, (uint)Marshal.SizeOf<DeviceRegistration>()))
        {
            int error = Marshal.GetLastPInvokeError(); string cleanupError = "";
            try { Stop(); } catch (Win32Exception e) { cleanupError = " " + e.Message; }
            throw new Win32Exception(error, "后台输入注册失败；可从托盘返回编辑器或鼠标调整。" + cleanupError);
        }
        registered = true; Active = true;
    }
    public void Stop()
    {
        Active = false; matcher.Reset();
        if (registered && !RegisterRawInputDevices(Devices(1), 2, (uint)Marshal.SizeOf<DeviceRegistration>()))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "后台输入注销失败；命令处理已停用，请退出软件。");
        registered = false;
    }
    public void SetBindings(IReadOnlyDictionary<InputCommand, InputBinding> bindings) => matcher.SetBindings(bindings);
    private nint HandleMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message != 0xFF || !Active) return 0;
        try
        {
            uint size = 1024;
            uint count = GetRawInputData(lparam, 0x10000003, buffer, ref size, (uint)RawPacketDecoder.HeaderSize);
            if (count == uint.MaxValue) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (count > 1024 || count < RawPacketDecoder.HeaderSize) throw new InvalidDataException("后台输入消息尺寸无效。");
            Marshal.Copy(buffer, managedBuffer, 0, (int)count);
            RawPacketDecoder.Decode(managedBuffer.AsSpan(0, (int)count), (device, code, pressed) =>
            { if (matcher.Feed(device, code, pressed) is { } command) Command?.Invoke(command); });
            Array.Clear(managedBuffer, 0, (int)count);
        }
        catch (Exception e)
        {
            string cleanupError = "";
            try { Stop(); } catch (Win32Exception cleanup) { cleanupError = "\n" + cleanup.Message; }
            Array.Clear(managedBuffer); Failed?.Invoke("后台快捷浏览已停用：" + e.Message + cleanupError);
        }
        // Do not mark WM_INPUT handled: WPF/DefWindowProc must perform system cleanup.
        return 0;
    }
    public void Dispose()
    {
        if (disposed) return;
        try { Stop(); }
        finally
        {
            try { source.RemoveHook(HandleMessage); }
            finally { Marshal.FreeHGlobal(buffer); Array.Clear(managedBuffer); disposed = true; }
        }
    }
}

