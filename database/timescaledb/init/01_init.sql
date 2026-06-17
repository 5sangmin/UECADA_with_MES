CREATE EXTENSION IF NOT EXISTS timescaledb;


-- 1) 기본 테이블 정의
CREATE TABLE IF NOT EXISTS public.equipment_snapshot (
    ts timestamptz NOT NULL,
    line_id text NOT NULL,
    equipment_id text NOT NULL,

    equipment_type text,
    heartbeat bigint,
    quality_code smallint,
    power boolean,
    status_code smallint,
    progress double precision,
    cycle_time double precision,
    part_count bigint,
    data1_setpoint double precision,
    data1_sensor double precision,
    data2_setpoint double precision,
    data2_sensor double precision,
    data3_setpoint double precision,
    data3_sensor double precision,
    externaldata1_sensor double precision,
    externaldata2_sensor double precision,
    externaldata3_sensor double precision,
    externaldata4_sensor double precision,
    cmd_id bigint,
    cmd_accepted smallint,
    cmd_status smallint,
    created_at timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT equipment_snapshot_pkey
        PRIMARY KEY (ts, line_id, equipment_id)
);

SELECT create_hypertable(
    'equipment_snapshot',
    'ts',
    if_not_exists => TRUE,
    migrate_data => TRUE
);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_line_eq_ts_desc
    ON public.equipment_snapshot (line_id, equipment_id, ts DESC);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_eqtype_ts_desc
    ON public.equipment_snapshot (equipment_type, ts DESC);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_created_at
    ON public.equipment_snapshot (created_at DESC);