// src/Features/UdpRelay/Replay/ReplayTimeParser.cs
//
// Replay API 시간 입력 파싱 정책 (PR2 보강):
//   - tz offset 이 명시되어 있으면 그대로 사용 (예: "2026-06-22T07:09:20Z", "2026-06-22T16:09:20+09:00")
//   - tz offset 이 없으면 KST(+09:00) 로 간주 (예: "2026-06-22T16:09:20", "2026-06-22 16:09:20")
//
// 이 규칙은 한국 운영팀 친화성을 위한 명시적 결정. ISO 8601 의 "naive = local server tz" 기본을 의도적으로 깸.

using System.Globalization;

namespace BeApi.Features.UdpRelay.Replay;

public static class ReplayTimeParser
{
    /// <summary>한국 표준시 offset (UTC+09:00).</summary>
    public static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    /// <summary>
    /// 입력 문자열을 DateTimeOffset 으로 파싱.
    /// </summary>
    /// <returns>파싱 성공 시 true. 실패 시 reason 에 사유.</returns>
    public static bool TryParse(string? input, out DateTimeOffset result, out string? reason)
    {
        result = default;
        reason = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            reason = "비어있는 시간 문자열입니다.";
            return false;
        }

        var s = input.Trim();

        // 1) tz offset 또는 'Z' 가 있으면 DateTimeOffset.Parse 가 그대로 처리
        //    - 'T' 뒤 또는 ' ' 뒤에 '+', '-', 'Z' 가 보이면 offset 있음
        if (HasExplicitOffset(s))
        {
            if (DateTimeOffset.TryParse(
                    s,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
            {
                return true;
            }
            reason = $"시간 문자열 '{input}' 을 파싱할 수 없습니다 (offset 포함).";
            return false;
        }

        // 2) tz 누락 → KST 로 간주
        //    DateTime.TryParse + Kind=Unspecified → KST offset 부여
        if (DateTime.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.NoCurrentDateDefault,
                out var dt))
        {
            // Kind 와 무관하게, KST 로 간주하여 DateTimeOffset 생성
            var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
            result = new DateTimeOffset(unspecified, KstOffset);
            return true;
        }

        reason = $"시간 문자열 '{input}' 을 파싱할 수 없습니다.";
        return false;
    }

    private static bool HasExplicitOffset(string s)
    {
        // 'Z' 로 끝나면 UTC
        if (s.EndsWith('Z') || s.EndsWith('z')) return true;

        // 시간 파트의 시작 위치 ('T' 또는 공백 뒤)
        int timeStart = s.LastIndexOfAny(new[] { 'T', 't', ' ' });
        if (timeStart < 0) return false; // 날짜만 보낼 경우 offset 없음

        // 시간 파트의 첫 콜론 이후 위치에서 '+' 또는 '-' 가 나오면 offset
        // (첫 콜론은 HH:MM 구분자. 그 이후의 '+'/'-' 는 반드시 tz offset)
        int firstColon = s.IndexOf(':', timeStart);
        if (firstColon < 0) return false;

        for (int i = firstColon + 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '+' || c == '-') return true;
        }
        return false;
    }
}
