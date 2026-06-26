package com.example.phm.sensor.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.time.Instant;

import com.example.phm.sensor.opcua.EquipmentSnapshot;
import com.example.phm.sensor.opcua.EquipmentSnapshotStore;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class RealtimeEquipmentServiceTest {

    private EquipmentSnapshotStore store;
    private RealtimeEquipmentService service;

    @BeforeEach
    void setUp() {
        store = mock(EquipmentSnapshotStore.class);
        service = new RealtimeEquipmentService(store);
    }

    @Test
    void statusOverride_whenNoSnapshot_returnsNull() {
        when(store.get("EQ-1")).thenReturn(null);

        assertThat(service.statusOverride("EQ-1")).isNull();
    }

    @Test
    void statusOverride_whenStale_returnsNull() {
        EquipmentSnapshot snapshot = snapshotWithStatus("EQ-1", true, 1);
        when(store.get("EQ-1")).thenReturn(snapshot);
        when(store.isStale("EQ-1", EquipmentSnapshotStore.DEFAULT_STALE_THRESHOLD_MS)).thenReturn(true);

        assertThat(service.statusOverride("EQ-1")).isNull();
    }

    @Test
    void statusOverride_code0_returnsIDLE() {
        assertStatus("EQ-1", true, 0, "IDLE");
    }

    @Test
    void statusOverride_code1_returnsRUNNING() {
        assertStatus("EQ-1", true, 1, "RUNNING");
    }

    @Test
    void statusOverride_code2_returnsCOMPLETE() {
        assertStatus("EQ-1", true, 2, "COMPLETE");
    }

    @Test
    void statusOverride_code3_returnsWARNING() {
        assertStatus("EQ-1", true, 3, "WARNING");
    }

    @Test
    void statusOverride_code4_returnsERROR() {
        assertStatus("EQ-1", true, 4, "ERROR");
    }

    @Test
    void statusOverride_unknownCode_returnsNull() {
        assertStatus("EQ-1", true, 99, null);
    }

    @Test
    void statusOverride_powerFalse_returnsMaintenance_ignoringStatusCode() {
        // power=false 이면 status_code 가 RUNNING(1)이어도 MAINTENANCE 반환
        EquipmentSnapshot snapshot = snapshotWithStatus("EQ-1", false, 1);
        when(store.get("EQ-1")).thenReturn(snapshot);
        when(store.isStale("EQ-1", EquipmentSnapshotStore.DEFAULT_STALE_THRESHOLD_MS)).thenReturn(false);

        assertThat(service.statusOverride("EQ-1")).isEqualTo("MAINTENANCE");
    }

    @Test
    void statusOverride_powerFalse_returnsMaintenance_whenStatusCode4() {
        // power=false 면 ERROR(4)가 와도 MAINTENANCE 우선
        EquipmentSnapshot snapshot = snapshotWithStatus("EQ-1", false, 4);
        when(store.get("EQ-1")).thenReturn(snapshot);
        when(store.isStale("EQ-1", EquipmentSnapshotStore.DEFAULT_STALE_THRESHOLD_MS)).thenReturn(false);

        assertThat(service.statusOverride("EQ-1")).isEqualTo("MAINTENANCE");
    }

    private void assertStatus(String equipId, boolean power, int statusCode, String expected) {
        EquipmentSnapshot snapshot = snapshotWithStatus(equipId, power, statusCode);
        when(store.get(equipId)).thenReturn(snapshot);
        when(store.isStale(equipId, EquipmentSnapshotStore.DEFAULT_STALE_THRESHOLD_MS)).thenReturn(false);

        assertThat(service.statusOverride(equipId)).isEqualTo(expected);
    }

    private EquipmentSnapshot snapshotWithStatus(String equipId, boolean power, int statusCode) {
        EquipmentSnapshot snapshot = new EquipmentSnapshot(equipId);
        snapshot.applyField("power", power, Instant.now());
        snapshot.applyField("status_code", statusCode, Instant.now());
        return snapshot;
    }
}
