package com.example.phm.line.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import com.example.phm.analysis.entity.AnalysisResult;
import com.example.phm.analysis.repository.AnalysisResultRepository;
import com.example.phm.equipment.entity.Equipment;
import com.example.phm.equipment.entity.EquipmentStatus;
import com.example.phm.equipment.repository.EquipmentRepository;
import com.example.phm.equipment.repository.EquipmentStatusRepository;
import com.example.phm.line.entity.ProductionLine;
import com.example.phm.line.repository.ProductionLineRepository;
import com.example.phm.sensor.service.RealtimeEquipmentService;
import com.example.phm.vibration.dto.VibrationRealtimeResponse;
import com.example.phm.vibration.service.VibrationWindowMonitorService;
import org.junit.jupiter.api.Test;

class LineAggregationServiceTest {

    /**
     * N+1 회피 검증: 설비 N 대에 대해 analysisResultRepository.findTopBy... 가
     * 설비 수 만큼이 아니라, 새로 추가된 batch 메서드 findLatestForEquipmentCodes 가
     * "딱 1번" 호출되는지 확인한다.
     */
    @Test
    void getLines_doesNotIssueN1QueriesForAnalysisLatest() throws Exception {
        ProductionLine line = lineOf("LINE-A", "FACTORY-01", "라인 A", null);

        Equipment e1 = new Equipment();
        e1.setEquipmentCode("EQ-1");
        e1.setLocation("LINE-A");

        Equipment e2 = new Equipment();
        e2.setEquipmentCode("EQ-2");
        e2.setLocation("LINE-A");

        EquipmentStatus s1 = new EquipmentStatus();
        s1.setEquipId("EQ-1");
        s1.setStatusCode("RUNNING");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(e1, e2));
        when(statusRepo.findAll()).thenReturn(List.of(s1));
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.<AnalysisResult>of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride(anyString())).thenReturn(null);

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result).hasSize(1);
        assertThat(result.get(0).lineId()).isEqualTo("LINE-A");

        verify(analysisRepo, times(1)).findLatestForEquipmentCodes(any());
        verify(analysisRepo, never()).findTopByEquipmentCodeOrderByCreatedAtDesc(anyString());
    }

    @Test
    void getLines_usesRealtimeSensorStatusBeforeSeededDatabaseStatus() throws Exception {
        ProductionLine line = lineOf("LINE-01", "FACTORY-01", "1라인", "RUNNING");

        Equipment equipment = new Equipment();
        equipment.setEquipmentCode("LINE-01_CAST-01");
        equipment.setLocation("LINE-01");

        EquipmentStatus seededAlarm = new EquipmentStatus();
        seededAlarm.setEquipId("LINE-01_CAST-01");
        seededAlarm.setStatusCode("ALARM");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(equipment));
        when(statusRepo.findAll()).thenReturn(List.of(seededAlarm));
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.<AnalysisResult>of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride("LINE-01_CAST-01")).thenReturn("RUNNING");

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result).hasSize(1);
        assertThat(result.get(0).equipmentRunning()).isEqualTo(1);
        assertThat(result.get(0).equipmentAlarm()).isZero();
        assertThat(result.get(0).lineStatus()).isEqualTo("RUNNING");
    }

    // ── 신규 5-상태 집계 검증 ────────────────────────────────────────────────

    @Test
    void getLines_COMPLETEStatusCountsAsStandby() throws Exception {
        ProductionLine line = lineOf("LINE-01", "FACTORY-01", "1라인", "RUNNING");

        Equipment equipment = new Equipment();
        equipment.setEquipmentCode("LINE-01_CNC-01");
        equipment.setLocation("LINE-01");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(equipment));
        when(statusRepo.findAll()).thenReturn(List.of());
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride("LINE-01_CNC-01")).thenReturn("COMPLETE");

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result).hasSize(1);
        assertThat(result.get(0).equipmentStandby()).isEqualTo(1);  // COMPLETE → standby 버킷
        assertThat(result.get(0).equipmentAlarm()).isZero();
        assertThat(result.get(0).equipmentRunning()).isZero();
    }

    @Test
    void getLines_WARNINGStatusCountsAsAlarm() throws Exception {
        ProductionLine line = lineOf("LINE-01", "FACTORY-01", "1라인", "RUNNING");

        Equipment equipment = new Equipment();
        equipment.setEquipmentCode("LINE-01_CNC-01");
        equipment.setLocation("LINE-01");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(equipment));
        when(statusRepo.findAll()).thenReturn(List.of());
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride("LINE-01_CNC-01")).thenReturn("WARNING");

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result.get(0).equipmentAlarm()).isEqualTo(1);   // WARNING → alarm 버킷
        assertThat(result.get(0).lineStatus()).isEqualTo("ALARM"); // 알람 있으면 라인도 ALARM
        assertThat(result.get(0).equipmentRunning()).isZero();
    }

    @Test
    void getLines_ERRORStatusCountsAsAlarm() throws Exception {
        ProductionLine line = lineOf("LINE-01", "FACTORY-01", "1라인", "RUNNING");

        Equipment equipment = new Equipment();
        equipment.setEquipmentCode("LINE-01_CNC-01");
        equipment.setLocation("LINE-01");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(equipment));
        when(statusRepo.findAll()).thenReturn(List.of());
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride("LINE-01_CNC-01")).thenReturn("ERROR");

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result.get(0).equipmentAlarm()).isEqualTo(1);   // ERROR → alarm 버킷
        assertThat(result.get(0).lineStatus()).isEqualTo("ALARM");
    }

    @Test
    void getLines_MAINTENANCEStatusCountedSeparately() throws Exception {
        ProductionLine line = lineOf("LINE-01", "FACTORY-01", "1라인", "RUNNING");

        Equipment equipment = new Equipment();
        equipment.setEquipmentCode("LINE-01_CNC-01");
        equipment.setLocation("LINE-01");

        ProductionLineRepository lineRepo = mock(ProductionLineRepository.class);
        EquipmentRepository equipmentRepo = mock(EquipmentRepository.class);
        EquipmentStatusRepository statusRepo = mock(EquipmentStatusRepository.class);
        AnalysisResultRepository analysisRepo = mock(AnalysisResultRepository.class);
        VibrationWindowMonitorService monitor = mock(VibrationWindowMonitorService.class);
        RealtimeEquipmentService realtimeEquipmentService = mock(RealtimeEquipmentService.class);

        when(lineRepo.findByFactoryId("FACTORY-01")).thenReturn(List.of(line));
        when(equipmentRepo.findAll()).thenReturn(List.of(equipment));
        when(statusRepo.findAll()).thenReturn(List.of());
        when(analysisRepo.findLatestForEquipmentCodes(any())).thenReturn(List.of());
        when(monitor.latestRealtime(anyString())).thenReturn(VibrationRealtimeResponse.empty("any"));
        when(realtimeEquipmentService.statusOverride("LINE-01_CNC-01")).thenReturn("MAINTENANCE");

        LineAggregationService service = new LineAggregationService(
                lineRepo, equipmentRepo, statusRepo, analysisRepo, monitor, realtimeEquipmentService
        );

        var result = service.getLines("FACTORY-01");

        assertThat(result.get(0).equipmentMaintenance()).isEqualTo(1); // MAINTENANCE → 별도 버킷
        assertThat(result.get(0).equipmentAlarm()).isZero();
        assertThat(result.get(0).equipmentStandby()).isZero();
        assertThat(result.get(0).lineStatus()).isEqualTo("RUNNING");   // 알람 없으면 원래 라인 상태
    }

    private ProductionLine lineOf(String id, String factory, String name, String status) throws Exception {
        ProductionLine line = ProductionLine.class.getDeclaredConstructor().newInstance();
        for (var field : ProductionLine.class.getDeclaredFields()) {
            field.setAccessible(true);
            switch (field.getName()) {
                case "lineId" -> field.set(line, id);
                case "factoryId" -> field.set(line, factory);
                case "lineName" -> field.set(line, name);
                case "lineStatus" -> field.set(line, status);
                default -> { /* no-op */ }
            }
        }
        return line;
    }
}
