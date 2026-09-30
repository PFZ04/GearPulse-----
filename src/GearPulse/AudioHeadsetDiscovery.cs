using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GearPulse;

// Core Audio exposes active playback endpoints, including USB, analog and Bluetooth.
public static class AudioHeadsetDiscovery
{
    public sealed record Endpoint(string Id, string Name, uint FormFactor, Guid ContainerId, string Connection,
        string Icon = "headset", int? Battery = null);

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
    private static readonly Guid SystemPlaceholderContainer = new("00000000-0000-0000-ffff-ffffffffffff");

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

    public static bool IsSpeaker(uint formFactor, string name) =>
        formFactor == 1 || name.Contains("speaker", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("soundbar", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("音箱", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("音響", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("扬声器", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("喇叭", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhysicalContainer(Guid container) =>
        container != Guid.Empty && container != SystemPlaceholderContainer;

    private static IReadOnlyList<AtkDeviceDiscovery.Node> BluetoothNodes(Guid container, string name,
        IReadOnlyList<AtkDeviceDiscovery.Node> nodes)
    {
        var direct = !IsPhysicalContainer(container) ? [] : nodes.Where(node => node.ContainerId == container &&
            node.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (direct.Length > 0) return direct;
        // Some audio endpoints use a different container from their Bluetooth parent.
        var matches = nodes.Where(node => IsPhysicalContainer(node.ContainerId) &&
            node.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) &&
            node.Name.Length >= 5 && !node.Name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) &&
            name.Contains(node.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Select(node => node.ContainerId).Distinct().Take(2).Count() == 1 ? matches : [];
    }

    public static Endpoint? Resolve(string id, string name, uint formFactor, Guid container,
        IReadOnlyList<AtkDeviceDiscovery.Node> nodes)
    {
        if (name.Length == 0) return null;
        var bluetooth = BluetoothNodes(container, name, nodes);
        var related = !IsPhysicalContainer(container) ? [] : nodes.Where(node => node.ContainerId == container).ToArray();
        var connection = ConnectionFor(container, name, related);
        if (bluetooth.Count > 0) connection = "bluetooth";
        var speakerHint = bluetooth.Any(node => IsSpeaker(uint.MaxValue, node.Name));
        var speaker = bluetooth.Count > 0 &&
            (speakerHint || IsSpeaker(formFactor, name) && !IsHeadset(formFactor, name));
        if (!speaker && !IsHeadset(formFactor, name)) return null;
        if (bluetooth.Count > 0)
        {
            if (bluetooth.Any(node => node.Connected == false) &&
                !bluetooth.Any(node => node.Connected == true)) return null;
            var physicalContainer = bluetooth[0].ContainerId;
            var battery = bluetooth.Where(node => node.Connected != false && node.Battery is >= 0 and <= 100)
                .Select(node => node.Battery).FirstOrDefault();
            return new Endpoint(id, name, formFactor, physicalContainer, "bluetooth",
                speaker ? "speaker" : "headset", battery);
        }
        return new Endpoint(id, name, formFactor, container, connection);
    }

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
                            var container = ReadGuid(store, ContainerId);
                            device.GetId(out var id);
                            var endpoint = Resolve(id, name, formFactor, container, nodes);
                            if (endpoint is not null) endpoints.Add(endpoint);
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

    public static IReadOnlyList<DeviceState> ToStates(IEnumerable<Endpoint> source)
    {
        var endpoints = source.Select(endpoint =>
        {
            var key = endpoint.Icon == "speaker" && endpoint.ContainerId != Guid.Empty
                ? endpoint.ContainerId.ToString("N") : endpoint.Id;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            var id = $"audio-{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
            return new DeviceState(id, endpoint.Name, endpoint.Icon, endpoint.Battery, null, true,
                endpoint.Battery.HasValue ? "ok" : "unavailable", endpoint.Connection, endpoint.ContainerId);
        }).ToArray();
        var speakers = endpoints.Where(state => state.Icon == "speaker").GroupBy(state => state.Id)
            .Select(group => group.OrderByDescending(state => state.Battery.HasValue)
                .ThenByDescending(state => IsSpeaker(uint.MaxValue, state.Name)).First()).ToArray();
        var speakerContainers = speakers.Select(state => state.ContainerId)
            .Where(container => container != Guid.Empty).ToHashSet();
        return endpoints.Where(state => state.Icon != "speaker" &&
            !speakerContainers.Contains(state.ContainerId)).Concat(speakers).ToArray();
    }

    public static IReadOnlyList<DeviceState> Sample()
    {
        try
        {
            return ToStates(Enumerate());
        }
        catch (Exception error)
        {
            AppLog.Write("Audio headset discovery failed", error);
            return [];
        }
    }
}
