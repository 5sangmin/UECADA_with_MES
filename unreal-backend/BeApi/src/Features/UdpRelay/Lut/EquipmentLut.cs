// src/Features/UdpRelay/Lut/EquipmentLut.cs
using Microsoft.Extensions.Options;

namespace BeApi.Features.UdpRelay.Lut;

/// <summary>
/// TSDB 의 text 키를 wire 정수 ID 로 변환하는 조회기.
///
/// 현재 스키마 설계 상, TSDB 의 line_id/equipment_id 는 '1','101' 의 정수 문자열 형태.
/// 따라서 기본 전략은 short.TryParse 로 항등 변환.
///
/// LUT(appsettings.EquipmentLut.{Lines,Equipments}) 가 비어있으면:
///   → 항등 사상 허용 (모든 정수 문자열 삽입).
///
/// LUT 가 어떤 값이라도 들어있으면:
///   → whitelist 로 동작. 등록된 키만 통과, value 가 있으면 매핑 우선 사용.
///
/// 이 설계는 (a) 운영에서 LUT 를 일일이 관리하지 않아도 동작하며,
/// (b) 원한다면 whitelist 로 잘못된 장비를 걸러낼 수 있도록 한다.
/// </summary>
public sealed class EquipmentLut
{
    private readonly IReadOnlyDictionary<string, short>? _lines;
    private readonly IReadOnlyDictionary<string, short>? _equipments;

    public EquipmentLut(IOptions<EquipmentLutSettings> options)
    {
        var s = options.Value;
        _lines = s.Lines.Count == 0
            ? null
            : new Dictionary<string, short>(s.Lines, StringComparer.OrdinalIgnoreCase);
        _equipments = s.Equipments.Count == 0
            ? null
            : new Dictionary<string, short>(s.Equipments, StringComparer.OrdinalIgnoreCase);
    }

    public int LineCount => _lines?.Count ?? 0;
    public int EquipmentCount => _equipments?.Count ?? 0;
    public bool HasLineWhitelist => _lines != null;
    public bool HasEquipmentWhitelist => _equipments != null;

    /// <summary>line_id text → wire short. LUT 매핑이 있으면 그 값, 없으면 정수 파싱.</summary>
    public bool TryGetLineId(string lineKey, out short lineId)
    {
        if (_lines != null)
        {
            return _lines.TryGetValue(lineKey, out lineId);
        }
        return short.TryParse(lineKey, out lineId);
    }

    /// <summary>equipment_id text → wire short. LUT 매핑이 있으면 그 값, 없으면 정수 파싱.</summary>
    public bool TryGetEquipmentId(string equipmentKey, out short equipmentId)
    {
        if (_equipments != null)
        {
            return _equipments.TryGetValue(equipmentKey, out equipmentId);
        }
        return short.TryParse(equipmentKey, out equipmentId);
    }
}
