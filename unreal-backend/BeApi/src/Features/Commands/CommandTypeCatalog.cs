// src/Features/Commands/CommandTypeCatalog.cs
//
// 설비 prefix 별 valid command_type 카탈로그.
//
// 공통 (모든 prefix 적용):
//   power, unload_request, load_request, inject_warning, inject_error, reset_error
//
// 전용 (_sp = setpoint):
//   CAST: injection_pressure_sp, mold_temperature_sp, cooling_flow_sp
//   CNC : spindle_speed_sp, tool_usage_sp, coolant_flow_sp
//   WASH: cleaning_concentration_sp, cleaning_temperature_sp, cleaning_pressure_sp
//   ASSY: tightening_torque_sp, tightening_angle_sp, press_force_sp
//   TEST: bore_dimension_sp, hole_dimension_sp

namespace BeApi.Features.Commands;

public static class CommandTypeCatalog
{
    /// <summary>모든 설비 공통 command_type.</summary>
    public static readonly IReadOnlySet<string> Common = new HashSet<string>(StringComparer.Ordinal)
    {
        "power",
        "unload_request",
        "load_request",
        "inject_warning",
        "inject_error",
        "reset_error",
    };

    /// <summary>설비 prefix → 전용 command_type 집합.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByPrefix =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["CAST"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "injection_pressure_sp",
                "mold_temperature_sp",
                "cooling_flow_sp",
            },
            ["CNC"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "spindle_speed_sp",
                "tool_usage_sp",
                "coolant_flow_sp",
            },
            ["WASH"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "cleaning_concentration_sp",
                "cleaning_temperature_sp",
                "cleaning_pressure_sp",
            },
            ["ASSY"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "tightening_torque_sp",
                "tightening_angle_sp",
                "press_force_sp",
            },
            ["TEST"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "bore_dimension_sp",
                "hole_dimension_sp",
            },
        };

    /// <summary>prefix 에 대해 command_type 이 허용되는지 검사.</summary>
    public static bool IsValid(string prefix, string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType)) return false;
        if (Common.Contains(commandType)) return true;
        return ByPrefix.TryGetValue(prefix, out var set) && set.Contains(commandType);
    }

    /// <summary>prefix 가 받을 수 있는 전체 valid command_type 목록 (디버깅/에러 메시지용).</summary>
    public static IReadOnlyList<string> AllowedFor(string prefix)
    {
        var common = Common.AsEnumerable();
        var specific = ByPrefix.TryGetValue(prefix, out var set)
            ? set.AsEnumerable()
            : Enumerable.Empty<string>();
        return common.Concat(specific).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
