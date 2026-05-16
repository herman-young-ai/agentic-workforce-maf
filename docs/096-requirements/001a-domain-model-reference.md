# Domain Model Reference (SRS Appendix A)

Companion to: `029-mission-control-requirements.md` Section 3.1

---

## Base Mixins

### UUIDMixin

| Field | Type | Notes |
|-------|------|-------|
| `id` | UUID (postgresql UUID, as_uuid=False) | PK, default `uuid7()` |

### TimestampMixin

| Field | Type | Notes |
|-------|------|-------|
| `created_at` | DateTime(timezone=True) | Not null, default `utc_now` |
| `updated_at` | DateTime(timezone=True) | Not null, default `utc_now`, onupdate `utc_now` |

---

## Enumerations

| Enum Class | Location | Storage | Values |
|------------|----------|---------|--------|
| `MissionTier` | models/mission.py | DB Enum | `user`, `platform` |
| `MissionStatus` | models/mission.py | DB Enum | `active`, `paused`, `archived` |
| `MissionMemberRole` | models/mission.py | DB Enum | `owner`, `maintainer`, `viewer` |
| `ChangeType` | models/context.py | DB Enum | `add`, `replace`, `remove`, `prune`, `archive` |
| `IntentSource` | models/intent.py | String | `user_chat`, `user_cli`, `director_inferred`, `system` |
| `LearningKind` | models/learning.py | String | `failure_pattern`, `success_pattern`, `anti_pattern`, `retry_strategy`, `capability_gap` |
| `LearningStatus` | models/learning.py | String | `active`, `superseded`, `retracted` |
| `MissionPlanStatus` | models/plan.py | DB Enum | `active`, `paused`, `completed`, `archived` |
| `PlanTaskStatus` | models/plan.py | DB Enum | `proposed`, `approved`, `queued`, `running`, `completed`, `failed`, `skipped`, `cancelled` |
| `ArtifactType` | models/artifact.py | String | `research_report`, `vulnerability_report`, `quality_audit`, `architecture_review`, `report`, `code`, `data` |
| `ArtifactContentType` | models/artifact.py | String | `text`, `markdown`, `json`, `code` |
| `DecisionStatus` | models/decision.py | DB Enum | `active`, `superseded`, `reversed` |
| `SessionStatus` | models/session.py | DB Enum | `active`, `suspended`, `completed`, `expired`, `failed` |
| `ExecutionRecordStatus` | models/execution.py | DB Enum | `pending`, `running`, `completed`, `failed`, `cancelled`, `timed_out` |
| `TaskExecutionStatus` | models/execution.py | DB Enum | `completed`, `failed`, `skipped` |
| `TaskAttemptStatus` | models/execution.py | DB Enum | `passed`, `failed` |
| `DecisionType` | models/execution.py | DB Enum | `retry`, `fail`, `pass`, `re_plan`, `skip`, `escalate`, `post_run_supervisor` |
| `FailureTier` | models/execution.py | DB Enum | `tier_1_structural`, `tier_2_quality`, `tier_3_integration`, `agent_error`, `timeout` |
| `EnvironmentStatus` | models/environment.py | DB Enum | `creating`, `running`, `stopped`, `failed`, `destroyed` |
| `WorkflowRunState` | models/workflow.py | DB Enum | `pending`, `running`, `awaiting_input`, `completed`, `failed`, `cancelled` |
| `HumanInputRequestStatus` | models/workflow.py | DB Enum | `pending`, `completed`, `timed_out`, `cancelled` |
| `SystemRole` | models/user.py | String | `viewer`, `user`, `admin`, `sysadmin` |

---

## Entity Models

### 1. Mission

**Table:** `missions`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `name` | String(200) | No | — | Unique |
| `description` | Text | No | — | — |
| `brief` | Text | Yes | — | — |
| `status` | Enum(MissionStatus) | No | — | — |
| `owner_id` | String(200) | No | — | Indexed |
| `team_id` | String(200) | Yes | — | — |
| `default_roster` | String(100) | No | `"default"` | — |
| `budget_ceiling_usd` | Numeric(12,6) | Yes | — | — |
| `repo_url` | Text | Yes | — | — |
| `repo_root` | Text | Yes | — | — |
| `gate_mode` | String(20) | Yes | — | — |
| `tier` | Enum(MissionTier) | No | `user` | — |
| `execution_environment` | String(20) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `members` | MissionMember | all, delete-orphan | default |

---

### 2. MissionMember

**Table:** `mission_members`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`mission_id`, `user_id`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `user_id` | UUID | No | — | FK → `users.id` RESTRICT, Indexed |
| `role` | Enum(MissionMemberRole) | No | `VIEWER` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 3. MissionContext

**Table:** `mission_contexts`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Unique, Indexed |
| `context_data` | JSON | No | `{}` | — |
| `version` | Integer | No | `1` | — |
| `size_characters` | Integer | No | `0` | — |
| `size_tokens` | Integer | No | `0` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `changes` | ContextChange | all, delete-orphan | default |

---

### 4. ContextChange

**Table:** `context_changes`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `context_id` | UUID | No | — | FK → `mission_contexts.id` CASCADE, Indexed |
| `version` | Integer | No | — | — |
| `change_type` | Enum(ChangeType) | No | — | — |
| `path` | String(500) | No | — | — |
| `old_value` | JSON | Yes | — | — |
| `new_value` | JSON | Yes | — | — |
| `agent_id` | String(200) | Yes | — | — |
| `mission_id` | UUID | Yes | — | FK |
| `task_id` | String(100) | Yes | — | — |
| `execution_id` | UUID | Yes | — | Indexed |
| `reason` | Text | No | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 5. ContextMilestone

**Table:** `context_milestones`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `title` | String(200) | No | — | — |
| `description` | Text | No | `""` | — |
| `version_snapshot` | Integer | No | — | — |
| `context_snapshot` | JSON | No | — | — |
| `created_by` | String(200) | No | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 6. MissionIntent

**Table:** `mission_intents`
**Mixins:** UUIDMixin only

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `intent` | Text | No | — | — |
| `intent_summary` | Text | No | — | — |
| `scope` | JSON | No | `{}` | — |
| `source` | String(32) | No | — | — |
| `revised_from` | UUID | Yes | — | FK (self-referential) |
| `reason` | Text | No | — | — |
| `agent_id` | String(200) | Yes | — | — |
| `session_id` | UUID | Yes | — | FK → `sessions.id` SET NULL |
| `created_at` | DateTime(TZ) | No | — | — |

---

### 7. MissionLearning

**Table:** `mission_learnings`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `kind` | String(32) | No | — | — |
| `title` | String(200) | No | — | — |
| `body` | Text | No | — | — |
| `recommendation` | Text | No | — | — |
| `evidence` | JSON | No | `{}` | — |
| `domain_tags` | JSON | No | — | — |
| `agent_names` | JSON | No | — | — |
| `confidence` | Numeric(12,6) | No | `0.0` | — |
| `occurrence_count` | Integer | No | `1` | — |
| `platform_promoted` | Boolean | No | `False` | — |
| `promoted_at` | DateTime | Yes | — | — |
| `promoted_by` | String(200) | Yes | — | — |
| `contradicts_id` | UUID | Yes | — | FK (self-referential) |
| `status` | String(32) | No | `"active"` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 8. MissionEvent

**Table:** `mission_events`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `session_id` | UUID | Yes | — | Indexed |
| `event_type` | String(100) | No | — | Indexed |
| `timestamp` | DateTime(TZ) | No | — | — |
| `source` | String(200) | No | `""` | — |
| `data` | JSON | No | `{}` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 9. MissionArtifact

**Table:** `mission_artifacts`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `execution_record_id` | UUID | Yes | — | FK → `execution_records.id` SET NULL, Indexed |
| `task_execution_id` | UUID | Yes | — | FK → `task_executions.id` SET NULL, Indexed |
| `agent_id` | String(200) | No | `""` | — |
| `artifact_type` | String(50) | No | `"report"` | — |
| `title` | String(500) | No | — | — |
| `content_type` | String(20) | No | `"markdown"` | — |
| `language` | String(50) | Yes | — | — |
| `content_text` | Text | Yes | — | — |
| `storage_url` | String(1000) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 10. MissionPlan

**Table:** `mission_plans`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Unique, Indexed |
| `task_plan_json` | JSON | Yes | — | — |
| `gate_mode` | String(20) | Yes | — | — |
| `status` | Enum(MissionPlanStatus) | No | `ACTIVE` | — |
| `version` | Integer | No | `1` | — |
| `budget_usd_ceiling` | Numeric | Yes | — | — |
| `total_spent_usd` | Numeric | No | `0.0` | — |
| `veto_window_sec` | Integer | Yes | — | — |
| `supervisor_iteration_count` | Integer | No | `0` | — |
| `max_supervisor_iterations` | Integer | No | `20` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `task_states` | PlanTaskState | all, delete-orphan | selectin |

---

### 11. PlanTaskState

**Table:** `plan_task_states`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`mission_plan_id`, `task_id`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_plan_id` | UUID | No | — | FK → `mission_plans.id` CASCADE, Indexed |
| `task_id` | String(200) | No | — | — |
| `status` | Enum(PlanTaskStatus) | No | `PROPOSED` | — |
| `approved_by_user_id` | UUID | Yes | — | FK → `users.id` RESTRICT |
| `approved_at` | DateTime | Yes | — | — |
| `execution_record_id` | UUID | Yes | — | FK → `execution_records.id` SET NULL, Indexed |
| `task_execution_id` | UUID | Yes | — | FK → `task_executions.id` SET NULL |
| `last_attempt_at` | DateTime | Yes | — | — |
| `retry_count` | Integer | No | `0` | — |
| `veto_expires_at` | DateTime | Yes | — | — |
| `result_summary` | Text | Yes | — | — |
| `failure_reason` | Text | Yes | — | — |
| `cost_usd` | Numeric | No | `0.0` | — |
| `duration_seconds` | Numeric | No | `0.0` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 12. MissionTeamMember

**Table:** `mission_team_members`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`mission_id`, `agent_catalog_id`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `agent_catalog_id` | UUID | No | — | FK → `agent_catalog.id` CASCADE, Indexed |
| `user_prompt` | Text | Yes | — | — |
| `user_prompt_version` | Integer | No | `1` | — |
| `enabled` | Boolean | No | `True` | — |
| `display_order` | Integer | No | `0` | — |
| `custom_constraints` | JSON | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 13. MissionDecision

**Table:** `mission_decisions`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `decision_id` | String(50) | No | — | — |
| `domain` | String(100) | No | — | Indexed |
| `decision` | Text | No | — | — |
| `rationale` | Text | No | — | — |
| `made_by` | String(200) | No | — | — |
| `execution_id` | UUID | Yes | — | — |
| `status` | Enum(DecisionStatus) | No | `ACTIVE` | — |
| `superseded_by` | String(50) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 14. MilestoneSummary

**Table:** `milestone_summaries`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `title` | String(300) | No | — | — |
| `summary` | Text | No | — | — |
| `execution_ids` | JSON | No | — | — |
| `key_outcomes` | JSON | No | — | — |
| `domain_tags` | JSON | No | — | — |
| `period_start` | String(30) | Yes | — | — |
| `period_end` | String(30) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 15. Session

**Table:** `sessions`
**Mixins:** UUIDMixin + TimestampMixin
**Indexes:** (`user_id`, `status`), (`last_activity_at`), (`expires_at`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `status` | Enum(SessionStatus) | No | `ACTIVE` | — |
| `user_id` | UUID | Yes | — | Indexed |
| `mission_id` | UUID | Yes | — | Indexed |
| `agent_id` | String(100) | Yes | — | — |
| `goal` | String(2000) | Yes | — | — |
| `plan_id` | UUID | Yes | — | — |
| `session_metadata` | JSON | No | `{}` | — |
| `total_input_tokens` | Integer | No | `0` | — |
| `total_output_tokens` | Integer | No | `0` | — |
| `total_cost_usd` | Numeric | No | `0.0` | — |
| `cost_budget_usd` | Numeric | Yes | — | — |
| `rolling_summary` | Text | No | `""` | — |
| `rolling_summary_anchor` | Integer | No | `0` | — |
| `rolling_summary_version` | Integer | No | `0` | — |
| `last_activity_at` | DateTime(TZ) | No | `utc_now` | — |
| `expires_at` | DateTime | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `channels` | SessionChannel | — | selectin |
| `messages` | SessionMessage | — | noload |

---

### 16. SessionChannel

**Table:** `session_channels`
**Mixins:** UUIDMixin only
**Indexes:** (`session_id`, `is_active`), (`channel_type`, `channel_id`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `session_id` | UUID | No | — | FK, Indexed |
| `channel_type` | String(50) | No | — | — |
| `channel_id` | String(200) | No | — | — |
| `bound_at` | DateTime(TZ) | No | `utc_now` | — |
| `is_active` | Boolean | No | `True` | — |

---

### 17. SessionMessage

**Table:** `session_messages`
**Mixins:** UUIDMixin only
**Indexes:** (`session_id`, `created_at`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `session_id` | UUID | No | — | FK, Indexed |
| `role` | String(20) | No | — | — |
| `content` | Text | No | — | — |
| `sender_id` | String(100) | Yes | — | — |
| `model` | String(100) | Yes | — | — |
| `input_tokens` | Integer | Yes | — | — |
| `output_tokens` | Integer | Yes | — | — |
| `cost_usd` | Numeric | Yes | — | — |
| `thinking` | Text | Yes | — | — |
| `tool_name` | String(200) | Yes | — | — |
| `tool_call_id` | String(100) | Yes | — | — |
| `status` | String(20) | No | `"complete"` | — |
| `created_at` | DateTime(TZ) | No | — | — |
| `updated_at` | DateTime | Yes | — | — |

---

### 18. SessionFollowup

**Table:** `session_followups`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `session_id` | UUID | No | — | FK → `sessions.id` CASCADE, Indexed |
| `mission_id` | UUID | Yes | — | Indexed |
| `trigger_event_types` | JSON | No | — | — |
| `trigger_filter` | JSON | Yes | — | — |
| `prompt_template` | Text | No | — | — |
| `created_by_agent` | String(200) | No | — | — |
| `expires_at` | DateTime(TZ) | No | — | Indexed |
| `consumed_at` | DateTime | Yes | — | — |
| `cooldown_seconds` | Integer | No | `0` | — |
| `max_fires` | Integer | No | `1` | — |
| `fire_count` | Integer | No | `0` | — |
| `failure_reason` | Text | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 19. ExecutionRecord

**Table:** `execution_records`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `session_id` | UUID | Yes | — | FK → `sessions.id` SET NULL, Indexed |
| `mission_id` | UUID | Yes | — | FK → `missions.id` SET NULL, Indexed |
| `roster_name` | String(200) | No | — | Indexed |
| `objective_statement` | Text | Yes | — | — |
| `objective_category` | String(100) | No | — | Indexed |
| `status` | Enum(ExecutionRecordStatus) | No | — | Indexed |
| `task_plan_json` | JSON | Yes | — | — |
| `execution_outcome_json` | JSON | Yes | — | — |
| `planning_thinking_trace` | Text | Yes | — | — |
| `total_cost_usd` | Numeric | No | `0.0` | — |
| `started_at` | DateTime | Yes | — | — |
| `completed_at` | DateTime | Yes | — | — |
| `summarized` | Boolean | No | `False` | — |
| `parent_execution_record_id` | UUID | Yes | — | FK (self), Indexed |
| `completed_task_count` | Integer | No | `0` | — |
| `failed_task_count` | Integer | No | `0` | — |
| `task_type` | String(100) | No | — | Indexed |
| `task_params` | JSON | Yes | — | — |
| `temporal_workflow_id` | String(200) | Yes | — | — |
| `workflow_run_id` | UUID | Yes | — | FK → `workflow_runs.id` SET NULL, Indexed |
| `workflow_step_id` | String(100) | Yes | — | — |
| `execution_environment` | String(20) | Yes | — | — |
| `cost_ceiling_usd` | Numeric | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `task_executions` | TaskExecution | all, delete-orphan | default |
| `decisions` | ExecutionDecision | all, delete-orphan | default |

---

### 20. TaskExecution

**Table:** `task_executions`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `execution_record_id` | UUID | No | — | FK, Indexed |
| `task_id` | String(200) | No | — | — |
| `agent_name` | String(200) | No | — | — |
| `status` | Enum(TaskExecutionStatus) | No | — | Indexed |
| `output_data` | JSON | No | — | — |
| `token_usage` | JSON | No | — | — |
| `verification_outcome` | JSON | No | — | — |
| `domain_tags` | JSON | No | — | — |
| `cost_usd` | Numeric | No | `0.0` | — |
| `duration_seconds` | Numeric | Yes | — | — |
| `started_at` | DateTime | Yes | — | — |
| `completed_at` | DateTime | Yes | — | — |
| `execution_id` | UUID | No | — | Indexed |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `attempts` | TaskAttempt | all, delete-orphan | default |

---

### 21. TaskAttempt

**Table:** `task_attempts`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `task_execution_id` | UUID | No | — | FK, Indexed |
| `attempt_number` | Integer | No | — | — |
| `status` | Enum(TaskAttemptStatus) | No | — | — |
| `failure_tier` | Enum(FailureTier) | Yes | — | — |
| `failure_reason` | Text | Yes | — | — |
| `feedback_provided` | Text | Yes | — | — |
| `input_tokens` | Integer | No | `0` | — |
| `output_tokens` | Integer | No | `0` | — |
| `cost_usd` | Numeric | No | `0.0` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 22. ExecutionDecision

**Table:** `execution_decisions`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `execution_record_id` | UUID | No | — | FK, Indexed |
| `decision_type` | Enum(DecisionType) | No | — | Indexed |
| `task_id` | String(200) | Yes | — | — |
| `reasoning` | Text | No | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 23. ExecutionEnvironmentModel

**Table:** `execution_environments`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `mode` | String(20) | No | — | — |
| `status` | Enum(EnvironmentStatus) | No | `CREATING` | — |
| `container_id` | String | Yes | — | — |
| `container_name` | String | Yes | — | — |
| `branch_name` | String | Yes | — | — |
| `image` | String | Yes | — | — |
| `working_dir` | Text | Yes | — | — |
| `execution_count` | Integer | No | `0` | — |
| `last_execution_at` | DateTime | Yes | — | — |
| `config` | JSON | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 24. WorkflowDefinition

**Table:** `workflow_definitions`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`mission_id`, `name`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `mission_id` | UUID | Yes | — | FK → `missions.id` CASCADE, Indexed |
| `name` | String(200) | No | — | — |
| `description` | Text | No | `""` | — |
| `version` | Integer | No | `1` | — |
| `enabled` | Boolean | No | `True` | — |
| `objective` | JSON | No | — | — |
| `steps` | JSON | No | — | — |
| `edges_json` | JSON | No | — | — |
| `canvas_state` | JSON | No | — | — |
| `trigger_config` | JSON | No | — | — |
| `budget_config` | JSON | No | — | — |
| `context` | JSON | No | — | — |
| `designed_by` | String(200) | Yes | — | — |
| `locked_at` | DateTime | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 25. WorkflowRun

**Table:** `workflow_runs`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `workflow_name` | String(200) | No | — | Indexed |
| `workflow_version` | Integer | No | — | — |
| `mission_id` | UUID | Yes | — | FK → `missions.id` SET NULL, Indexed |
| `status` | Enum(WorkflowRunState) | No | `PENDING` | Indexed |
| `session_id` | UUID | Yes | — | FK → `sessions.id` SET NULL, Indexed |
| `trigger_type` | String(50) | No | `"on_demand"` | — |
| `triggered_by` | String(200) | No | — | — |
| `context` | JSON | No | `{}` | — |
| `total_cost_usd` | Numeric | No | `0.0` | — |
| `budget_usd` | Numeric | Yes | — | — |
| `started_at` | DateTime | Yes | — | — |
| `completed_at` | DateTime | Yes | — | — |
| `error_data` | JSON | Yes | — | — |
| `result_summary` | Text | Yes | — | — |
| `execution_record_id` | UUID | Yes | — | FK → `execution_records.id` SET NULL, Indexed |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 26. WorkflowSchedule

**Table:** `workflow_schedules`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`workflow_definition_id`, `mission_id`, `cron_expression`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `workflow_definition_id` | UUID | No | — | FK → `workflow_definitions.id` CASCADE, Indexed |
| `mission_id` | UUID | No | — | FK → `missions.id` CASCADE, Indexed |
| `cron_expression` | String | No | — | — |
| `enabled` | Boolean | No | `True` | — |
| `next_run_at` | DateTime | Yes | — | — |
| `last_run_at` | DateTime | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 27. AgentCatalog

**Table:** `agent_catalog`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `agent_name` | String(200) | No | — | Unique, Indexed |
| `agent_type` | String(50) | No | — | Indexed |
| `version` | String | No | — | — |
| `description` | Text | No | — | — |
| `system_prompt` | Text | No | — | — |
| `model_config_json` | JSON | No | — | — |
| `tools` | JSON | No | — | — |
| `scope` | JSON | No | — | — |
| `interface` | JSON | No | — | — |
| `constraints` | JSON | No | — | — |
| `keywords` | JSON | No | — | — |
| `thinking_budget_json` | JSON | No | — | — |
| `enabled` | Boolean | No | `True` | Indexed |
| `chat_enabled` | Boolean | No | `False` | — |
| `visibility` | String(20) | No | `"public"` | Indexed |
| `engine` | String(20) | No | `"pydantic_ai"` | — |
| `effort` | String(10) | Yes | — | — |
| `invocation_tier` | String(20) | No | `"user"` | — |
| `invocation_required_role` | String(20) | Yes | — | — |
| `execution_mode` | String(20) | No | `"standard"` | — |
| `max_input_length` | Integer | No | `32000` | — |
| `max_budget_usd` | Numeric | No | `1.0` | — |
| `cache_ttl` | String(10) | Yes | — | — |
| `produces_artifact` | Boolean | No | `False` | — |
| `artifact_type` | String(50) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 28. PromptVersion

**Table:** `prompt_versions`
**Mixins:** UUIDMixin + TimestampMixin
**Indexes:** (`entity_type`, `entity_id`, `version`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `entity_type` | String(50) | No | — | — |
| `entity_id` | UUID | No | — | — |
| `prompt_type` | String(20) | No | — | — |
| `content` | Text | No | — | — |
| `version` | Integer | No | — | — |
| `changed_by` | String(200) | No | — | — |
| `change_reason` | Text | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 29. User

**Table:** `users`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `email` | String(200) | No | — | Unique, Indexed |
| `display_name` | String(200) | No | — | — |
| `hashed_password` | String(200) | Yes | — | — |
| `system_role` | String(20) | No | `"user"` | Indexed |
| `is_active` | Boolean | No | `True` | — |
| `is_service_account` | Boolean | No | `False` | — |
| `last_login_at` | DateTime | Yes | — | — |
| `current_mission_id` | UUID | Yes | — | FK → `missions.id` SET NULL, Indexed |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

**Relationships:**

| Name | Target | Cascade | Lazy |
|------|--------|---------|------|
| `api_keys` | ApiKey | all, delete-orphan | default |
| `current_mission` | Mission | — | default |

---

### 30. ApiKey

**Table:** `api_keys`
**Mixins:** UUIDMixin + TimestampMixin
**Table Constraints:** UniqueConstraint(`user_id`, `name`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `user_id` | UUID | No | — | FK → `users.id` CASCADE, Indexed |
| `name` | String(100) | No | — | — |
| `key_prefix` | String(8) | No | — | Indexed |
| `hashed_key` | String(200) | No | — | — |
| `expires_at` | DateTime | Yes | — | — |
| `revoked_at` | DateTime | Yes | — | — |
| `last_used_at` | DateTime | Yes | — | — |
| `scopes` | JSON | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 31. LlmCall

**Table:** `llm_calls`
**Mixins:** UUIDMixin + TimestampMixin
**Extra Indexes:** (`created_at`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `session_id` | UUID | Yes | — | Indexed |
| `mission_id` | UUID | Yes | — | Indexed |
| `execution_record_id` | UUID | Yes | — | — |
| `agent_name` | String(200) | No | — | Indexed |
| `agent_role` | String(50) | No | — | — |
| `model` | String(100) | No | — | Indexed |
| `provider` | String(50) | No | — | — |
| `input_tokens` | Integer | No | `0` | — |
| `output_tokens` | Integer | No | `0` | — |
| `cache_read_tokens` | Integer | No | `0` | — |
| `cache_creation_tokens` | Integer | No | `0` | — |
| `cost_usd` | Numeric | No | `0.0` | — |
| `latency_ms` | Integer | Yes | — | — |
| `request_id` | String(100) | Yes | — | — |
| `tool_count` | Integer | No | `0` | — |
| `tool_schema_tokens` | Integer | No | `0` | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

### 32. ModelPricing

**Table:** `model_pricing`
**Mixins:** None
**Primary Key:** Composite (`model`, `effective_from`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `model` | String(100) | No | — | PK (composite) |
| `effective_from` | DateTime(TZ) | No | — | PK (composite) |
| `effective_to` | DateTime(TZ) | Yes | — | — |
| `price_per_mtok_input` | Numeric | No | — | — |
| `price_per_mtok_output` | Numeric | No | — | — |
| `price_per_mtok_cache_read` | Numeric | Yes | — | — |
| `price_per_mtok_cache_creation` | Numeric | Yes | — | — |

---

### 33. Embedding

**Table:** `embeddings`
**Mixins:** UUIDMixin only
**Table Constraints:** UniqueConstraint(`source_table`, `source_id`, `version`)

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `source_table` | String(64) | No | — | Indexed |
| `source_id` | UUID | No | — | — |
| `mission_id` | UUID | Yes | — | FK → `missions.id` CASCADE, Indexed |
| `embedding_data` | String | No | — | (pgvector in DB) |
| `model` | String(64) | No | — | — |
| `version` | Integer | No | — | — |
| `content_hash` | String(64) | Yes | — | — |
| `created_at` | DateTime(TZ) | No | — | — |

---

### 34. HumanInputRequest

**Table:** `human_input_requests`
**Mixins:** UUIDMixin + TimestampMixin

| Column | Type | Nullable | Default | Constraints/Indexes |
|--------|------|----------|---------|---------------------|
| `id` | UUID | No | `uuid7()` | PK |
| `workflow_run_id` | UUID | No | — | FK → `workflow_runs.id` CASCADE, Indexed |
| `step_id` | String(100) | No | — | — |
| `mission_id` | UUID | Yes | — | FK → `missions.id` SET NULL, Indexed |
| `session_id` | UUID | Yes | — | FK → `sessions.id` SET NULL |
| `temporal_workflow_id` | String(200) | No | — | — |
| `prompt_message` | Text | No | — | — |
| `channel` | String(50) | No | `"telegram"` | — |
| `choices_json` | JSON | Yes | — | — |
| `status` | Enum(HumanInputRequestStatus) | No | `PENDING` | Indexed |
| `response` | Text | Yes | — | — |
| `responder_id` | String(200) | Yes | — | — |
| `timeout_at` | DateTime | Yes | — | — |
| `resolved_at` | DateTime | Yes | — | — |
| `created_at` | DateTime(TZ) | No | `utc_now` | — |
| `updated_at` | DateTime(TZ) | No | `utc_now` | — |

---

## SRS Accuracy Notes

1. **Embedding.embedding_data** is declared as `String` in the ORM but maps to the `pgvector` vector type in the actual database via Alembic migration. Application code must handle serialization between Python lists/arrays and the pgvector wire format.

2. **MissionPlan.status** and **PlanTaskState.status** use `server_default` with `.name` (uppercase enum member name, e.g. `"ACTIVE"`) but the Python enum `.value` is lowercase (e.g. `"active"`). Verify the deployed schema to confirm which convention is stored — mismatches cause lookup failures on reads.

3. **Several UUID columns lack FK constraints** — `Session.user_id`, `Session.mission_id`, `LlmCall.session_id`, `LlmCall.mission_id`, `LlmCall.execution_record_id`, and `MissionEvent.session_id` have indexes but no foreign key relationships defined in the ORM. Referential integrity for these columns is enforced at the application level only.

4. **MissionLearning.promoted_at** has a `Mapped[str | None]` type annotation in the ORM class but uses a `DateTime` column type. Treat this field as a timezone-aware datetime at the database and service layer; the annotation is a typing inconsistency that does not affect runtime behavior.
