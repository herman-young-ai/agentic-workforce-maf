using System;
using AgenticWorkforce.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSodAndPromotionStateRetrofits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters: declare the new enum + columns first, backfill from
            // the legacy bool, THEN drop it. EF's scaffolder put the drop on top,
            // which would erase the promotion state for any pre-existing rows.

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
                .Annotation("Npgsql:Enum:project_role", "viewer,operator,reviewer,owner")
                .Annotation("Npgsql:Enum:project_status", "active,paused,completed,archived")
                .Annotation("Npgsql:Enum:project_tier", "user,platform")
                .Annotation("Npgsql:Enum:promotion_status", "none,pending_approval,approved,rejected")
                .Annotation("Npgsql:Enum:session_status", "active,suspended,completed,expired,failed")
                .Annotation("Npgsql:Enum:system_role", "platform_admin,member")
                .Annotation("Npgsql:Enum:task_source", "workflow,planner,manual,ad_hoc,retry,system")
                .Annotation("Npgsql:Enum:task_status", "proposed,approved,queued,running,completed,failed,skipped,cancelled")
                .Annotation("Npgsql:Enum:task_type", "agent_task,human_decision,ai_decision,action,sub_workflow")
                .Annotation("Npgsql:Enum:workflow_run_status", "pending,running,awaiting_input,completed,failed,cancelled")
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:agent_role", "lead,specialist,reviewer,support")
                .OldAnnotation("Npgsql:Enum:agent_visibility", "public,private,internal")
                .OldAnnotation("Npgsql:Enum:artifact_type", "research_report,vulnerability_report,quality_audit,architecture_review,report,code,data")
                .OldAnnotation("Npgsql:Enum:attempt_status", "passed,failed")
                .OldAnnotation("Npgsql:Enum:change_type", "add,replace,remove,prune,archive")
                .OldAnnotation("Npgsql:Enum:content_format", "markdown,pptx,docx,xlsx,pdf,code,json")
                .OldAnnotation("Npgsql:Enum:decision_status", "active,superseded,reversed")
                .OldAnnotation("Npgsql:Enum:document_type", "reference,policy,data,report,code,other")
                .OldAnnotation("Npgsql:Enum:event_severity", "debug,info,warning,error")
                .OldAnnotation("Npgsql:Enum:extraction_status", "pending,processing,completed,failed")
                .OldAnnotation("Npgsql:Enum:failure_tier", "tier1structural,tier2quality,tier3integration,agent_error,timeout")
                .OldAnnotation("Npgsql:Enum:human_decision_type", "approved,rejected,escalated,overridden")
                .OldAnnotation("Npgsql:Enum:human_input_request_status", "pending,completed,timed_out,cancelled")
                .OldAnnotation("Npgsql:Enum:intent_source", "user_chat,user_cli,director_inferred,system")
                .OldAnnotation("Npgsql:Enum:learning_kind", "failure_pattern,success_pattern,anti_pattern,retry_strategy,capability_gap,domain_insight")
                .OldAnnotation("Npgsql:Enum:learning_status", "active,retracted,superseded")
                .OldAnnotation("Npgsql:Enum:message_role", "user,assistant,system,tool_call,tool_result")
                .OldAnnotation("Npgsql:Enum:project_role", "owner,operator,reviewer,viewer")
                .OldAnnotation("Npgsql:Enum:project_status", "active,paused,completed,archived")
                .OldAnnotation("Npgsql:Enum:project_tier", "user,platform")
                .OldAnnotation("Npgsql:Enum:session_status", "active,suspended,completed,expired,failed")
                .OldAnnotation("Npgsql:Enum:system_role", "platform_admin,member")
                .OldAnnotation("Npgsql:Enum:task_source", "workflow,planner,manual,ad_hoc,retry,system")
                .OldAnnotation("Npgsql:Enum:task_status", "proposed,approved,queued,running,completed,failed,skipped,cancelled")
                .OldAnnotation("Npgsql:Enum:task_type", "agent_task,human_decision,ai_decision,action,sub_workflow")
                .OldAnnotation("Npgsql:Enum:workflow_run_status", "pending,running,awaiting_input,completed,failed,cancelled")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Guid>(
                name: "triggered_by_id",
                table: "workflow_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "promotion_rejected_reason",
                table: "project_learnings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "promotion_requested_at",
                table: "project_learnings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "promotion_requested_by_id",
                table: "project_learnings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<PromotionStatus>(
                name: "promotion_status",
                table: "project_learnings",
                type: "promotion_status",
                nullable: false,
                defaultValue: PromotionStatus.None);

            // Backfill: any learning previously promoted via the bool flag
            // becomes Approved under the new state machine. Run before dropping
            // the legacy column.
            migrationBuilder.Sql(
                "UPDATE project_learnings SET promotion_status = 'approved' WHERE platform_promoted = true;");

            migrationBuilder.DropColumn(
                name: "platform_promoted",
                table: "project_learnings");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_runs_triggered_by_id",
                table: "workflow_runs",
                column: "triggered_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_promotion_requested_by_id",
                table: "project_learnings",
                column: "promotion_requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_learnings_promotion_status_promotion_requested_at",
                table: "project_learnings",
                columns: new[] { "promotion_status", "promotion_requested_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_project_learnings_users_promotion_requested_by_id",
                table: "project_learnings",
                column: "promotion_requested_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_runs_users_triggered_by_id",
                table: "workflow_runs",
                column: "triggered_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_project_learnings_users_promotion_requested_by_id",
                table: "project_learnings");

            migrationBuilder.DropForeignKey(
                name: "fk_workflow_runs_users_triggered_by_id",
                table: "workflow_runs");

            migrationBuilder.DropIndex(
                name: "ix_workflow_runs_triggered_by_id",
                table: "workflow_runs");

            migrationBuilder.DropIndex(
                name: "ix_project_learnings_promotion_requested_by_id",
                table: "project_learnings");

            migrationBuilder.DropIndex(
                name: "ix_project_learnings_promotion_status_promotion_requested_at",
                table: "project_learnings");

            migrationBuilder.DropColumn(
                name: "triggered_by_id",
                table: "workflow_runs");

            migrationBuilder.DropColumn(
                name: "promotion_rejected_reason",
                table: "project_learnings");

            migrationBuilder.DropColumn(
                name: "promotion_requested_at",
                table: "project_learnings");

            migrationBuilder.DropColumn(
                name: "promotion_requested_by_id",
                table: "project_learnings");

            migrationBuilder.DropColumn(
                name: "promotion_status",
                table: "project_learnings");

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
                .Annotation("Npgsql:PostgresExtension:vector", ",,")
                .OldAnnotation("Npgsql:Enum:agent_role", "lead,specialist,reviewer,support")
                .OldAnnotation("Npgsql:Enum:agent_visibility", "public,private,internal")
                .OldAnnotation("Npgsql:Enum:artifact_type", "research_report,vulnerability_report,quality_audit,architecture_review,report,code,data")
                .OldAnnotation("Npgsql:Enum:attempt_status", "passed,failed")
                .OldAnnotation("Npgsql:Enum:change_type", "add,replace,remove,prune,archive")
                .OldAnnotation("Npgsql:Enum:content_format", "markdown,pptx,docx,xlsx,pdf,code,json")
                .OldAnnotation("Npgsql:Enum:decision_status", "active,superseded,reversed")
                .OldAnnotation("Npgsql:Enum:document_type", "reference,policy,data,report,code,other")
                .OldAnnotation("Npgsql:Enum:event_severity", "debug,info,warning,error")
                .OldAnnotation("Npgsql:Enum:extraction_status", "pending,processing,completed,failed")
                .OldAnnotation("Npgsql:Enum:failure_tier", "tier1structural,tier2quality,tier3integration,agent_error,timeout")
                .OldAnnotation("Npgsql:Enum:human_decision_type", "approved,rejected,escalated,overridden")
                .OldAnnotation("Npgsql:Enum:human_input_request_status", "pending,completed,timed_out,cancelled")
                .OldAnnotation("Npgsql:Enum:intent_source", "user_chat,user_cli,director_inferred,system")
                .OldAnnotation("Npgsql:Enum:learning_kind", "failure_pattern,success_pattern,anti_pattern,retry_strategy,capability_gap,domain_insight")
                .OldAnnotation("Npgsql:Enum:learning_status", "active,retracted,superseded")
                .OldAnnotation("Npgsql:Enum:message_role", "user,assistant,system,tool_call,tool_result")
                .OldAnnotation("Npgsql:Enum:project_role", "viewer,operator,reviewer,owner")
                .OldAnnotation("Npgsql:Enum:project_status", "active,paused,completed,archived")
                .OldAnnotation("Npgsql:Enum:project_tier", "user,platform")
                .OldAnnotation("Npgsql:Enum:promotion_status", "none,pending_approval,approved,rejected")
                .OldAnnotation("Npgsql:Enum:session_status", "active,suspended,completed,expired,failed")
                .OldAnnotation("Npgsql:Enum:system_role", "platform_admin,member")
                .OldAnnotation("Npgsql:Enum:task_source", "workflow,planner,manual,ad_hoc,retry,system")
                .OldAnnotation("Npgsql:Enum:task_status", "proposed,approved,queued,running,completed,failed,skipped,cancelled")
                .OldAnnotation("Npgsql:Enum:task_type", "agent_task,human_decision,ai_decision,action,sub_workflow")
                .OldAnnotation("Npgsql:Enum:workflow_run_status", "pending,running,awaiting_input,completed,failed,cancelled")
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<bool>(
                name: "platform_promoted",
                table: "project_learnings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
