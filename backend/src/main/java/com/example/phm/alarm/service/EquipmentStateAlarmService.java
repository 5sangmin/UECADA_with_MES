package com.example.phm.alarm.service;

import java.time.LocalDateTime;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

import com.example.phm.alarm.entity.Alarm;
import com.example.phm.alarm.repository.AlarmRepository;
import com.example.phm.sensor.opcua.EquipmentSnapshot;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;

/**
 * X_DAS ns=3 의 {@code power} / {@code status_code} 신호 변화에 따라 alarm 테이블에 알람을 INSERT 한다.
 *
 * <p>휴리스틱(센서 임계치)이 아니라 실데이터 상태 신호 기반이므로, 쿨다운 대신
 * <b>상태 전환 시점에만</b> INSERT 한다. 직전 상태(power, status_code)를 in-memory 로 추적하며,
 * 동일 상태가 지속되는 동안에는 중복 INSERT 하지 않는다. 알람 해소는 담당자가 직접 처리한다.
 *
 * <p>전환 규칙:
 * <ul>
 *   <li>power true → false: severity=DANGER ({@code EQUIP_POWER_OFF})</li>
 *   <li>status_code 정상(0/1/4) → 2(WARNING): severity=WARNING ({@code EQUIP_STATUS_WARNING})</li>
 *   <li>status_code 정상(0/1/4) → 3(ERROR): severity=DANGER ({@code EQUIP_STATUS_ERROR})</li>
 *   <li>status_code 2(WARNING) → 3(ERROR): severity=DANGER (escalation)</li>
 * </ul>
 * 전원이 꺼진 상태(power=false)에서는 status_code 전환 알람을 생성하지 않는다(전원 알람으로 충분).
 */
@Service
public class EquipmentStateAlarmService {

    private static final Logger log = LoggerFactory.getLogger(EquipmentStateAlarmService.class);

    private static final int STATUS_WARNING = 2;
    private static final int STATUS_ERROR = 3;
    private static final Set<Integer> NORMAL_STATUS = Set.of(0, 1, 4);

    private final AlarmRepository alarmRepository;

    /** 설비별 마지막으로 관측한 상태 (전환 감지용). */
    private final ConcurrentHashMap<String, LastState> lastStates = new ConcurrentHashMap<>();

    public EquipmentStateAlarmService(AlarmRepository alarmRepository) {
        this.alarmRepository = alarmRepository;
    }

    /**
     * 스냅샷의 power/status_code 가 갱신될 때 호출. 직전 상태와 비교해 전환이 감지되면 alarm INSERT.
     * 동일 설비에 대한 평가를 원자적으로 수행해 중복 INSERT 를 방지한다.
     */
    public void evaluate(EquipmentSnapshot snapshot) {
        if (snapshot == null) {
            return;
        }
        String equipId = snapshot.getEquipId();
        Boolean power = snapshot.getPower();
        Integer statusCode = snapshot.getStatusCode();

        // power / status_code 둘 다 아직 수신 전이면 판정 보류
        if (power == null && statusCode == null) {
            return;
        }

        lastStates.compute(equipId, (key, prev) -> {
            evaluateTransition(snapshot, prev, power, statusCode);
            return new LastState(power, statusCode);
        });
    }

    private void evaluateTransition(
            EquipmentSnapshot snapshot,
            LastState prev,
            Boolean power,
            Integer statusCode
    ) {
        // 최초 관측: 기준선만 기록하고 INSERT 하지 않음 (전환 기준이 없으므로)
        if (prev == null) {
            return;
        }

        String equipId = snapshot.getEquipId();

        // 1. 전원 OFF 진입 (power true → false) — status_code 보다 우선
        boolean wasPowered = !Boolean.FALSE.equals(prev.power());
        boolean nowOff = Boolean.FALSE.equals(power);
        if (wasPowered && nowOff) {
            insert(snapshot, "EQUIP_POWER_OFF", "전원 차단", "DANGER",
                    "[" + equipId + "] 전원 OFF 감지 (power=false)");
            return;
        }

        // 전원이 꺼져 있으면 status_code 전환 알람은 생성하지 않음
        if (nowOff) {
            return;
        }

        // 2. status_code 전환
        if (statusCode == null) {
            return;
        }
        Integer prevStatus = prev.statusCode();
        boolean prevNormal = prevStatus == null || NORMAL_STATUS.contains(prevStatus);

        if (statusCode == STATUS_ERROR
                && (prevNormal || prevStatus == STATUS_WARNING)
                && prevStatus != null && prevStatus != STATUS_ERROR) {
            insert(snapshot, "EQUIP_STATUS_ERROR", "설비 에러", "DANGER",
                    "[" + equipId + "] 설비 ERROR (status_code=3)");
        } else if (statusCode == STATUS_WARNING && prevNormal && (prevStatus == null || prevStatus != STATUS_WARNING)) {
            insert(snapshot, "EQUIP_STATUS_WARNING", "설비 경고", "WARNING",
                    "[" + equipId + "] 설비 WARNING (status_code=2)");
        }
    }

    private void insert(EquipmentSnapshot snapshot, String alarmCode, String alarmType,
                        String severity, String message) {
        Alarm alarm = new Alarm();
        alarm.setEquipmentCode(snapshot.getEquipId());
        alarm.setAlarmCode(alarmCode);
        alarm.setAlarmType(alarmType);
        alarm.setAlarmCategory("공통");
        alarm.setSeverity(severity);
        alarm.setAlarmMessage(message);
        alarm.setStatus("OPEN");
        alarm.setOccurredAt(LocalDateTime.now());
        alarm.setSensorSnapshot(toJson(snapshot));

        alarmRepository.save(alarm);
        log.info("[EquipmentStateAlarm] {} {} - {} ({})", severity, snapshot.getEquipId(), alarmType, alarmCode);
    }

    private String toJson(EquipmentSnapshot s) {
        return String.format(
                Locale.ROOT,
                "{\"power\":%s,\"status_code\":%s,\"heartbeat\":%s,\"quality_code\":%s,"
                        + "\"cmd_status\":%s,\"progress\":%s,\"cycle_time\":%s,\"part_count\":%s,"
                        + "\"data_1_sensor\":%s,\"data_2_sensor\":%s,\"data_3_sensor\":%s,"
                        + "\"line_id\":%s,\"equipment_id\":%s}",
                s.getPower(), s.getStatusCode(), s.getHeartbeat(), s.getQualityCode(),
                s.getCmdStatus(), s.getProgress(), s.getCycleTime(), s.getPartCount(),
                s.getData1Sensor(), s.getData2Sensor(), s.getData3Sensor(),
                s.getLineId(), s.getEquipmentId());
    }

    private record LastState(Boolean power, Integer statusCode) {
    }
}
