CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS equipment_snapshot (
    id bigserial PRIMARY KEY,
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
    payload_json jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

SELECT create_hypertable(
    'equipment_snapshot',
    'ts',
    if_not_exists => TRUE,
    migrate_data => TRUE
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_equipment_snapshot_ts_line_eq
    ON public.equipment_snapshot (ts, line_id, equipment_id);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_line_eq_ts_desc
    ON public.equipment_snapshot (line_id, equipment_id, ts DESC);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_eqtype_ts_desc
    ON public.equipment_snapshot (equipment_type, ts DESC);

CREATE INDEX IF NOT EXISTS idx_equipment_snapshot_created_at
    ON public.equipment_snapshot (created_at DESC);
    