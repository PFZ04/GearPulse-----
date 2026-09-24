using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GearPulse;

// Core Audio exposes active playback endpoints, including USB, analog and Bluetooth.
public static class AudioHeadsetDiscovery
{
    public sealed record Endpoint(string Id, string Name, uint FormFactor, Guid ContainerId, string Connection);

    [StructLayout(LayoutKind.Sequential)] private readonly struct PropertyKey(Guid format, uint id)
    {
        public readonly Guid Format = format;
        public readonly uint Id = id;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropertyValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public uint Number;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        void RegisterEndpointNotificationCallback(IntPtr callback);
        void UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr result);
        void OpenPropertyStore(uint access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyValue value);
        void SetValue(ref PropertyKey key, ref PropertyValue value);
        void Commit();
    }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropertyValue value);

    private static readonly PropertyKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PropertyKey FormFactor = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);
    private static readonly PropertyKey ContainerId = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    private static PropertyValue Get(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out var value);
        return value;
    }
    private static string ReadString(IPropertyStore store, PropertyKey key)
    {
        var value = Get(store, key);
        try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
        finally { PropVariantClear(ref value); }
    }
    private static uint ReadNumber(IPropertyStore store, PropertyKey key)
    {
        var value = Get(store, key);
        try { return value.Type == 19 ? value.Number : uint.MaxValue; }
        finally { PropVariantClear(ref value); }
    }
    private static Guid ReadGuid(IPropertyStore store, PropertyKey key)
    {
        var value = Get(store, key);
        try { return value.Type == 72 && value.Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Pointer) : Guid.Empty; }
        finally { PropVariantClear(ref value); }
    }

    public static bool IsHeadset(uint formFactor, string name) =>
        formFactor is 3 or 5 || name.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("headset", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("earphone", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("耳机", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("耳機", StringComparison.OrdinalIgnoreCase);

    public static string ConnectionFor(Guid container, string name, IEnumerable<AtkDeviceDiscovery.Node> nodes)
    {
        var related = container == Guid.Empty ? [] : nodes.Where(x => x.ContainerId == container).ToArray();
        if (related.Any(x => x.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase))) return "bluetooth";
        if (name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) return "bluetooth";
        if (related.Any(x => x.Vendor == 0x046d && x.Product == 0x0b19)) return "wired";
        if (related.Any(x => (x.Vendor == 0x046d && x.Product == 0x0b18) ||
            (x.Vendor == 0x1532 && x.Product == 0x0555))) return "wireless";
        if (related.Any(x => x.Name.Contains("receiver", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("dongle", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("LIGHTSPEED", StringComparison.OrdinalIgnoreCase))) return "wireless";
        if (name.Contains("wireless", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LIGHTSPEED", StringComparison.OrdinalIgnoreCase)) return "wireless";
        return "wired";
    }

    public static IReadOnlyList<Endpoint> Enumerate()
    {
        var enumerator = (IDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        try
        {
            enumerator.EnumAudioEndpoints(0, 1, out var collection); // eRender, DEVICE_STATE_ACTIVE
            try
            {
                collection.GetCount(out var count);
                var endpoints = new List<Endpoint>();
                IReadOnlyList<AtkDeviceDiscovery.Node> nodes;
                try { nodes = AtkDeviceDiscovery.EnumerateNodes(false); }
                catch (Exception error) { AppLog.Write("Audio transport discovery failed", error); nodes = []; }
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);
                    try
                    {
                        device.OpenPropertyStore(0, out var store);
                        try
                        {
                            var name = ReadString(store, FriendlyName).Trim();
                            var formFactor = ReadNumber(store, FormFactor);
                            if (!IsHeadset(formFactor, name) || name.Length == 0) continue;
                            var container = ReadGuid(store, ContainerId);
                            device.GetId(out var id);
                            endpoints.Add(new Endpoint(id, name, formFactor, container,
                                ConnectionFor(container, name, nodes)));
                        }
                        finally { Marshal.ReleaseComObject(store); }
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return endpoints;
            }
            finally { Marshal.ReleaseComObject(collection); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    public static IReadOnlyList<DeviceState> Sample()
    {
        try
        {
            return Enumerate().Select(endpoint =>
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.Id));
                var id = $"audio-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
                return new DeviceState(id, endpoint.Name, "headset", null, null, true,
                    "unavailable", endpoint.Connection, endpoint.ContainerId);
            }).ToArray();
        }
        catch (Exception error)
        {
            AppLog.Write("Audio headset discovery failed", error);
            return [];
        }
    }
}
