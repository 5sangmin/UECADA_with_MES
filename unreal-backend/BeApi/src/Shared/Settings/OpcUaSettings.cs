// src/Shared/Settings/OpcUaSettings.cs
namespace BeApi.Shared.Settings;

public class OpcUaSettings
{
    public const string SectionName = "OpcUa";  // JSON 키와 1:1 매핑
    public string EndpointUrl { get; set; } = string.Empty;
    public int ReconnectDelayMs { get; set; } = 5000;
    public int SessionTimeoutMs { get; set; } = 30000;
}