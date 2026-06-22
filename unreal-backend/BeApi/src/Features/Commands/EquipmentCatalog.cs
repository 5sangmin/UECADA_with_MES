// src/Features/Commands/EquipmentCatalog.cs
//
// 운영 화면 드롭다운에 표시할 설비 코드 목록.
// EquipmentCodeResolver 규칙(CAST/CNC/WASH/ASSY/TEST) 과 일치.
// 새 설비 늘면 이 배열 + DB seed 만 수정.

namespace BeApi.Features.Commands;

public static class EquipmentCatalog
{
    /// <summary>
    /// 운영 화면 드롭다운용 (equipment_code, equipment_id) 목록.
    /// line_id 는 equipment_code 로 유도할 수 없으므로 운영자가 직접 입력한다.
    /// </summary>
    public static readonly IReadOnlyList<EquipmentCatalogItem> Items = new[]
    {
        new EquipmentCatalogItem("CAST-01", 101),
        new EquipmentCatalogItem("CNC-01",  201),
        new EquipmentCatalogItem("CNC-02",  202),
        new EquipmentCatalogItem("CNC-03",  203),
        new EquipmentCatalogItem("WASH-01", 301),
        new EquipmentCatalogItem("ASSY-01", 401),
        new EquipmentCatalogItem("ASSY-02", 402),
        new EquipmentCatalogItem("TEST-01", 501),
        new EquipmentCatalogItem("TEST-02", 502),
    };

    /// <summary>설비 prefix 추출 (예: "CAST-01" → "CAST").</summary>
    public static string? TryGetPrefix(string code)
    {
        var idx = code.IndexOf('-');
        return idx > 0 ? code[..idx] : null;
    }
}

public sealed record EquipmentCatalogItem(string EquipmentCode, int EquipmentId);
