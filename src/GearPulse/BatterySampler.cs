namespace GearPulse;

public sealed class BatterySampler(IReadOnlyList<IBatteryProvider> providers)
{
    private int sampling;
    public bool IsSampling => Volatile.Read(ref sampling) != 0;

    public async Task<IReadOnlyList<DeviceState>?> SampleAsync()
    {
        if (Interlocked.Exchange(ref sampling, 1) != 0) return null;
        try
        {
            return await Task.Run(() => providers.SelectMany(provider =>
            {
                try { return provider.Read(); }
                catch (Exception error)
                {
                    AppLog.Write($"Provider {provider.Id} failed", error);
                    return [new DeviceState(provider.Id, provider.Id, "battery", null, null, true, "error")];
                }
            }).ToArray());
        }
        finally { Interlocked.Exchange(ref sampling, 0); }
    }
}
