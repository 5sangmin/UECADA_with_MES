// src/Features/Video/EquipmentTypeResolver.cs
//
// equipmentId → equipment 타입 폴더명 매핑.
//
// equipmentId 의 백의 자리(hundreds digit) 가 타입을 결정한다:
//
//   1xx → CAST   (CAST-01 = 101)
//   2xx → CNC    (CNC-01 = 201, CNC-02 = 202, CNC-03 = 203 모두 같은 영상)
//   3xx → WASH   (WASH-01 = 301)
//   4xx → ASSY   (ASSY-01 = 401, ASSY-02 = 402 모두 같은 영상)
//   5xx → TEST   (TEST-01 = 501, TEST-02 = 502 모두 같은 영상)
//
// 라인은 영상 선택에 영향을 주지 않는다 (line 1/2/3 의 CNC-01 은 동일 영상).
// 따라서 영상 파일은 (5 타입) × (5 상태) = 최대 25 개 + 타입별 default.

namespace BeApi.Features.Video;

public static class EquipmentTypeResolver
{
    /// <summary>equipmentId 의 백의 자리로 타입 폴더명을 반환. 매칭 실패 시 null.</summary>
    public static string? GetTypeCode(int equipmentId)
    {
        var hundreds = equipmentId / 100;
        return hundreds switch
        {
            1 => "CAST",
            2 => "CNC",
            3 => "WASH",
            4 => "ASSY",
            5 => "TEST",
            _ => null,
        };
    }

    /// <summary>정상적으로 매핑되는 타입 목록 (UI 헤더 등에서 사용).</summary>
    public static IReadOnlyList<string> AllTypes { get; } = new[] { "CAST", "CNC", "WASH", "ASSY", "TEST" };
}
