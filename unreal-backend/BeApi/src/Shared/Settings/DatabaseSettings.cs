// src/Shared/Settings/DatabaseSettings.cs
namespace BeApi.Shared.Settings;

public class DatabaseSettings
{
    public const string SectionName = "Database";
    public string TsdbConnectionString { get; set; } = string.Empty;
    public string CommandDbConnectionString { get; set; } = string.Empty;
}