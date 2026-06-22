// src/Features/UdpRelay/Lut/EquipmentLutSettings.cs
//
// TSDB 텍스트 키 ↔ wire 정수 ID 매핑.
//
//   TSDB:    line_id='LINE-01', equipment_id='LINE-01_CNC-02'
//   Wire:    line_id=1,         equipment_id=202
//
// 매핑 규칙(고정 LUT):
//   line_id : LINE-01=1, LINE-02=2, LINE-03=3
//   equipment_id 의 백자릿수:
//     CAST=1xx, CNC=2xx, WASH=3xx, ASSY=4xx, TEST=5xx
//     일의/십의 자리는 라인 내 인덱스(y)
//     즉 'LINE-01_CNC-02' → 200 + 02 = 202
//
// 본 클래스는 appsettings 의 EquipmentLut 섹션으로부터 dict 형태로 주입받는다.
// 실수로 누락된 키가 있을 경우 startup 시 fail-fast 한다.

namespace BeApi.Features.UdpRelay.Lut;

public class EquipmentLutSettings
{
    public const string SectionName = "EquipmentLut";

    /// <summary>"LINE-01" → 1.</summary>
    public Dictionary<string, short> Lines { get; set; } = new();

    /// <summary>"LINE-01_CNC-02" → 202.</summary>
    public Dictionary<string, short> Equipments { get; set; } = new();
}
