using System;
using AgenticWorkforce.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AgenticWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:agent_role", "lead,specialist,reviewer,support")
                .Annotation("Npgsql:Enum:agent_visibility", "public,private,internal")
                .Annotation("Npgsql:Enum:artifact_type", "research_report,vulnerability_report,quality_audit,architecture_review,report,code,data")
                .Annotation("Npgsql:Enum:attempt_status", "passed,failed")
                .Annotation("Npgsql:Enum:change_type", "add,replace,remove,prune,archive")
                .Annotation("Npgsql:Enum:content_format", "markdown,pptx,docx,xlsx,pdf,code,json")
                .Annotation("Npgsql:Enum:decision_status", "active,superseded,reversed")
                .Annotation("Npgsql:Enum:document_type", "reference,policy,data,report,code,other")
                .Annotation("Npgsql:Enum:event_severity", "debug,info,warning,error")
                .Annotation("Npgsql:Enum:extraction_status", "pending,processing,completed,failed")
                .Annotation("Npgsql:Enum:failure_tier", "tier1structural,tier2quality,tier3integration,agent_error,timeout")
                .Annotation("Npgsql:Enum:human_decision_type", "approved,rejected,escalated,overridden")
                .Annotation("Npgsql:Enum:human_input_request_status", "pending,completed,timed_out,cancelled")
                .Annotation("Npgsql:Enum:intent_source", "user_chat,user_cli,director_inferred,system")
                .Annotation("Npgsql:Enum:learning_kind", "failure_pattern,success_pattern,anti_pattern,retry_strategy,capability_gap,domain_insight")
                .Annotation("Npgsql:Enum:learning_status", "active,retracted,superseded")
                .Annotation("Npgsql:Enum:message_role", "user,assistant,system,tool_call,tool_result")
                .Annotation("Npgsql:Enum:project_role", "owner,operator,reviewer,viewer")
                .Annotation("Npgsql:Enum:project_status", "active,paused,completed,archived")
                .Annotation("Npgsql:Enum:project_tier", "user,platform")
                .Annotation("Npgsql:Enum:session_status", "active,suspended,completed,expired,failed")
                .Annotation("Npgsql:Enum:system_role", "platform_admin,member")
                .Annotation("Npgsql:Enum:task_source", "workflow,planner,manual,ad_hoc,retry,system")
                .Annotation("Npgsql:Enum:task_status", "proposed,approved,queued,running,completed,failed,skipped,cancelled")
                .Annotation("Npgsql:Enum:task_type", "agent_task,human_decision,ai_decision,action,sub_workflow")
                .Annotation("Npgsql:Enum:workflow_run_status", "pending,running,awaiting_input,completed,failed,cancelled")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "agent_catalogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: false),
                    agent_type = table.Column<string>(type: "text", nullable: true),
                    agent_version = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    system_prompt = table.Column<string>(type: "text", nullable: true),
                    model_config = table.Column<string>(type: "jsonb", nullable: true),
                    tools = table.Column<string>(type: "jsonb", nullable: true),
                    scope = table.Column<string>(type: "jsonb", nullable: true),
                    @interface = table.Column<string>(name: "interface", type: "jsonb", nullable: true),
                    constraints = table.Column<string>(type: "jsonb", nullable: true),
                    keywords = table.Column<string[]>(type: "text[]", nullable: false),
                    thinking_budget = table.Column<string>(type: "jsonb", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    chat_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    visibility = table.Column<AgentVisibility>(type: "agent_visibility", nullable: false),
                    engine = table.Column<string>(type: "text", nullable: true),
                    max_input_length = table.Column<int>(type: "integer", nullable: true),
                    max_budget_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    produces_artifact = table.Column<bool>(type: "boolean", nullable: false),
                    artifact_type = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_catalogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_pricing",
                columns: table => new
                {
                    model = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    price_per_mtok_input = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    price_per_mtok_output = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    price_per_mtok_cache_read = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    price_per_mtok_cache_create = table.Column<decimal>(type: "numeric(12,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_pricing", x => new { x.model, x.effective_from });
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    objective = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    brief = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<ProjectStatus>(type: "project_status", nullable: false),
                    budget_ceiling_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    jurisdiction = table.Column<string>(type: "text", nullable: true),
                    template_name = table.Column<string>(type: "text", nullable: true),
                    tier = table.Column<ProjectTier>(type: "project_tier", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    changed_by = table.Column<string>(type: "text", nullable: true),
                    change_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    hashed_password = table.Column<string>(type: "text", nullable: true),
                    system_role = table.Column<SystemRole>(type: "system_role", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_service_account = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "context_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    version_snapshot = table.Column<int>(type: "integer", nullable: false),
                    context_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_context_milestones_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "milestone_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    workflow_run_ids = table.Column<string>(type: "jsonb", nullable: true),
                    key_outcomes = table.Column<string>(type: "jsonb", nullable: true),
                    domain_tags = table.Column<string[]>(type: "text[]", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_milestone_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_milestone_summaries_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_catalog_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<AgentRole>(type: "agent_role", nullable: false),
                    user_prompt = table.Column<string>(type: "text", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    custom_constraints = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_agents_agent_catalogs_agent_catalog_id",
                        column: x => x.agent_catalog_id,
                        principalTable: "agent_catalogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_agents_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_contexts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_data = table.Column<string>(type: "jsonb", nullable: false),
                    context_version = table.Column<int>(type: "integer", nullable: false),
                    size_characters = table.Column<int>(type: "integer", nullable: false),
                    size_tokens = table.Column<int>(type: "integer", nullable: false),
                    format_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_contexts", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_contexts_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    nodes = table.Column<string>(type: "jsonb", nullable: false),
                    edges = table.Column<string>(type: "jsonb", nullable: false),
                    canvas_state = table.Column<string>(type: "jsonb", nullable: true),
                    designed_by = table.Column<string>(type: "text", nullable: true),
                    designed_by_agent = table.Column<string>(type: "text", nullable: true),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    format_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_definitions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    key_prefix = table.Column<string>(type: "text", nullable: false),
                    hashed_key = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    scopes = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_url = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    extracted_text = table.Column<string>(type: "text", nullable: true),
                    extracted_text_url = table.Column<string>(type: "text", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    extraction_status = table.Column<ExtractionStatus>(type: "extraction_status", nullable: false),
                    extraction_error = table.Column<string>(type: "text", nullable: true),
                    document_type = table.Column<DocumentType>(type: "document_type", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string[]>(type: "text[]", nullable: false),
                    embeddings_generated = table.Column<bool>(type: "boolean", nullable: false),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_documents_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_documents_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<ProjectRole>(type: "project_role", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<SessionStatus>(type: "session_status", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_name = table.Column<string>(type: "text", nullable: true),
                    goal = table.Column<string>(type: "text", nullable: true),
                    rolling_summary = table.Column<string>(type: "text", nullable: true),
                    rolling_summary_anchor = table.Column<int>(type: "integer", nullable: true),
                    rolling_summary_version = table.Column<int>(type: "integer", nullable: false),
                    total_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    cost_budget_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "context_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_version = table.Column<int>(type: "integer", nullable: false),
                    change_type = table.Column<ChangeType>(type: "change_type", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    agent_name = table.Column<string>(type: "text", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_context_changes_project_contexts_context_id",
                        column: x => x.context_id,
                        principalTable: "project_contexts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_context_changes_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cron_expression = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_schedules_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_schedules_workflow_definitions_workflow_definition",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    section_title = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_chunks_project_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "project_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_document_chunks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent = table.Column<string>(type: "text", nullable: false),
                    intent_summary = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<IntentSource>(type: "intent_source", nullable: false),
                    revised_from_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_intents", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_intents_project_intents_revised_from_id",
                        column: x => x.revised_from_id,
                        principalTable: "project_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_project_intents_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_intents_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "session_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_type = table.Column<string>(type: "text", nullable: false),
                    channel_id = table.Column<string>(type: "text", nullable: false),
                    bound_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_channels_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<MessageRole>(type: "message_role", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    sender_id = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    thinking = table.Column<string>(type: "text", nullable: true),
                    tool_name = table.Column<string>(type: "text", nullable: true),
                    tool_call_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_messages_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_name = table.Column<string>(type: "text", nullable: false),
                    workflow_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<WorkflowRunStatus>(type: "workflow_run_status", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_type = table.Column<string>(type: "text", nullable: true),
                    triggered_by = table.Column<string>(type: "text", nullable: true),
                    context = table.Column<string>(type: "jsonb", nullable: true),
                    total_cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    budget_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: true),
                    error_data = table.Column<string>(type: "jsonb", nullable: true),
                    result_summary = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_runs_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_runs_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_workflow_runs_workflow_definitions_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<TaskType>(type: "task_type", nullable: false),
                    status = table.Column<TaskStatus>(type: "task_status", nullable: false),
                    objective = table.Column<string>(type: "text", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<TaskSource>(type: "task_source", nullable: false),
                    workflow_node_id = table.Column<string>(type: "text", nullable: true),
                    parent_task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inputs = table.Column<string>(type: "jsonb", nullable: true),
                    outputs = table.Column<string>(type: "jsonb", nullable: true),
                    output_summary = table.Column<string>(type: "text", nullable: true),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    max_retries = table.Column<int>(type: "integer", nullable: false),
                    assigned_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    format_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tasks_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tasks_tasks_parent_task_id",
                        column: x => x.parent_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tasks_users_assigned_to_id",
                        column: x => x.assigned_to_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tasks_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tasks_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "human_input_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prompt_message = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: true),
                    choices = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<HumanInputRequestStatus>(type: "human_input_request_status", nullable: false),
                    decision = table.Column<HumanDecisionType>(type: "human_decision_type", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    responder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    timeout_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_human_input_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_human_input_requests_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_human_input_requests_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_human_input_requests_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_human_input_requests_users_responder_id",
                        column: x => x.responder_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_human_input_requests_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "text", nullable: true),
                    artifact_type = table.Column<ArtifactType>(type: "artifact_type", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    content_format = table.Column<ContentFormat>(type: "content_format", nullable: false),
                    content_text = table.Column<string>(type: "text", nullable: true),
                    storage_url = table.Column<string>(type: "text", nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    format_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_artifacts_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_artifacts_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_ref = table.Column<string>(type: "text", nullable: false),
                    domain = table.Column<string>(type: "text", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    made_by = table.Column<string>(type: "text", nullable: false),
                    workflow_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<DecisionStatus>(type: "decision_status", nullable: false),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_decisions_project_decisions_superseded_by_id",
                        column: x => x.superseded_by_id,
                        principalTable: "project_decisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_project_decisions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_decisions_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_project_decisions_workflow_runs_workflow_run_id",
                        column: x => x.workflow_run_id,
                        principalTable: "workflow_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "project_learnings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<LearningKind>(type: "learning_kind", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    recommendation = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: true),
                    agent_names = table.Column<string[]>(type: "text[]", nullable: false),
                    domain_tags = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<LearningStatus>(type: "learning_status", nullable: false),
                    retracted_by = table.Column<string>(type: "text", nullable: true),
                    retracted_reason = table.Column<string>(type: "text", nullable: true),
                    superseded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contradicts_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform_promoted = table.Column<bool>(type: "boolean", nullable: false),
                    promoted_by = table.Column<string>(type: "text", nullable: true),
                    promoted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    format_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_learnings", x => x.id);
                    table.CheckConstraint("ck_project_learnings_confidence", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_project_learnings_project_learnings_contradicts_id",
                        column: x => x.contradicts_id,
                        principalTable: "project_learnings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_project_learnings_project_learnings_superseded_by_id",
                        column: x => x.superseded_by_id,
                        principalTable: "project_learnings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_project_learnings_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_learnings_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "task_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<AttemptStatus>(type: "attempt_status", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_tier = table.Column<FailureTier>(type: "failure_tier", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    feedback_provided = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_attempts_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_attempts_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_dependencies",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_task_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_dependencies", x => new { x.task_id, x.depends_on_task_id });
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_depends_on_task_id",
                        column: x => x.depends_on_task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_catalogs_agent_name",
                table: "agent_catalogs",
                column: "agent_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_prefix",
                table: "api_keys",
                column: "key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id_name",
                table: "api_keys",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_context_changes_context_id",
                table: "context_changes",
                column: "context_id");

            migrationBuilder.CreateIndex(
                name: "ix_context_changes_project_id",
                table: "context_changes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_context_milestones_project_id",
                table: "context_milestones",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_document_id",
                table: "document_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_project_id",
                table: "document_chunks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_project_id_decision",
                table: "human_input_requests",
                columns: new[] { "project_id", "decision" });

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_project_id_status",
                table: "human_input_requests",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_responder_id",
                table: "human_input_requests",
                column: "responder_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_session_id",
                table: "human_input_requests",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_task_id",
                table: "human_input_requests",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_input_requests_workflow_run_id",
                table: "human_input_requests",
                column: "workflow_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_milestone_summaries_project_id",
                table: "milestone_summaries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_agents_agent_catalog_id",
                table: "project_agents",
                column: "agent_catalog_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_agents_project_id_agent_catalog_id",
                table: "project_agents",
                columns: new[] { "project_id", "agent_catalog_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_artifacts_project_id",
                table: "project_artifacts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_artifacts_task_id",
                table: "project_artifacts",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_contexts_project_id",
                table: "project_contexts",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_decisions_project_id_decision_ref",
                table: "project_decisions",
                columns: new[] { "project_id", "decision_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_decisions_project_id_status",
                table: "project_decisions",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_decisions_superseded_by_id",
                table: "project_decisions",
                column: "superseded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_decisions_task_id",
                table: "project_decisions",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_decisions_workflow_run_id",
                table: "project_decisions",
                column: "workflow_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_project_id",
                table: "project_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_uploaded_by_id",
                table: "project_documents",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_intents_project_id",
                table: "project_intents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_intents_revised_from_id",
                table: "project_intents",
                column: "revised_from_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_intents_session_id",
                table: "project_intents",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_contradicts_id",
                table: "project_learnings",
                column: "contradicts_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_project_id_status",
                table: "project_learnings",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_superseded_by_id",
                table: "project_learnings",
                column: "superseded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_task_id",
                table: "project_learnings",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_project_id_user_id",
                table: "project_members",
                columns: new[] { "project_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                table: "project_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_name",
                table: "projects",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_status",
                table: "projects",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_versions_entity_type_entity_id_prompt_type_version",
                table: "prompt_versions",
                columns: new[] { "entity_type", "entity_id", "prompt_type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_channels_channel_type_channel_id",
                table: "session_channels",
                columns: new[] { "channel_type", "channel_id" });

            migrationBuilder.CreateIndex(
                name: "ix_session_channels_session_id",
                table: "session_channels",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_messages_session_id",
                table: "session_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_messages_session_id_created_at",
                table: "session_messages",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_project_id_status",
                table: "sessions",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id",
                table: "sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_attempts_project_id",
                table: "task_attempts",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_attempts_task_id_attempt_number",
                table: "task_attempts",
                columns: new[] { "task_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_depends_on_task_id",
                table: "task_dependencies",
                column: "depends_on_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assigned_to_id",
                table: "tasks",
                column: "assigned_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by_id",
                table: "tasks",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_parent_task_id",
                table: "tasks",
                column: "parent_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_created_at",
                table: "tasks",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_started_at",
                table: "tasks",
                columns: new[] { "project_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_status",
                table: "tasks",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_session_id",
                table: "tasks",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_workflow_run_id",
                table: "tasks",
                column: "workflow_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_name_version",
                table: "workflow_definitions",
                columns: new[] { "name", "version" },
                unique: true,
                filter: "project_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_project_id",
                table: "workflow_definitions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_project_id_name_version",
                table: "workflow_definitions",
                columns: new[] { "project_id", "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_project_id_status",
                table: "workflow_runs",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_session_id",
                table: "workflow_runs",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_workflow_definition_id",
                table: "workflow_runs",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_schedules_enabled_next_run_at",
                table: "workflow_schedules",
                columns: new[] { "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_schedules_project_id",
                table: "workflow_schedules",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_schedules_workflow_definition_id",
                table: "workflow_schedules",
                column: "workflow_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "context_changes");

            migrationBuilder.DropTable(
                name: "context_milestones");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "human_input_requests");

            migrationBuilder.DropTable(
                name: "milestone_summaries");

            migrationBuilder.DropTable(
                name: "model_pricing");

            migrationBuilder.DropTable(
                name: "project_agents");

            migrationBuilder.DropTable(
                name: "project_artifacts");

            migrationBuilder.DropTable(
                name: "project_decisions");

            migrationBuilder.DropTable(
                name: "project_intents");

            migrationBuilder.DropTable(
                name: "project_learnings");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "prompt_versions");

            migrationBuilder.DropTable(
                name: "session_channels");

            migrationBuilder.DropTable(
                name: "session_messages");

            migrationBuilder.DropTable(
                name: "task_attempts");

            migrationBuilder.DropTable(
                name: "task_dependencies");

            migrationBuilder.DropTable(
                name: "workflow_schedules");

            migrationBuilder.DropTable(
                name: "project_contexts");

            migrationBuilder.DropTable(
                name: "project_documents");

            migrationBuilder.DropTable(
                name: "agent_catalogs");

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropTable(
                name: "workflow_runs");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "projects");
        }
    }
}
