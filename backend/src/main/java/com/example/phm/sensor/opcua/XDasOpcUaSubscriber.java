package com.example.phm.sensor.opcua;

import static org.eclipse.milo.opcua.stack.core.types.builtin.unsigned.Unsigned.uint;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Function;
import java.util.stream.Collectors;

import com.example.phm.alarm.service.EquipmentStateAlarmService;
import com.example.phm.config.XDasOpcUaProperties;
import com.example.phm.sensor.SensorBufferRegistry;
import com.example.phm.sensor.SensorFrame;
import org.eclipse.milo.opcua.sdk.client.OpcUaClient;
import org.eclipse.milo.opcua.sdk.client.api.subscriptions.UaMonitoredItem;
import org.eclipse.milo.opcua.sdk.client.api.subscriptions.UaSubscription;
import org.eclipse.milo.opcua.stack.core.AttributeId;
import org.eclipse.milo.opcua.stack.core.types.builtin.DataValue;
import org.eclipse.milo.opcua.stack.core.types.builtin.NodeId;
import org.eclipse.milo.opcua.stack.core.types.builtin.QualifiedName;
import org.eclipse.milo.opcua.stack.core.types.builtin.StatusCode;
import org.eclipse.milo.opcua.stack.core.types.enumerated.MonitoringMode;
import org.eclipse.milo.opcua.stack.core.types.enumerated.TimestampsToReturn;
import org.eclipse.milo.opcua.stack.core.types.structured.MonitoredItemCreateRequest;
import org.eclipse.milo.opcua.stack.core.types.structured.MonitoringParameters;
import org.eclipse.milo.opcua.stack.core.types.structured.ReadValueId;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.context.SmartLifecycle;
import org.springframework.stereotype.Component;

/**
 * X_DAS OPC UA 구독기.
 *
 * <p>두 네임스페이스를 동시에 구독한다:
 * <ul>
 *   <li><b>ns=2 (deprecated)</b>: 기존 numeric leaf → {@link SensorBufferRegistry} ring buffer.
 *       진동 분석(ai-api) 등 기존 경로가 사용하므로 유지하되 신규 로직에서는 사용하지 않는다.
 *       향후 영향 평가 후 별도 PR 에서 정리 예정.</li>
 *   <li><b>ns=3 (canonical)</b>: power/status_code 등 13+필드 → {@link EquipmentSnapshotStore} 스냅샷.
 *       설비상태 판정 및 알람 생성의 단일 진실 소스.</li>
 * </ul>
 */
@Component
public class XDasOpcUaSubscriber implements SmartLifecycle {

    private static final Logger log = LoggerFactory.getLogger(XDasOpcUaSubscriber.class);
    private static final NodeId SERVER_STATUS_NODE_ID = new NodeId(0, 2256);

    private final XDasOpcUaProperties properties;
    private final SensorBufferRegistry registry;
    private final EquipmentSnapshotStore snapshotStore;
    private final EquipmentStateAlarmService stateAlarmService;
    private final AtomicBoolean running = new AtomicBoolean(false);
    private final AtomicLong clientHandle = new AtomicLong(1);

    private volatile ExecutorService executor;
    private volatile OpcUaClient client;
    private volatile Map<String, XDasOpcUaNodeMapping> mappingsByNodeId = Map.of();
    private volatile Map<String, XDasNs3NodeMapping> ns3MappingsByNodeId = Map.of();

    public XDasOpcUaSubscriber(
            XDasOpcUaProperties properties,
            SensorBufferRegistry registry,
            EquipmentSnapshotStore snapshotStore,
            EquipmentStateAlarmService stateAlarmService
    ) {
        this.properties = properties;
        this.registry = registry;
        this.snapshotStore = snapshotStore;
        this.stateAlarmService = stateAlarmService;
    }

    @Override
    public void start() {
        if (!properties.enabled()) {
            log.info("X_DAS OPC UA subscription is disabled");
            return;
        }
        if (!running.compareAndSet(false, true)) {
            return;
        }

        executor = Executors.newSingleThreadExecutor(runnable -> {
            Thread thread = new Thread(runnable, "x-das-opcua-subscriber");
            thread.setDaemon(true);
            return thread;
        });
        executor.submit(this::runSubscriptionLoop);
    }

    @Override
    public void stop() {
        running.set(false);
        disconnectClient();

        ExecutorService currentExecutor = executor;
        if (currentExecutor != null) {
            currentExecutor.shutdownNow();
        }
    }

    @Override
    public boolean isRunning() {
        return running.get();
    }

    @Override
    public int getPhase() {
        return Integer.MAX_VALUE;
    }

    private void runSubscriptionLoop() {
        while (running.get()) {
            try {
                subscribeUntilStopped();
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
                return;
            } catch (Exception exception) {
                if (running.get()) {
                    log.warn(
                            "X_DAS OPC UA subscription failed. endpoint={}, retryInMs={}, reason={}",
                            properties.endpointUrl(),
                            properties.reconnectDelayMs(),
                            exception.getMessage()
                    );
                    try {
                        sleepBeforeRetry();
                    } catch (InterruptedException interruptedException) {
                        Thread.currentThread().interrupt();
                        return;
                    }
                }
            }
        }
    }

    private void subscribeUntilStopped() throws Exception {
        // ns=2 (deprecated) — ring buffer 호환 유지
        List<XDasOpcUaNodeMapping> ns2Mappings =
                XDasOpcUaNodeMappings.defaults(properties.includeLine01AliasBuffers());
        mappingsByNodeId = ns2Mappings.stream()
                .collect(Collectors.toUnmodifiableMap(XDasOpcUaNodeMapping::nodeId, Function.identity()));

        // ns=3 (canonical) — 스냅샷 + 알람
        List<XDasNs3NodeMapping> ns3Mappings = XDasNs3NodeMappings.defaults();
        ns3MappingsByNodeId = ns3Mappings.stream()
                .collect(Collectors.toUnmodifiableMap(XDasNs3NodeMapping::nodeId, Function.identity()));

        OpcUaClient currentClient = OpcUaClient.create(properties.endpointUrl());
        client = currentClient;
        try {
            currentClient.connect().get(10, TimeUnit.SECONDS);

            UaSubscription subscription = currentClient
                    .getSubscriptionManager()
                    .createSubscription(properties.publishingIntervalMs())
                    .get(10, TimeUnit.SECONDS);

            List<MonitoredItemCreateRequest> requests = new ArrayList<>();
            ns2Mappings.forEach(mapping -> requests.add(createRequest(mapping.nodeId())));
            ns3Mappings.forEach(mapping -> requests.add(createRequest(mapping.nodeId())));

            List<UaMonitoredItem> monitoredItems = subscription.createMonitoredItems(
                    TimestampsToReturn.Both,
                    requests,
                    (item, index) -> item.setValueConsumer(this::recordValue)
            ).get(10, TimeUnit.SECONDS);

            long goodItems = monitoredItems.stream()
                    .filter(item -> item.getStatusCode() != null && item.getStatusCode().isGood())
                    .count();
            if (goodItems == 0) {
                throw new IllegalStateException("No X_DAS OPC UA nodes were accepted by the server yet");
            }
            if (goodItems < monitoredItems.size()) {
                // ns=3 노드가 서버에 아직 없으면 일부만 수락됨 — 전체 실패시키지 않고 진행 (fallback)
                log.warn(
                        "X_DAS OPC UA accepted only some monitored items. accepted={}, requested={}",
                        goodItems,
                        monitoredItems.size()
                );
            }

            log.info(
                    "Subscribed to X_DAS OPC UA endpoint. endpoint={}, ns2Nodes={}, ns3Nodes={}",
                    properties.endpointUrl(),
                    ns2Mappings.size(),
                    ns3Mappings.size()
            );

            while (running.get()) {
                currentClient.readValue(0.0, TimestampsToReturn.Both, SERVER_STATUS_NODE_ID)
                        .get(5, TimeUnit.SECONDS);
                Thread.sleep(5000L);
            }
        } finally {
            disconnectClient();
        }
    }

    private MonitoredItemCreateRequest createRequest(String nodeId) {
        int handle = (int) clientHandle.getAndIncrement();

        ReadValueId readValueId = new ReadValueId(
                NodeId.parse(nodeId),
                AttributeId.Value.uid(),
                null,
                QualifiedName.NULL_VALUE
        );
        MonitoringParameters parameters = new MonitoringParameters(
                uint(handle),
                properties.publishingIntervalMs(),
                null,
                uint(properties.queueSize()),
                true
        );
        return new MonitoredItemCreateRequest(readValueId, MonitoringMode.Reporting, parameters);
    }

    private void recordValue(UaMonitoredItem item, DataValue dataValue) {
        StatusCode statusCode = dataValue.getStatusCode();
        if (statusCode != null && !statusCode.isGood()) {
            log.debug("Skipping bad X_DAS OPC UA value. nodeId={}, status={}", item.getReadValueId().getNodeId(), statusCode);
            return;
        }

        Object rawValue = dataValue.getValue() == null ? null : dataValue.getValue().getValue();
        if (rawValue == null) {
            return;
        }

        String nodeId = item.getReadValueId().getNodeId().toParseableString();

        // ns=3 canonical: 스냅샷 갱신 + 상태 전환 알람
        XDasNs3NodeMapping ns3Mapping = ns3MappingsByNodeId.get(nodeId);
        if (ns3Mapping != null) {
            recordNs3Value(ns3Mapping, rawValue);
            return;
        }

        // ns=2 (deprecated): ring buffer push (numeric 만)
        recordNs2Value(nodeId, rawValue);
    }

    private void recordNs3Value(XDasNs3NodeMapping mapping, Object rawValue) {
        EquipmentSnapshot snapshot = snapshotStore.update(mapping.equipId(), mapping.field(), rawValue);
        // 상태 신호(power/status_code) 변화 시에만 알람 평가
        if ("power".equals(mapping.field()) || "status_code".equals(mapping.field())) {
            stateAlarmService.evaluate(snapshot);
        }
    }

    private void recordNs2Value(String nodeId, Object rawValue) {
        Double numericValue = numericValue(rawValue);
        if (numericValue == null) {
            log.debug("Skipping non-numeric ns=2 X_DAS OPC UA value. nodeId={}, value={}", nodeId, rawValue);
            return;
        }
        XDasOpcUaNodeMapping mapping = mappingsByNodeId.get(nodeId);
        if (mapping == null) {
            log.debug("Skipping unmapped X_DAS OPC UA value. nodeId={}", nodeId);
            return;
        }
        SensorFrame frame = new SensorFrame(System.currentTimeMillis(), numericValue);
        mapping.bufferKeys().forEach(bufferKey -> registry.push(bufferKey, frame));
    }

    private Double numericValue(Object rawValue) {
        if (rawValue instanceof Number number) {
            return number.doubleValue();
        }
        if (rawValue instanceof Boolean bool) {
            return bool ? 1.0 : 0.0;
        }
        return null;
    }

    private void disconnectClient() {
        OpcUaClient currentClient = client;
        client = null;
        if (currentClient != null) {
            try {
                currentClient.disconnect().get(3, TimeUnit.SECONDS);
            } catch (Exception exception) {
                log.debug("Failed to disconnect X_DAS OPC UA client cleanly: {}", exception.getMessage());
            }
        }
    }

    private void sleepBeforeRetry() throws InterruptedException {
        Thread.sleep(properties.reconnectDelayMs());
    }
}
