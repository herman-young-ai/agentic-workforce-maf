using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticWorkforce.Infrastructure.Migrations
{
    /// <summary>
    /// Creates the two RANGE-partitioned tables that EF Core ignores
    /// (<c>project_events</c>, <c>llm_calls</c>) plus an initial set of monthly
    /// partitions starting at the current month. Production is expected to hand
    /// future partition creation to pg_partman.
    /// </summary>
    public partial class CreatePartitionedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE project_events (
                    id           UUID NOT NULL,
                    created_at   TIMESTAMPTZ NOT NULL,
                    project_id   UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                    task_id      UUID REFERENCES tasks(id)    ON DELETE SET NULL,
                    session_id   UUID REFERENCES sessions(id) ON DELETE SET NULL,
                    event_type   TEXT NOT NULL,
                    source       TEXT,
                    data         JSONB,
                    severity     event_severity NOT NULL DEFAULT 'info',
                    updated_at   TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (id, created_at)
                ) PARTITION BY RANGE (created_at);

                CREATE INDEX ix_project_events_project_created_at ON project_events (project_id, created_at);
                CREATE INDEX ix_project_events_task_created_at    ON project_events (task_id, created_at) WHERE task_id IS NOT NULL;
                CREATE INDEX ix_project_events_session_created_at ON project_events (session_id, created_at) WHERE session_id IS NOT NULL;
                CREATE INDEX ix_project_events_severity           ON project_events (severity, created_at);
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE llm_calls (
                    id                     UUID NOT NULL,
                    created_at             TIMESTAMPTZ NOT NULL,
                    session_id             UUID,
                    project_id             UUID REFERENCES projects(id) ON DELETE SET NULL,
                    task_id                UUID,
                    agent_name             TEXT,
                    agent_role             TEXT,
                    model                  TEXT NOT NULL,
                    provider               TEXT NOT NULL,
                    input_tokens           BIGINT NOT NULL DEFAULT 0,
                    output_tokens          BIGINT NOT NULL DEFAULT 0,
                    cache_read_tokens      BIGINT NOT NULL DEFAULT 0,
                    cache_creation_tokens  BIGINT NOT NULL DEFAULT 0,
                    cost_usd               NUMERIC(12,6) NOT NULL DEFAULT 0,
                    latency_ms             INT NOT NULL DEFAULT 0,
                    request_id             TEXT,
                    tool_count             INT NOT NULL DEFAULT 0,
                    updated_at             TIMESTAMPTZ NOT NULL,
                    PRIMARY KEY (id, created_at)
                ) PARTITION BY RANGE (created_at);

                CREATE INDEX ix_llm_calls_project_created_at ON llm_calls (project_id, created_at) WHERE project_id IS NOT NULL;
                CREATE INDEX ix_llm_calls_session_created_at ON llm_calls (session_id, created_at) WHERE session_id IS NOT NULL;
                CREATE INDEX ix_llm_calls_task_created_at    ON llm_calls (task_id, created_at)    WHERE task_id    IS NOT NULL;
                CREATE INDEX ix_llm_calls_model_created_at   ON llm_calls (model, created_at);
            ");

            // Seed three months of partitions starting at the current month so that
            // inserts succeed immediately on a freshly migrated database. pg_partman
            // is expected to manage future windows in production.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    months INT := 3;
                    i INT;
                    start_ts TIMESTAMPTZ;
                    end_ts   TIMESTAMPTZ;
                    suffix   TEXT;
                BEGIN
                    FOR i IN 0..(months - 1) LOOP
                        start_ts := date_trunc('month', now()) + (i * INTERVAL '1 month');
                        end_ts   := start_ts + INTERVAL '1 month';
                        suffix   := to_char(start_ts, 'YYYYMM');

                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS project_events_%s PARTITION OF project_events FOR VALUES FROM (%L) TO (%L);',
                            suffix, start_ts, end_ts);

                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS llm_calls_%s PARTITION OF llm_calls FOR VALUES FROM (%L) TO (%L);',
                            suffix, start_ts, end_ts);
                    END LOOP;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS llm_calls;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS project_events;");
        }
    }
}
