// Pages/Test/OneShot.cshtml.cs
//
// 테스트용 one-shot URL 모음 페이지.
//   - 각 항목은 /command/request 의 prefill + autoSubmit=1 쿼리를 사용한다.
//   - 클릭 한 번에 명령이 등록되고, 결과는 /command/request 화면에 그대로 표시된다.
//   - "공통 6 타입" + "각 설비 prefix 전용" 을 모두 한 번씩 검증할 수 있도록 12개 시나리오 구성.
//
// idempotency_key 는 매 호출마다 새로 만들고 싶다면 idempotency_key 파라미터를 비워두면 된다.
// 여기서는 동일 시나리오를 한 페이지에서 반복 클릭해도 새 row 가 만들어지도록 idempotency_key 를 넣지 않는다.

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Test;

public class OneShotModel : PageModel
{
    public IReadOnlyList<OneShotScenario> Scenarios { get; } = new OneShotScenario[]
    {
        // === 공통 6 타입 (설비별로 골고루) ===
        new("공통 · power on (CAST-01)",
            "CAST 전원 ON", line: 1, equip: "CAST-01",
            commandType: "power", value: "true"),

        new("공통 · power off (CNC-02)",
            "CNC-02 전원 OFF", line: 2, equip: "CNC-02",
            commandType: "power", value: "false"),

        new("공통 · load_request (WASH-01)",
            "WASH-01 적재 요청", line: 2, equip: "WASH-01",
            commandType: "load_request", value: "true"),

        new("공통 · unload_request (ASSY-01)",
            "ASSY-01 언로드 요청", line: 3, equip: "ASSY-01",
            commandType: "unload_request", value: "true"),

        new("공통 · inject_warning (TEST-01)",
            "TEST-01 경고 주입", line: 3, equip: "TEST-01",
            commandType: "inject_warning", value: "1"),

        new("공통 · reset_error (ASSY-02)",
            "ASSY-02 에러 리셋", line: 3, equip: "ASSY-02",
            commandType: "reset_error", value: "true"),

        // === 설비 전용 (각 prefix 대표 1~2 개) ===
        new("CAST · injection_pressure_sp",
            "CAST-01 사출 압력 SP=120.5",
            line: 1, equip: "CAST-01",
            commandType: "injection_pressure_sp", value: "120.5"),

        new("CAST · cooling_flow_sp",
            "CAST-01 냉각 유량 SP=8.5",
            line: 1, equip: "CAST-01",
            commandType: "cooling_flow_sp", value: "8.5"),

        new("CNC · spindle_speed_sp",
            "CNC-01 스핀들 속도 SP=3500",
            line: 2, equip: "CNC-01",
            commandType: "spindle_speed_sp", value: "3500"),

        new("CNC · tool_usage_sp",
            "CNC-03 공구 사용량 SP=75.0",
            line: 2, equip: "CNC-03",
            commandType: "tool_usage_sp", value: "75.0"),

        new("ASSY · tightening_torque_sp",
            "ASSY-01 체결 토크 SP=12.5",
            line: 3, equip: "ASSY-01",
            commandType: "tightening_torque_sp", value: "12.5"),

        new("TEST · bore_dimension_sp",
            "TEST-02 보어 치수 SP=18.05",
            line: 3, equip: "TEST-02",
            commandType: "bore_dimension_sp", value: "18.05"),
    };
}

public sealed record OneShotScenario(
    string Title,
    string Description,
    int line,
    string equip,
    string commandType,
    string value)
{
    /// <summary>autoSubmit=1 포함된 one-shot URL 생성.</summary>
    public string ToUrl()
    {
        var qs = new[]
        {
            ("line", line.ToString()),
            ("equip", equip),
            ("command_type", commandType),
            ("value", value),
            ("source_type", "unreal-backend"),
            ("created_by", "unreal-backend"),
            ("autoSubmit", "1"),
        };
        var encoded = string.Join("&", qs.Select(p =>
            $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        return $"/command/request?{encoded}";
    }

    /// <summary>autoSubmit 없이 prefill 만 — 폼에서 확인 후 수동 전송용.</summary>
    public string ToPrefillUrl()
    {
        var qs = new[]
        {
            ("line", line.ToString()),
            ("equip", equip),
            ("command_type", commandType),
            ("value", value),
            ("source_type", "unreal-backend"),
            ("created_by", "unreal-backend"),
        };
        var encoded = string.Join("&", qs.Select(p =>
            $"{Uri.EscapeDataString(p.Item1)}={Uri.EscapeDataString(p.Item2)}"));
        return $"/command/request?{encoded}";
    }
}
