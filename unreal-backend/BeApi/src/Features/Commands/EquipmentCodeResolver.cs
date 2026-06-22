// src/Features/Commands/EquipmentCodeResolver.cs
//
// 설비 코드 ("CAST-01") → (prefix, equipment_id) 매핑.
//
// 규칙 (사용자 정의):
//   CAST-yy → 100 + yy
//   CNC-yy  → 200 + yy
//   WASH-yy → 300 + yy
//   ASSY-yy → 400 + yy
//   TEST-yy → 500 + yy
//
// line_id 는 코드에서 추정 불가 → request JSON 에 line_id 필수.

using System.Text.RegularExpressions;

namespace BeApi.Features.Commands;

public static class EquipmentCodeResolver
{
    // ^(CAST|CNC|WASH|ASSY|TEST)-(\d+)$ — 대소문자 구분 (사양상 대문자 prefix)
    private static readonly Regex CodePattern =
        new(@"^(?<prefix>CAST|CNC|WASH|ASSY|TEST)-(?<y>\d+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, int> PrefixBase =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CAST"] = 100,
            ["CNC"] = 200,
            ["WASH"] = 300,
            ["ASSY"] = 400,
            ["TEST"] = 500,
        };

    public sealed record Resolved(string Prefix, int Yy, int EquipmentId);

    /// <summary>설비 코드 → (prefix, yy, equipmentId).</summary>
    public static bool TryResolve(string? code, out Resolved? resolved, out string? error)
    {
        resolved = null;
        error = null;

        if (string.IsNullOrWhiteSpace(code))
        {
            error = "equipment_id 가 비어있습니다.";
            return false;
        }

        var m = CodePattern.Match(code);
        if (!m.Success)
        {
            error = $"equipment_id 형식이 잘못되었습니다: '{code}'. 예: 'CAST-01', 'CNC-02'";
            return false;
        }

        var prefix = m.Groups["prefix"].Value;
        if (!int.TryParse(m.Groups["y"].Value, out var yy))
        {
            error = $"equipment_id 의 숫자 부분 파싱 실패: '{code}'";
            return false;
        }

        if (yy <= 0 || yy >= 100)
        {
            error = $"equipment_id 의 숫자 부분이 범위 밖입니다 (1~99): '{code}'";
            return false;
        }

        if (!PrefixBase.TryGetValue(prefix, out var baseId))
        {
            // 정규식이 가드하지만 안전망.
            error = $"알 수 없는 prefix: '{prefix}'";
            return false;
        }

        resolved = new Resolved(prefix, yy, baseId + yy);
        return true;
    }
}
