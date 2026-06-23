package com.example.phm.sensor.opcua;

import java.time.Instant;

/**
 * X_DAS ns=3 canonical 스트림의 설비별 최신 스냅샷 (in-memory).
 *
 * <p>OPC UA monitored item 콜백이 필드 단위로 비동기 갱신하므로 가변(mutable) 객체로 두고,
 * 각 필드를 {@code volatile} 로 선언해 가시성을 보장한다. key 는 DB equip_id 형식
 * ({@code LINE-01_CAST-01}).
 */
public class EquipmentSnapshot {

    private final String equipId;

    private volatile Boolean power;
    private volatile Integer statusCode;
    private volatile Integer heartbeat;
    private volatile Integer qualityCode;
    private volatile Integer cmdStatus;
    private volatile Double progress;
    private volatile Double cycleTime;
    private volatile Integer partCount;
    private volatile Double data1Sensor;
    private volatile Double data2Sensor;
    private volatile Double data3Sensor;
    private volatile Integer lineId;
    private volatile Integer equipmentId;
    private volatile Instant lastUpdatedAt;
    private volatile Instant lastHeartbeatAt;

    public EquipmentSnapshot(String equipId) {
        this.equipId = equipId;
    }

    /**
     * ns=3 필드 1개를 갱신한다. 모든 갱신은 {@code lastUpdatedAt} 를 touch 하며,
     * {@code heartbeat} 갱신 시 {@code lastHeartbeatAt} 도 함께 touch 한다.
     *
     * @param field ns=3 field 이름 (예: {@code power}, {@code status_code})
     * @param value OPC UA 원시 값 (Boolean / Number)
     * @param now   갱신 시각
     */
    public void applyField(String field, Object value, Instant now) {
        switch (field) {
            case "power" -> this.power = toBool(value);
            case "status_code" -> this.statusCode = toInt(value);
            case "heartbeat" -> {
                this.heartbeat = toInt(value);
                this.lastHeartbeatAt = now;
            }
            case "quality_code" -> this.qualityCode = toInt(value);
            case "cmd_status" -> this.cmdStatus = toInt(value);
            case "progress" -> this.progress = toDouble(value);
            case "cycle_time" -> this.cycleTime = toDouble(value);
            case "part_count" -> this.partCount = toInt(value);
            case "data_1_sensor" -> this.data1Sensor = toDouble(value);
            case "data_2_sensor" -> this.data2Sensor = toDouble(value);
            case "data_3_sensor" -> this.data3Sensor = toDouble(value);
            case "line_id" -> this.lineId = toInt(value);
            case "equipment_id" -> this.equipmentId = toInt(value);
            default -> {
                // ts_epoch_ms 등 별도 보관하지 않는 필드: lastUpdatedAt 만 갱신
            }
        }
        this.lastUpdatedAt = now;
    }

    private static Boolean toBool(Object value) {
        if (value instanceof Boolean b) {
            return b;
        }
        if (value instanceof Number n) {
            return n.doubleValue() != 0.0;
        }
        return null;
    }

    private static Integer toInt(Object value) {
        if (value instanceof Number n) {
            return n.intValue();
        }
        if (value instanceof Boolean b) {
            return b ? 1 : 0;
        }
        return null;
    }

    private static Double toDouble(Object value) {
        if (value instanceof Number n) {
            return n.doubleValue();
        }
        if (value instanceof Boolean b) {
            return b ? 1.0 : 0.0;
        }
        return null;
    }

    public String getEquipId() {
        return equipId;
    }

    public Boolean getPower() {
        return power;
    }

    public Integer getStatusCode() {
        return statusCode;
    }

    public Integer getHeartbeat() {
        return heartbeat;
    }

    public Integer getQualityCode() {
        return qualityCode;
    }

    public Integer getCmdStatus() {
        return cmdStatus;
    }

    public Double getProgress() {
        return progress;
    }

    public Double getCycleTime() {
        return cycleTime;
    }

    public Integer getPartCount() {
        return partCount;
    }

    public Double getData1Sensor() {
        return data1Sensor;
    }

    public Double getData2Sensor() {
        return data2Sensor;
    }

    public Double getData3Sensor() {
        return data3Sensor;
    }

    public Integer getLineId() {
        return lineId;
    }

    public Integer getEquipmentId() {
        return equipmentId;
    }

    public Instant getLastUpdatedAt() {
        return lastUpdatedAt;
    }

    public Instant getLastHeartbeatAt() {
        return lastHeartbeatAt;
    }
}
