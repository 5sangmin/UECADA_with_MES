package com.example.phm.sensor.opcua;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import org.junit.jupiter.api.Test;

class EquipmentIdConverterTest {

    @Test
    void convertsNs3TokensToDbEquipId() {
        assertThat(EquipmentIdConverter.toEquipId("LINE01", "CAST01")).isEqualTo("LINE-01_CAST-01");
        assertThat(EquipmentIdConverter.toEquipId("LINE03", "CNC02")).isEqualTo("LINE-03_CNC-02");
        assertThat(EquipmentIdConverter.toEquipId("line02", "test01")).isEqualTo("LINE-02_TEST-01");
    }

    @Test
    void convertsNs3NodeBaseToDbEquipId() {
        assertThat(EquipmentIdConverter.nodeBaseToEquipId("TOTAL_DAS.LINE01.CAST01"))
                .isEqualTo("LINE-01_CAST-01");
        // field 가 붙어 있어도 line/eq 토큰만 사용
        assertThat(EquipmentIdConverter.nodeBaseToEquipId("TOTAL_DAS.LINE02.WASH01.power"))
                .isEqualTo("LINE-02_WASH-01");
    }

    @Test
    void convertsDbEquipIdBackToNs3Tokens() {
        assertThat(EquipmentIdConverter.toNs3Tokens("LINE-01_CAST-01"))
                .containsExactly("LINE01", "CAST01");
        assertThat(EquipmentIdConverter.toNs3Tokens("LINE-03_CNC-02"))
                .containsExactly("LINE03", "CNC02");
    }

    @Test
    void roundTripsAllLineEquipmentCombinations() {
        for (String line : new String[] {"LINE01", "LINE02", "LINE03"}) {
            for (String eq : new String[] {"CAST01", "CNC01", "CNC02", "CNC03",
                    "WASH01", "ASSY01", "ASSY02", "TEST01", "TEST02"}) {
                String equipId = EquipmentIdConverter.toEquipId(line, eq);
                assertThat(EquipmentIdConverter.toNs3Tokens(equipId)).containsExactly(line, eq);
            }
        }
    }

    @Test
    void rejectsMalformedToken() {
        assertThatThrownBy(() -> EquipmentIdConverter.toEquipId("LINE", "CAST01"))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    void returnsNullForMalformedBaseOrEquipId() {
        assertThat(EquipmentIdConverter.nodeBaseToEquipId("LINE01")).isNull();
        assertThat(EquipmentIdConverter.nodeBaseToEquipId(null)).isNull();
        assertThat(EquipmentIdConverter.toNs3Tokens("LINE-01")).isNull();
        assertThat(EquipmentIdConverter.toNs3Tokens(null)).isNull();
    }
}
