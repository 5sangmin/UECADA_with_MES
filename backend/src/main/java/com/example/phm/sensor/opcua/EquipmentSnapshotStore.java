package com.example.phm.sensor.opcua;

import java.time.Instant;
import java.util.concurrent.ConcurrentHashMap;

import org.springframework.stereotype.Component;

/**
 * 설비별 ns=3 최신 스냅샷을 보관하는 in-memory 저장소.
 *
 * <p>key 는 DB equip_id 형식({@code LINE-01_CAST-01})으로 통일한다. heartbeat(=가장 최근 갱신)
 * 기준 staleness 를 판정해, 통신이 끊긴 설비는 override 대상에서 제외할 수 있게 한다.
 */
@Component
public class EquipmentSnapshotStore {

    /** heartbeat staleness 기본 임계값 (ms). ns=3 publishing interval(1초)보다 충분히 큼. */
    public static final long DEFAULT_STALE_THRESHOLD_MS = 5000L;

    private final ConcurrentHashMap<String, EquipmentSnapshot> snapshots = new ConcurrentHashMap<>();

    /**
     * ns=3 필드 1개를 갱신하고, 갱신된 스냅샷을 반환한다.
     *
     * @param equipId DB equip_id 형식 ({@code LINE-01_CAST-01})
     * @param field   ns=3 field 이름
     * @param value   OPC UA 원시 값
     */
    public EquipmentSnapshot update(String equipId, String field, Object value) {
        EquipmentSnapshot snapshot = snapshots.computeIfAbsent(equipId, EquipmentSnapshot::new);
        snapshot.applyField(field, value, Instant.now());
        return snapshot;
    }

    public EquipmentSnapshot get(String equipId) {
        return snapshots.get(equipId);
    }

    public boolean isStale(String equipId) {
        return isStale(equipId, DEFAULT_STALE_THRESHOLD_MS);
    }

    /** 마지막 갱신 후 {@code thresholdMs} 가 지났거나 스냅샷이 없으면 stale. */
    public boolean isStale(String equipId, long thresholdMs) {
        EquipmentSnapshot snapshot = snapshots.get(equipId);
        if (snapshot == null || snapshot.getLastUpdatedAt() == null) {
            return true;
        }
        long ageMs = Instant.now().toEpochMilli() - snapshot.getLastUpdatedAt().toEpochMilli();
        return ageMs > thresholdMs;
    }
}
