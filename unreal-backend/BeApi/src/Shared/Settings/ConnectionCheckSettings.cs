// src/Shared/Settings/ConnectionCheckSettings.cs
namespace BeApi.Shared.Settings;

public class ConnectionCheckSettings
{
    public const string SectionName = "ConnectionCheck";
    public int IntervalSeconds { get; set; } = 30;
    public int TimeoutMs { get; set; } = 5000;
}