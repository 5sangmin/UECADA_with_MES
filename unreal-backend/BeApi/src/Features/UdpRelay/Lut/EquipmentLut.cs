// src/Features/UdpRelay/Lut/EquipmentLut.cs
using Microsoft.Extensions.Options;

namespace BeApi.Features.UdpRelay.Lut;

/// <summary>
/// EquipmentLutSettings 을 검증·캐싱한 immutable 조회기.
/// 등록은 PR2 의 UdpRelayServiceExtensions 에서 singleton 으로 수행한다.
/// </summary>
public sealed class EquipmentLut
{
    private readonly IReadOnlyDictionary<string, short> _lines;
    private readonly IReadOnlyDictionary<string, short> _equipments;

    public EquipmentLut(IOptions<EquipmentLutSettings> options)
    {
        var s = options.Value;

        if (s.Lines.Count == 0)
            throw new InvalidOperationException(
                "EquipmentLut:Lines 가 비어있습니다. appsettings 에 LUT 를 명시하세요.");
        if (s.Equipments.Count == 0)
            throw new InvalidOperationException(
                "EquipmentLut:Equipments 가 비어있습니다. appsettings 에 LUT 를 명시하세요.");

        _lines = new Dictionary<string, short>(s.Lines, StringComparer.OrdinalIgnoreCase);
        _equipments = new Dictionary<string, short>(s.Equipments, StringComparer.OrdinalIgnoreCase);
    }

    public int LineCount => _lines.Count;
    public int EquipmentCount => _equipments.Count;

    public bool TryGetLineId(string lineKey, out short lineId) =>
        _lines.TryGetValue(lineKey, out lineId);

    public bool TryGetEquipmentId(string equipmentKey, out short equipmentId) =>
        _equipments.TryGetValue(equipmentKey, out equipmentId);

    public short GetLineIdOrThrow(string lineKey) =>
        _lines.TryGetValue(lineKey, out var v)
            ? v
            : throw new KeyNotFoundException($"EquipmentLut: 알 수 없는 line_id '{lineKey}'.");

    public short GetEquipmentIdOrThrow(string equipmentKey) =>
        _equipments.TryGetValue(equipmentKey, out var v)
            ? v
            : throw new KeyNotFoundException($"EquipmentLut: 알 수 없는 equipment_id '{equipmentKey}'.");
}
