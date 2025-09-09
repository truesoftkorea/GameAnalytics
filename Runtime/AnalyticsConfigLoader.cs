// Runtime/AnalyticsConfigLoader.cs
using UnityEngine;

public static class AnalyticsConfigLoader
{
    private const string DefaultPath = "TrueSoft/AnalyticsSdkConfig"; // Resources/TrueSoft/AnalyticsSdkConfig.asset

    private static AnalyticsSdkConfig _cached;

    public static AnalyticsSdkConfig Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<AnalyticsSdkConfig>(DefaultPath);
        return _cached;
    }

    public static bool TryLoad(out AnalyticsSdkConfig config)
    {
        config = Load();
        return config != null;
    }
}