\
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SumUpSoloExplorer;

internal sealed class WinUsbDevice : IDisposable
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Guid GuidDevInterfaceUsbDevice =
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private SafeFileHandle? _file;
    private IntPtr _winUsbHandle;

    public string DevicePath { get; }
    public byte InterfaceNumber { get; }
    public IReadOnlyList<UsbPipeInfo> Pipes { get; }
    public byte? BulkOutPipe { get; }
    public byte? BulkInPipe { get; }

    private WinUsbDevice(
        string devicePath,
        SafeFileHandle file,
        IntPtr winUsbHandle,
        byte interfaceNumber,
        List<UsbPipeInfo> pipes)
    {
        DevicePath = devicePath;
        _file = file;
        _winUsbHandle = winUsbHandle;
        InterfaceNumber = interfaceNumber;
        Pipes = pipes;
        BulkOutPipe = pipes.FirstOrDefault(p => p.PipeType == UsbdPipeType.Bulk && (p.PipeId & 0x80) == 0)?.PipeId;
        BulkInPipe = pipes.FirstOrDefault(p => p.PipeType == UsbdPipeType.Bulk && (p.PipeId & 0x80) != 0)?.PipeId;
    }

    public static WinUsbDevice OpenSolo()
    {
        string? path = EnumerateUsbPaths().FirstOrDefault(p =>
            p.Contains("vid_345b&pid_0002&mi_01", StringComparison.OrdinalIgnoreCase));

        if (path is null)
        {
            throw new InvalidOperationException(
                "Solo MI_01 nebylo nalezeno. Ověř USB režim terminálu a WinUSB ovladač.");
        }

        SafeFileHandle file = CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (file.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile pro Solo selhalo.");

        if (!WinUsb_Initialize(file, out IntPtr winUsbHandle))
        {
            int error = Marshal.GetLastWin32Error();
            file.Dispose();
            throw new Win32Exception(error, "WinUsb_Initialize selhalo.");
        }

        try
        {
            if (!WinUsb_QueryInterfaceSettings(winUsbHandle, 0, out UsbInterfaceDescriptor descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nelze načíst USB interface descriptor.");

            var pipes = new List<UsbPipeInfo>();
            for (byte i = 0; i < descriptor.bNumEndpoints; i++)
            {
                if (!WinUsb_QueryPipe(winUsbHandle, 0, i, out WinUsbPipeInformation info))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Nelze načíst endpoint {i}.");

                pipes.Add(new UsbPipeInfo(info.PipeType, info.PipeId, info.MaximumPacketSize, info.Interval));
            }

            return new WinUsbDevice(path, file, winUsbHandle, descriptor.bInterfaceNumber, pipes);
        }
        catch
        {
            WinUsb_Free(winUsbHandle);
            file.Dispose();
            throw;
        }
    }

    public int Write(byte[] data)
    {
        byte pipe = BulkOutPipe ?? throw new InvalidOperationException("Bulk OUT endpoint nebyl nalezen.");
        if (!WinUsb_WritePipe(_winUsbHandle, pipe, data, (uint)data.Length, out uint written, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_WritePipe selhalo.");
        return checked((int)written);
    }

    public byte[]? Read(uint timeoutMs)
    {
        byte pipe = BulkInPipe ?? throw new InvalidOperationException("Bulk IN endpoint nebyl nalezen.");
        uint timeout = timeoutMs;
        WinUsb_SetPipePolicy(
            _winUsbHandle,
            pipe,
            PipeTransferTimeout,
            sizeof(uint),
            ref timeout);

        byte[] buffer = new byte[4096];
        if (!WinUsb_ReadPipe(_winUsbHandle, pipe, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            // ERROR_SEM_TIMEOUT
            if (error == 121)
                return null;
            throw new Win32Exception(error, "WinUsb_ReadPipe selhalo.");
        }

        if (read == 0)
            return null;

        Array.Resize(ref buffer, checked((int)read));
        return buffer;
    }

    public void Dispose()
    {
        if (_winUsbHandle != IntPtr.Zero)
        {
            WinUsb_Free(_winUsbHandle);
            _winUsbHandle = IntPtr.Zero;
        }

        _file?.Dispose();
        _file = null;
    }

    private static IEnumerable<string> EnumerateUsbPaths()
    {
        Guid guid = GuidDevInterfaceUsbDevice;
        IntPtr set = SetupDiGetClassDevs(
            ref guid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs selhalo.");

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        set,
                        IntPtr.Zero,
                        ref guid,
                        index,
                        ref interfaceData))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                        yield break;
                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces selhalo.");
                }

                SetupDiGetDeviceInterfaceDetail(
                    set,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out uint required,
                    IntPtr.Zero);

                int sizeError = Marshal.GetLastWin32Error();
                if (sizeError != ErrorInsufficientBuffer)
                    throw new Win32Exception(sizeError, "Nelze zjistit velikost device path.");

                IntPtr detail = Marshal.AllocHGlobal(checked((int)required));
                try
                {
                    // cbSize differs between x86 and x64.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                    if (!SetupDiGetDeviceInterfaceDetail(
                            set,
                            ref interfaceData,
                            detail,
                            required,
                            out _,
                            IntPtr.Zero))
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(),
                            "SetupDiGetDeviceInterfaceDetail selhalo.");
                    }

                    int pathOffset = IntPtr.Size == 8 ? 8 : 4;
                    string? path = Marshal.PtrToStringUni(detail + pathOffset);
                    if (!string.IsNullOrWhiteSpace(path))
                        yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private const uint PipeTransferTimeout = 3;

    internal sealed record UsbPipeInfo(
        UsbdPipeType PipeType,
        byte PipeId,
        ushort MaximumPacketSize,
        byte Interval);

    internal enum UsbdPipeType
    {
        Control = 0,
        Isochronous = 1,
        Bulk = 2,
        Interrupt = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsbInterfaceDescriptor
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public UsbdPipeType PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Initialize(
        SafeFileHandle deviceHandle,
        out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);
}
