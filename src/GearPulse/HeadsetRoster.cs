namespace GearPulse;

public static class HeadsetRoster
{
    private static string ModelKey(string name)
    {
        if (name.Contains("G522", StringComparison.OrdinalIgnoreCase)) return "g522";
        if (name.Contains("BLACKSHARK V2 PRO", StringComparison.OrdinalIgnoreCase)) return "blackshark-v2-pro";
        return "";
    }

    public static IReadOnlyList<DeviceState> Merge(IEnumerable<DeviceState> states)
    {
        var all = states.ToArray();
        var headsets = all.Where(x => x.Icon == "headset").ToArray();
        string Key(DeviceState x)
        {
            var model = ModelKey(x.Name);
            if (model.Length > 0)
            {
                var known = headsets.Where(y => !y.Id.StartsWith("audio-", StringComparison.Ordinal) &&
                    ModelKey(y.Name) == model).Take(2).ToArray();
                if (known.Length == 1 && (x.ContainerId == Guid.Empty || known[0].ContainerId == Guid.Empty ||
                    x.ContainerId == known[0].ContainerId))
                    return known[0].ContainerId != Guid.Empty
                        ? "container-" + known[0].ContainerId.ToString("N") : "model-" + model;
            }
            return x.ContainerId != Guid.Empty ? "container-" + x.ContainerId.ToString("N") : "id-" + x.Id;
        }
        var preferred = headsets.GroupBy(Key).ToDictionary(group => group.Key, group =>
        {
            // A verified reader wins over the Windows audio placeholder.
            return group.OrderByDescending(x => x.Id.StartsWith("audio-", StringComparison.Ordinal) ? 0 : 1)
                .ThenByDescending(x => x.Battery.HasValue).First();
        });
        var result = new List<DeviceState>();
        var seen = new HashSet<string>();
        foreach (var state in all)
        {
            if (state.Icon != "headset") result.Add(state);
            else if (seen.Add(Key(state))) result.Add(preferred[Key(state)]);
        }
        return result;
    }
}
