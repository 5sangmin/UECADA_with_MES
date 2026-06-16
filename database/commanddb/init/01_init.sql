CREATE TABLE IF NOT EXISTS command_queue (
    id bigserial PRIMARY KEY,
    command_id text NOT NULL UNIQUE,
    source_type text NOT NULL,
    line_id text NOT NULL,
    equipment_id text NOT NULL,
    command_type text NOT NULL,
    command_value jsonb,
    request_json jsonb NOT NULL,
    response_json jsonb,
    status text NOT NULL DEFAULT 'NEW',
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

CREATE INDEX IF NOT EXISTS idx_command_queue_status_priority_created
    ON command_queue (status, priority, created_at);

CREATE INDEX IF NOT EXISTS idx_command_queue_line_equipment
    ON command_queue (line_id, equipment_id);

CREATE INDEX IF NOT EXISTS idx_command_queue_created_at
    ON command_queue (created_at DESC);

CREATE TABLE IF NOT EXISTS command_history (
    id bigserial PRIMARY KEY,
    command_id text NOT NULL,
    old_status text,
    new_status text NOT NULL,
    changed_at timestamptz NOT NULL DEFAULT now(),
    changed_by text,
    worker_id text,
    message text,
    process_index integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_command_history_command_id
    ON command_history (command_id, changed_at);

CREATE TABLE IF NOT EXISTS command_latest_response (
    line_id text NOT NULL,
    equipment_id text NOT NULL,
    command_id text NOT NULL,
    command_type text NOT NULL,
    status text NOT NULL,
    accepted boolean,
    response_json jsonb,
    updated_at timestamptz NOT NULL DEFAULT now(),
    last_error text,
    PRIMARY KEY (line_id, equipment_id)
);

CREATE INDEX IF NOT EXISTS idx_command_latest_response_updated_at
    ON command_latest_response (updated_at DESC);