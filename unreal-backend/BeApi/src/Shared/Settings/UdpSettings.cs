// src/Shared/Settings/UdpSettings.cs
namespace BeApi.Shared.Settings;

public class UdpSettings
{
    public const string SectionName = "Udp";
    public string MulticastGroup { get; set; } = "239.100.0.1";
    public int ListenPort { get; set; } = 50020;
    public string BindAddress { get; set; } = "0.0.0.0";
    public int StaleThresholdSeconds { get; set; } = 10;
}