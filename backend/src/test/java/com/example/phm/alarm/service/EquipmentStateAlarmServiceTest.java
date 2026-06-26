package com.example.phm.alarm.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.argThat;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;

import java.time.Instant;

import com.example.phm.alarm.entity.Alarm;
import com.example.phm.alarm.repository.AlarmRepository;
import com.example.phm.sensor.opcua.EquipmentSnapshot;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;

class EquipmentStateAlarmServiceTest {

    private AlarmRepository alarmRepository;
    private EquipmentStateAlarmService service;

    @BeforeEach
    void setUp() {
        alarmRepository = mock(AlarmRepository.class);
        service = new EquipmentStateAlarmService(alarmRepository);
    }

    // ── 첫 관측 ──────────────────────────────────────────────────────────────

    @Test
    void firstObservation_noAlarm() {
        service.evaluate(snap("EQ-1", true, 1));

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    // ── 정상 상태 간 전환 (0=IDLE, 1=RUNNING, 2=COMPLETE) ───────────────────

    @Test
    void idleToRunning_noAlarm() {
        service.evaluate(snap("EQ-1", true, 0));
        service.evaluate(snap("EQ-1", true, 1));

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void runningToComplete_noAlarm() {
        service.evaluate(snap("EQ-1", true, 1));
        service.evaluate(snap("EQ-1", true, 2));

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void completeToIdle_noAlarm() {
        service.evaluate(snap("EQ-1", true, 2));
        service.evaluate(snap("EQ-1", true, 0));

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    // ── 정상 → WARNING(3) ────────────────────────────────────────────────────

    @Test
    void runningToWarning_insertsWarningAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // RUNNING → 기준선
        service.evaluate(snap("EQ-1", true, 3));   // → WARNING

        ArgumentCaptor<Alarm> captor = ArgumentCaptor.forClass(Alarm.class);
        verify(alarmRepository, times(1)).save(captor.capture());
        Alarm alarm = captor.getValue();
        assertThat(alarm.getAlarmCode()).isEqualTo("EQUIP_STATUS_WARNING");
        assertThat(alarm.getSeverity()).isEqualTo("WARNING");
        assertThat(alarm.getStatus()).isEqualTo("OPEN");
    }

    @Test
    void idleToWarning_insertsWarningAlarm() {
        service.evaluate(snap("EQ-1", true, 0));   // IDLE → 기준선
        service.evaluate(snap("EQ-1", true, 3));   // → WARNING

        verify(alarmRepository, times(1)).save(
                argThat(a -> "EQUIP_STATUS_WARNING".equals(a.getAlarmCode()))
        );
    }

    @Test
    void completeToWarning_insertsWarningAlarm() {
        // COMPLETE(2)는 정상 상태이므로 WARNING(3) 전환 시 알람 발생
        service.evaluate(snap("EQ-1", true, 2));   // COMPLETE → 기준선
        service.evaluate(snap("EQ-1", true, 3));   // → WARNING

        verify(alarmRepository, times(1)).save(
                argThat(a -> "EQUIP_STATUS_WARNING".equals(a.getAlarmCode()))
        );
    }

    // ── 정상 → ERROR(4) ──────────────────────────────────────────────────────

    @Test
    void runningToError_insertsDangerAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // RUNNING → 기준선
        service.evaluate(snap("EQ-1", true, 4));   // → ERROR

        ArgumentCaptor<Alarm> captor = ArgumentCaptor.forClass(Alarm.class);
        verify(alarmRepository, times(1)).save(captor.capture());
        Alarm alarm = captor.getValue();
        assertThat(alarm.getAlarmCode()).isEqualTo("EQUIP_STATUS_ERROR");
        assertThat(alarm.getSeverity()).isEqualTo("DANGER");
        assertThat(alarm.getStatus()).isEqualTo("OPEN");
    }

    @Test
    void completeToError_insertsDangerAlarm() {
        service.evaluate(snap("EQ-1", true, 2));   // COMPLETE → 기준선
        service.evaluate(snap("EQ-1", true, 4));   // → ERROR

        verify(alarmRepository, times(1)).save(
                argThat(a -> "EQUIP_STATUS_ERROR".equals(a.getAlarmCode()) && "DANGER".equals(a.getSeverity()))
        );
    }

    // ── WARNING(3) → ERROR(4) 에스컬레이션 ──────────────────────────────────

    @Test
    void warningToError_insertsDangerAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // 기준선
        service.evaluate(snap("EQ-1", true, 3));   // WARNING
        service.evaluate(snap("EQ-1", true, 4));   // ERROR (에스컬레이션)

        ArgumentCaptor<Alarm> captor = ArgumentCaptor.forClass(Alarm.class);
        verify(alarmRepository, times(2)).save(captor.capture());
        Alarm escalation = captor.getAllValues().get(1);
        assertThat(escalation.getAlarmCode()).isEqualTo("EQUIP_STATUS_ERROR");
        assertThat(escalation.getSeverity()).isEqualTo("DANGER");
    }

    // ── 복구 (에러 → 정상) ───────────────────────────────────────────────────

    @Test
    void errorToRunning_noAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // 기준선
        service.evaluate(snap("EQ-1", true, 4));   // ERROR
        service.evaluate(snap("EQ-1", true, 1));   // 복구 → RUNNING

        // 복구 시 추가 알람 없음 (첫 번째 알람만 있어야 함)
        verify(alarmRepository, times(1)).save(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void warningToRunning_noAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // 기준선
        service.evaluate(snap("EQ-1", true, 3));   // WARNING
        service.evaluate(snap("EQ-1", true, 1));   // 복구

        verify(alarmRepository, times(1)).save(org.mockito.ArgumentMatchers.any());
    }

    // ── 동일 상태 반복 — 중복 알람 없음 ─────────────────────────────────────

    @Test
    void repeatedWarning_noAdditionalAlarm() {
        service.evaluate(snap("EQ-1", true, 1));
        service.evaluate(snap("EQ-1", true, 3));   // 1번 알람
        service.evaluate(snap("EQ-1", true, 3));   // 동일 상태 → 추가 알람 없음

        verify(alarmRepository, times(1)).save(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void repeatedError_noAdditionalAlarm() {
        service.evaluate(snap("EQ-1", true, 1));
        service.evaluate(snap("EQ-1", true, 4));   // 1번 알람
        service.evaluate(snap("EQ-1", true, 4));   // 동일 상태 → 추가 알람 없음

        verify(alarmRepository, times(1)).save(org.mockito.ArgumentMatchers.any());
    }

    // ── 전원 ─────────────────────────────────────────────────────────────────

    @Test
    void powerOnToOff_insertsPowerOffAlarm() {
        service.evaluate(snap("EQ-1", true, 1));   // power ON 기준선
        service.evaluate(snap("EQ-1", false, 1));  // power OFF

        ArgumentCaptor<Alarm> captor = ArgumentCaptor.forClass(Alarm.class);
        verify(alarmRepository, times(1)).save(captor.capture());
        assertThat(captor.getValue().getAlarmCode()).isEqualTo("EQUIP_POWER_OFF");
        assertThat(captor.getValue().getSeverity()).isEqualTo("DANGER");
    }

    @Test
    void powerOff_statusChange_noStatusAlarm() {
        // 전원이 꺼진 상태에서 status_code 가 바뀌어도 status 알람은 생성하지 않음
        service.evaluate(snap("EQ-1", false, 0));  // power OFF 기준선
        service.evaluate(snap("EQ-1", false, 4));  // power OFF + ERROR code → 알람 없음

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    @Test
    void powerOffToOn_noAlarm() {
        service.evaluate(snap("EQ-1", false, 0));  // power OFF 기준선
        service.evaluate(snap("EQ-1", true, 1));   // power ON 복구 → 알람 없음

        verify(alarmRepository, never()).save(org.mockito.ArgumentMatchers.any());
    }

    // ── 설비 독립성 ──────────────────────────────────────────────────────────

    @Test
    void differentEquipments_trackedIndependently() {
        service.evaluate(snap("EQ-1", true, 1));
        service.evaluate(snap("EQ-2", true, 1));
        service.evaluate(snap("EQ-1", true, 3));   // EQ-1만 경고
        service.evaluate(snap("EQ-2", true, 1));   // EQ-2는 여전히 정상

        verify(alarmRepository, times(1)).save(
                argThat(a -> "EQ-1".equals(a.getEquipmentCode()))
        );
    }

    // ─────────────────────────────────────────────────────────────────────────

    private EquipmentSnapshot snap(String equipId, boolean power, int statusCode) {
        EquipmentSnapshot s = new EquipmentSnapshot(equipId);
        s.applyField("power", power, Instant.now());
        s.applyField("status_code", statusCode, Instant.now());
        return s;
    }
}
