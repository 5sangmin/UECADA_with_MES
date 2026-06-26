package com.example.phm.sensor.service;

import com.example.phm.sensor.opcua.EquipmentSnapshot;
import com.example.phm.sensor.opcua.EquipmentSnapshotStore;
import org.springframework.stereotype.Service;

/**
 * X_DAS ns=3 canonical 스냅샷({@code power}, {@code status_code}) 기반으로 설비 표시상태를 override 한다.
 *
 * <p>기존의 "모든 센서값이 0이면 꺼짐"(isAllSensorsZero) 휴리스틱을 폐기하고, 실데이터 신호로 판정한다.
 * 반환값은 기존 vocabulary({@code RUNNING}/{@code STANDBY}/{@code MAINTENANCE}/{@code ALARM})만 사용한다.
 * 스냅샷이 없거나 stale(5초) 이면 {@code null} 을 반환해 호출부가 base status 로 폴백하게 한다.
 */
@Service
public class RealtimeEquipmentService {

    private static final long HEARTBEAT_STALE_MS = EquipmentSnapshotStore.DEFAULT_STALE_THRESHOLD_MS;

    private final EquipmentSnapshotStore snapshotStore;

    public RealtimeEquipmentService(EquipmentSnapshotStore snapshotStore) {
        this.snapshotStore = snapshotStore;
    }

    /**
     * @param equipId DB equip_id 형식 ({@code LINE-01_CAST-01})
     * @return override 할 상태 문자열, 또는 override 하지 않을 경우 {@code null}
     */
    public String statusOverride(String equipId) {
        EquipmentSnapshot snapshot = snapshotStore.get(equipId);
        if (snapshot == null || snapshotStore.isStale(equipId, HEARTBEAT_STALE_MS)) {
            return null;
        }

        // power=false 는 status_code 보다 우선
        if (Boolean.FALSE.equals(snapshot.getPower())) {
            return "MAINTENANCE";
        }

        Integer statusCode = snapshot.getStatusCode();
        if (statusCode == null) {
            return null;
        }
        return switch (statusCode) {
            case 0 -> "IDLE";
            case 1 -> "RUNNING";
            case 2 -> "COMPLETE";
            case 3 -> "WARNING";
            case 4 -> "ERROR";
            default -> null;
        };
    }
}
