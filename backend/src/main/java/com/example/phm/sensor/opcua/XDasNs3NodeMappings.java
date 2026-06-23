package com.example.phm.sensor.opcua;

import java.util.ArrayList;
import java.util.List;

/**
 * ns=3 canonical 스트림의 monitored item 매핑 생성기.
 *
 * <p>Node-ID 형식: {@code ns=3;s=TOTAL_DAS.{LINE_TOKEN}.{EQ_TOKEN}.{field}} (대시 없음, flows.json 기준).
 * 라인 3개 × 설비 9종 × 필드 14개를 펼쳐 mapping 리스트를 만든다.
 */
public final class XDasNs3NodeMappings {

    private static final List<String> LINE_TOKENS = List.of("LINE01", "LINE02", "LINE03");

    private static final List<String> EQUIPMENT_TOKENS = List.of(
            "CAST01", "CNC01", "CNC02", "CNC03", "WASH01", "ASSY01", "ASSY02", "TEST01", "TEST02"
    );

    /**
     * 이번 PR 의 subscribe 대상 필드. equip-sim/X_DAS flows.json 의 ns=3 publish 필드명과 일치.
     * (line_id/equipment_id 포함 — Variable Node 로 별도 존재)
     */
    private static final List<String> FIELDS = List.of(
            "power",
            "status_code",
            "heartbeat",
            "quality_code",
            "cmd_status",
            "progress",
            "cycle_time",
            "part_count",
            "data_1_sensor",
            "data_2_sensor",
            "data_3_sensor",
            "ts_epoch_ms",
            "line_id",
            "equipment_id"
    );

    private XDasNs3NodeMappings() {
    }

    public static List<XDasNs3NodeMapping> defaults() {
        List<XDasNs3NodeMapping> mappings = new ArrayList<>();
        for (String lineToken : LINE_TOKENS) {
            for (String eqToken : EQUIPMENT_TOKENS) {
                String equipId = EquipmentIdConverter.toEquipId(lineToken, eqToken);
                String base = "TOTAL_DAS.%s.%s".formatted(lineToken, eqToken);
                for (String field : FIELDS) {
                    mappings.add(new XDasNs3NodeMapping(
                            "ns=3;s=%s.%s".formatted(base, field),
                            equipId,
                            field
                    ));
                }
            }
        }
        return List.copyOf(mappings);
    }
}
