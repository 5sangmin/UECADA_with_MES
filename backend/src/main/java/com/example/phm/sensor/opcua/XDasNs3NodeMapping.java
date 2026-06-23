package com.example.phm.sensor.opcua;

/**
 * ns=3 canonical 스트림의 monitored item 1개 매핑.
 *
 * @param nodeId  OPC UA node-id ({@code ns=3;s=TOTAL_DAS.LINE01.CAST01.power})
 * @param equipId DB equip_id 형식으로 미리 변환한 설비 식별자 ({@code LINE-01_CAST-01})
 * @param field   ns=3 field 이름 ({@code power}, {@code status_code} 등)
 */
public record XDasNs3NodeMapping(String nodeId, String equipId, String field) {
}
