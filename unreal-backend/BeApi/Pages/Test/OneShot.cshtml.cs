// Pages/Test/OneShot.cshtml.cs
//
// 테스트용 one-shot URL 모음 페이지.
//   - 각 항목은 /command/request 의 prefill + autoSubmit=1 쿼리를 사용한다.
//   - 클릭 한 번에 명령이 등록되고, 결과는 /command/request 화면에 그대로 표시된다.
//   - 공통 6 타입 + 각 설비 prefix 전용 타입을 한 번씩 검증.
//   - line 은 1/2/3 을 골고루 사용 (모든 설비는 모든 라인에 존재).
//
// 보안/운영 관점:
//   - 페이지 자체와 상단 nav 링크는 Development 환경에서만 노출된다 (Program.cs 에서 endpoint 가드).
//   - idempotency_key 는 비워두므로 반복 클릭 시 매번 새 row 가 만들어진다.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeApi.Pages.Test;

public class OneShotModel : PageModel
{
    private readonly IWebHostEnvironment _env;

    public OneShotModel(IWebHostEnvironment env)
    {
        _env = env;
    }

    public IActionResult OnGet()
    {
        // Development 일 때만 접근 허용. 그 외 환경은 404.
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }
        return Page();
    }

    public IReadOnlyList<OneShotScenario> Scenarios { get; } = new OneShotScenario[]
    {
        // === 공통 6 타입 (line 1/2/3 골고루) ===
        new("공통 · power on (line 1 / CAST-01)",
            "line 1 CAST 전원 ON", line: 1, equip: "CAST-01",
            commandType: "power", value: "true"),

        new("공통 · power off (line 2 / CNC-02)",
            "line 2 CNC-02 전원 OFF", line: 2, equip: "CNC-02",
            commandType: "power", value: "false"),

        new("공통 · load_request (line 3 / WASH-01)",
            "line 3 WASH-01 적재 요청", line: 3, equip: "WASH-01",
            commandType: "load_request", value: "true"),

        new("공통 · unload_request (line 1 / ASSY-01)",
            "line 1 ASSY-01 언로드 요청", line: 1, equip: "ASSY-01",
            commandType: "unload_request", value: "true"),

        new("공통 · inject_warning (line 2 / TEST-01)",
            "line 2 TEST-01 경고 주입", line: 2, equip: "TEST-01",
            commandType: "inject_warning", value: "1"),

        new("공통 · reset_error (line 3 / ASSY-02)",
            "line 3 ASSY-02 에러 리셋", line: 3, equip: "ASSY-02",
            commandType: "reset_error", value: "true"),

        // === 설비 전용 (각 prefix 대표 1~2 개, line 골고루) ===
        new("CAST · injection_pressure_sp",
            "line 2 CAST-01 사출 압력 SP=120.5",
            line: 2, equip: "CAST-01",
            commandType: "injection_pressure_sp", value: "120.5"),

        new("CAST · cooling_flow_sp",
            "line 3 CAST-01 냉각 유량 SP=8.5",
            line: 3, equip: "CAST-01",
            commandType: "cooling_flow_sp", value: "8.5"),

        new("CNC · spindle_speed_sp",
            "line 1 CNC-01 스핀들 속도 SP=3500",
            line: 1, equip: "CNC-01",
            commandType: "spindle_speed_sp", value: "3500"),

        new("CNC · tool_usage_sp",
            "line 3 CNC-03 공구 사용량 SP=75.0",
            line: 3, equip: "CNC-03",
            commandType: "tool_usage_sp", value: "75.0"),

        new("ASSY · tightening_torque_sp",
            "line 2 ASSY-01 체결 토크 SP=12.5",
            line: 2, equip: "ASSY-01",
            commandType: "tightening_torque_sp", value: "12.5"),

        new("TEST · bore_dimension_sp",
            "line 1 TEST-02 보어 치수 SP=18.05",
            line: 1, equip: "TEST-02",
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
