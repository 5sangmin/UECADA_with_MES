-- 0) command_id 발급용 sequence (BeApi 가 nextval 로 가져감)
--    int32 범위를 넘지 않도로 maxvalue 설정. cycle 안 함.
CREATE SEQUENCE IF NOT EXISTS public.command_id_seq
    AS integer
    INCREMENT BY 1
    MINVALUE 1
    MAXVALUE 2147483647
    START WITH 1
    NO CYCLE;

-- 1) 외부 시스템이 적재하는 요청 원장
--    command_id: UDP wire 의 std::int32_t cmd_id 와 1:1 대응 (32-bit signed).
--                외부 입력 시에도 정수만 허용. 단조 증가 권장.
CREATE TABLE IF NOT EXISTS public.command_request (
    id bigserial PRIMARY KEY,
    command_id integer NOT NULL UNIQUE,
    source_type text NOT NULL,
    line_id integer NOT NULL,
    equipment_id integer NOT NULL,
    command_type text NOT NULL,
    command_value jsonb,
    request_json jsonb NOT NULL,
    status text NOT NULL DEFAULT 'REQUESTED',
    priority integer NOT NULL DEFAULT 100,
    retry_count integer NOT NULL DEFAULT 0,
    max_retry integer NOT NULL DEFAULT 0,
    created_by text,
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    finished_at timestamptz,
    picked_at timestamptz,
    worker_id text,
    last_error text,
    idempotency_key text
);

CREATE INDEX IF NOT EXISTS idx_command_request_status_priority_created
    ON public.command_request (status, priority, created_at);

CREATE INDEX IF NOT EXISTS idx_command_request_line_equipment
    ON public.command_request (line_id, equipment_id);

CREATE INDEX IF NOT EXISTS idx_command_request_created_at
    ON public.command_request (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_command_request_idempotency_key
    ON public.command_request (idempotency_key);


-- 2) x-das Node-RED가 관측한 응답 이벤트 로그
--    command_id 는 command_request.command_id 와 동일한 int32 echo (UDP 응답 record 의 cmd_id).
CREATE TABLE IF NOT EXISTS public.command_response_event (
    id bigserial PRIMARY KEY,
    command_id integer NOT NULL,
    line_id integer NOT NULL,
    equipment_id integer NOT NULL,
    accepted boolean,
    cmd_status smallint,
    status text NOT NULL,
    response_json jsonb,
    source_tsepochms bigint,
    observed_at timestamptz NOT NULL DEFAULT now(),
    snapshot_key text,
    source_node_id text,
    last_error text
);

CREATE INDEX IF NOT EXISTS idx_command_response_event_command
    ON public.command_response_event (command_id, observed_at DESC);

CREATE INDEX IF NOT EXISTS idx_command_response_event_line_equipment
    ON public.command_response_event (line_id, equipment_id, observed_at DESC);

CREATE INDEX IF NOT EXISTS idx_command_response_event_observed_at
    ON public.command_response_event (observed_at DESC);


-- 3) 설비별 최신 응답 projection
CREATE TABLE IF NOT EXISTS public.command_latest_response (
    line_id integer NOT NULL,
    equipment_id integer NOT NULL,
    command_id integer NOT NULL,
    command_type text,
    status text NOT NULL,
    accepted boolean,
    cmd_status smallint,
    response_json jsonb,
    source_tsepochms bigint,
    first_observed_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    snapshot_key text,
    source_node_id text,
    last_error text,
    PRIMARY KEY (line_id, equipment_id)
);

CREATE INDEX IF NOT EXISTS idx_command_latest_response_command_id
    ON public.command_latest_response (command_id);

CREATE INDEX IF NOT EXISTS idx_command_latest_response_updated_at
    ON public.command_latest_response (updated_at DESC);


-- 4) history는 request + response_event 기반 view
CREATE OR REPLACE VIEW public.command_history AS
SELECT
    r.command_id,
    r.source_type,
    r.line_id,
    r.equipment_id,
    r.command_type,
    r.command_value,
    r.request_json,
    r.status AS request_status,
    r.priority,
    r.retry_count,
    r.max_retry,
    r.created_by,
    r.created_at AS request_created_at,
    r.started_at AS request_started_at,
    r.finished_at AS request_finished_at,
    r.picked_at,
    r.worker_id AS request_worker_id,
    r.last_error AS request_last_error,
    r.idempotency_key,

    e.id AS response_event_id,
    LAG(e.status) OVER (
        PARTITION BY e.command_id, e.line_id, e.equipment_id
        ORDER BY e.observed_at, e.id
    ) AS old_status,
    e.status AS new_status,
    e.accepted,
    e.cmd_status,
    e.response_json,
    e.source_tsepochms,
    e.observed_at AS changed_at,
    e.source_node_id,
    e.last_error AS response_last_error,
    ROW_NUMBER() OVER (
        PARTITION BY e.command_id, e.line_id, e.equipment_id
        ORDER BY e.observed_at, e.id
    ) - 1 AS process_index
FROM public.command_request r
LEFT JOIN public.command_response_event e
  ON e.command_id = r.command_id
 AND e.line_id = r.line_id
 AND e.equipment_id = r.equipment_id;