// src/Features/Commands/EquipmentCatalog.cs
//
// 운영 화면 드롭다운에 표시할 설비 코드 목록.
// EquipmentCodeResolver 규칙(CAST/CNC/WASH/ASSY/TEST) 과 일치.
// 새 설비 늘면 이 배열 + DB seed 만 수정.

namespace BeApi.Features.Commands;

public static class EquipmentCatalog
{
    /// <summary>운영 화면 드롭다운용 (line_id, equipment_code) 목록.</summary>
    public static readonly IReadOnlyList<EquipmentCatalogItem> Items = new[]
    {
        new EquipmentCatalogItem(1, "CAST-01", 101),
        new EquipmentCatalogItem(2, "CNC-01",  201),
        new EquipmentCatalogItem(2, "CNC-02",  202),
        new EquipmentCatalogItem(2, "CNC-03",  203),
        new EquipmentCatalogItem(2, "WASH-01", 301),
        new EquipmentCatalogItem(3, "ASSY-01", 401),
        new EquipmentCatalogItem(3, "ASSY-02", 402),
        new EquipmentCatalogItem(3, "TEST-01", 501),
        new EquipmentCatalogItem(3, "TEST-02", 502),
    };

    /// <summary>설비 prefix 추출 (예: "CAST-01" → "CAST").</summary>
    public static string? TryGetPrefix(string code)
    {
        var idx = code.IndexOf('-');
        return idx > 0 ? code[..idx] : null;
    }
}

public sealed record EquipmentCatalogItem(int LineId, string EquipmentCode, int EquipmentId);
