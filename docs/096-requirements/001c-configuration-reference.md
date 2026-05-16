# Configuration Reference (SRS Appendix C)

Companion to: `029-mission-control-requirements.md` Section 9.0

---

## Environment Variables (Secrets)

Source: `modules/backend/core/config.py` — `Settings` (Pydantic BaseSettings), loaded from `config/.env`.

| Variable | Purpose | Required | Default | Sensitive |
|----------|---------|----------|---------|-----------|
| DB_PASSWORD | PostgreSQL password | Yes | — | Yes |
| REDIS_PASSWORD | Redis password | Yes | — | Yes |
| JWT_SECRET | JWT signing secret | Yes | — | Yes |
| API_KEY_SALT | API key hashing salt | Yes | — | Yes |
| TELEGRAM_BOT_TOKEN | Telegram Bot API token | Yes | — | Yes |
| TELEGRAM_WEBHOOK_SECRET | Telegram webhook verification | Yes | — | Yes |
| ANTHROPIC_API_KEY | Primary Anthropic API key | Yes | — | Yes |
| ADMIN_API_KEY | Legacy admin authentication key | No | "" | Yes (when set) |
| PERPLEXITY_API_KEY | Perplexity search API | No | "" | Yes (when set) |
| TAVILY_API_KEY | Tavily search API | No | "" | Yes (when set) |
| BRAVE_API_KEY | Brave Search API | No | "" | Yes (when set) |

### Additional Env Vars (outside Settings)

| Variable | Source | Purpose |
|----------|--------|---------|
| ANTHROPIC_API_KEY, OPENAI_API_KEY | providers.yaml env_var field | Provider credential resolution |
| MC_API_URL | CLI api_client.py | Override CLI API base URL |
| MC_LOG_LEVEL | core/logging.py | Console log level override |
| MC_LOG_FORMAT | core/logging.py | Console log format override |

---

## YAML Configuration Files

All files under `config/settings/`. Loaded by `AppConfig` in `core/config.py`.

### application.yaml

| Path | Value | Controls |
|------|-------|----------|
| name | Mission Control | Application display name |
| version | 0.1.0 | Semver |
| description | AI-first autonomous agent platform | Short description |
| environment | development | Runtime label |
| debug | true | Debug mode toggle |
| api_prefix | /api | HTTP API prefix |
| docs_enabled | true | OpenAPI docs exposure |
| server.host | 0.0.0.0 | Bind host |
| server.port | 8100 | Bind port |
| cors.origins | [http://localhost:5173, :5174, :3000, :3100] | Allowed CORS origins |
| pagination.default_limit | 50 | Default page size |
| pagination.max_limit | 100 | Max page size |
| timeouts.database | 10 | DB timeout (seconds) |
| timeouts.external_api | 30 | Outbound HTTP timeout (seconds) |
| timeouts.background | 120 | Background task timeout (seconds) |
| telegram.webhook_path | /webhook/telegram | Webhook route |
| telegram.authorized_users | [] | Allowed Telegram user IDs (empty = deny all) |
| telegram.max_message_length | 4096 | Max message length |
| cli.console_width | 160 | Rich output width |
| cli.plan_poll_interval_seconds | 3.0 | Plan status polling interval |
| cli.plan_poll_max_iterations | 300 | Max poll iterations |

### database.yaml

| Path | Value | Controls |
|------|-------|----------|
| host | localhost | PostgreSQL host |
| port | 5432 | PostgreSQL port |
| name | mission_control | Database name |
| user | mission_control | Database user |
| pool_size | 10 | Connection pool size |
| max_overflow | 20 | Extra connections beyond pool |
| pool_timeout | 30 | Pool wait timeout (seconds) |
| pool_recycle | 1800 | Connection recycle interval (seconds) |
| echo | false | Echo SQL statements |
| echo_pool | false | Echo pool events |
| redis.host | localhost | Redis host |
| redis.port | 6379 | Redis port |
| redis.db | 0 | Redis logical DB |
| redis.broker.queue_name | mc_tasks | TaskIQ queue name |
| redis.broker.result_expiry_seconds | 3600 | Task result TTL |

### logging.yaml

| Path | Value | Controls |
|------|-------|----------|
| level | INFO | Global log level |
| format | json | Log format |
| handlers.console.enabled | true | Console handler |
| handlers.file.enabled | true | JSONL file handler |
| handlers.file.path | var/logs/system.jsonl | Log file path |
| handlers.file.rotation_when | midnight | Rotation schedule |
| handlers.file.rotation_interval | 1 | Rotation interval |
| handlers.file.backup_count | 14 | Rotated files to keep |

### security.yaml

| Path | Value | Controls |
|------|-------|----------|
| jwt.algorithm | HS256 | JWT algorithm |
| jwt.access_token_expire_minutes | 30 | Access token TTL |
| jwt.refresh_token_expire_days | 7 | Refresh token TTL |
| jwt.audience | mission-control-api | JWT audience |
| rate_limiting.api.requests_per_minute | 60 | API rate limit |
| rate_limiting.api.requests_per_hour | 1000 | API rate limit |
| rate_limiting.telegram.messages_per_minute | 30 | Telegram rate limit |
| rate_limiting.telegram.messages_per_hour | 500 | Telegram rate limit |
| rate_limiting.websocket.messages_per_minute | 60 | WebSocket rate limit |
| rate_limiting.websocket.messages_per_hour | 1000 | WebSocket rate limit |
| request_limits.max_body_size_bytes | 1048576 | Max request body |
| request_limits.max_header_size_bytes | 8192 | Max header size |
| headers.x_content_type_options | nosniff | Security header |
| headers.x_frame_options | DENY | Clickjacking protection |
| headers.referrer_policy | strict-origin-when-cross-origin | Referrer policy |
| headers.hsts_enabled | false | HSTS toggle |
| headers.hsts_max_age | 31536000 | HSTS max-age |
| secrets_validation.jwt_secret_min_length | 32 | Min JWT secret length |
| secrets_validation.api_key_salt_min_length | 16 | Min API key salt length |
| secrets_validation.webhook_secret_min_length | 16 | Min webhook secret length |
| roles.viewer.level | 1 | Role hierarchy level |
| roles.user.level | 2 | Role hierarchy level |
| roles.admin.level | 3 | Role hierarchy level |
| roles.sysadmin.level | 4 | Highest privilege level |
| user_roles | {} | External user ID to role mapping |
| cors.enforce_in_production | true | Production CORS strictness |
| cors.allow_methods | [GET, POST, PATCH, DELETE, OPTIONS] | Allowed methods |
| cors.allow_headers | [Authorization, Content-Type, X-Request-ID, X-Frontend-ID, X-API-Key] | Allowed headers |

### sessions.yaml

| Path | Value | Controls |
|------|-------|----------|
| default_ttl_hours | 24 | Default session TTL |
| max_ttl_hours | 168 | Maximum session TTL |
| default_cost_budget_usd | 50.00 | Default per-session budget |
| max_cost_budget_usd | 500.00 | Max session budget |
| cleanup_interval_minutes | 60 | Cleanup cadence |
| budget_warning_threshold | 0.80 | Warn at 80% of budget |

### temporal.yaml

| Path | Value | Controls |
|------|-------|----------|
| enabled | true | Temporal integration toggle |
| server_url | 127.0.0.1:7233 | Temporal frontend address |
| namespace | default | Temporal namespace |
| task_queue | agent-missions | Worker task queue |
| workflow_execution_timeout_days | 30 | Workflow timeout horizon |
| activity_start_to_close_seconds | 600 | Default activity window |
| activity_retry_max_attempts | 3 | Activity retries |
| approval_timeout_seconds | 14400 | Human approval timeout (4h) |
| escalation_timeout_seconds | 86400 | Escalation timeout (24h) |
| notification_timeout_seconds | 30 | Notification delivery timeout |
| budget_timeout_multiplier_seconds | 120 | Seconds per dollar for timeout tuning |
| min_activity_timeout_seconds | 600 | Activity timeout floor |
| persistence_timeout_seconds | 30 | Persist activity deadline |
| persistence_retry_max_attempts | 3 | Persist retries |
| execution_retry_max_attempts | 2 | execute_mission retries |
| execution_retry_initial_interval_seconds | 5 | Backoff initial |
| execution_retry_max_interval_seconds | 60 | Backoff cap |
| dispatch_check_interval_seconds | 1 | Scheduler check cadence |
| wait_action_sleep_seconds | 1 | WAIT action poll sleep |
| refine_poll_interval_seconds | 1 | Refine poll interval |
| refine_poll_max_seconds | 60 | Refine max wait |
| max_supervisor_iterations | 25 | Supervisor loop cap |

### missions.yaml

| Path | Value | Controls |
|------|-------|----------|
| default_budget_ceiling_usd | null | Default ceiling |
| max_missions_per_owner | 50 | Ownership cap |
| pcd_max_size_bytes | 20480 | MCD max size |
| pcd_target_size_bytes | 15360 | MCD target size |
| pcd_prune_threshold_pct | 80 | Prune threshold |
| pcd_alert_threshold_pct | 90 | Alert threshold |
| history_summarize_after_days | 30 | Summarize stale history |
| enable_context_assembly | true | Context assembly toggle |
| knowledge.enabled | true | Knowledge platform |
| knowledge.embedding.provider | openai | Embedding provider |
| knowledge.embedding.model | text-embedding-3-small | Embedding model |
| knowledge.embedding.dimensions | 1536 | Vector dimensions |
| knowledge.embedding.version | 1 | Embedding schema version |
| knowledge.embedding.backfill_batch_size | 50 | Backfill batch |
| knowledge.embedding.default_search_mode | hybrid | Search mode |
| knowledge.embedding.fail_open | true | Structured fallback on outage |
| knowledge.learnings.min_occurrence_for_extraction | 2 | Extraction threshold |
| knowledge.learnings.promotion_confidence_threshold | 0.7 | Promotion cutoff |
| knowledge.learnings.promotion_min_missions | 2 | Cross-mission promotion min |
| knowledge.learnings.context_assembler_reserve_tokens | 1500 | Token reservation |
| knowledge.learnings.context_assembler_max_entries | 5 | Max assembled learnings |
| knowledge.learnings.dedup_similarity_threshold | 0.92 | Dedup cosine threshold |
| knowledge.session_memory.compression_threshold_messages | 50 | Compression trigger |
| knowledge.session_memory.summary_max_tokens | 8000 | Summary token cap |
| knowledge.intent.auto_capture | true | Auto intent capture |
| knowledge.intent.summary_max_chars | 160 | Intent summary length |
| knowledge.officer.schedule_cron | 0 4 * * * | Knowledge Officer cron |
| knowledge.officer.budget_usd_per_run | 0.10 | Officer budget |
| knowledge.officer.model | haiku | Officer model |

### gate.yaml

| Path | Value | Controls |
|------|-------|----------|
| mode | off | Gate mode: off/interactive/ai_assisted/autonomous |
| ai.model | copilot:claude-haiku-4.5 | AI reviewer model |
| ai.temperature | 0.0 | AI reviewer temperature |
| ai.max_tokens | 1024 | AI reviewer max tokens |
| ai.budget_per_review_usd | 0.01 | Per-review budget |
| points.pre_dispatch.enabled | true | Pre-dispatch checkpoint |
| points.pre_layer.enabled | true | Pre-layer checkpoint |
| points.post_task.enabled | true | Post-task checkpoint |
| points.verification_failed.enabled | true | Verification failure checkpoint |
| points.post_layer.enabled | true | Post-layer checkpoint |
| points.post_run.enabled | true | Post-run checkpoint |
| auto_rules.cost_threshold_pct | 80 | Skip gate when below budget % |
| auto_rules.max_tasks_per_layer | 10 | Auto-continue task ceiling |
| auto_rules.skip_post_task_on_pass | true | Skip post-task on pass |

### workflows.yaml

| Path | Value | Controls |
|------|-------|----------|
| workflows_dir | config/workflow_templates | Directory to scan for workflow YAML files |
| max_steps_per_workflow | 20 | Maximum steps in a single workflow |
| max_context_size_bytes | 1048576 | Maximum mission context size (bytes) |
| default_step_timeout_seconds | 600 | Default timeout per step |
| default_budget_usd | 10.00 | Default workflow budget |
| max_budget_usd | 100.00 | Hard cap on workflow budget |
| max_concurrent_missions | 10 | Maximum simultaneous workflow runs |
| enable_workflow_matching | true | Workflow matching fast-path |
| max_scheduled_runs_per_day | 50 | Platform-wide scheduled run safety cap |
| schedule_evaluation_interval_seconds | 60 | Scheduler check interval |
| workshop.max_cached_input_sets | 10 | Workshop cached input sets |
| workshop.default_budget_usd | 2.00 | Workshop default budget |
| workshop.max_budget_usd | 20.00 | Workshop max budget |
| workshop.cache_stale_days | 7 | Workshop cache staleness threshold |

### notifications.yaml

| Path | Value | Controls |
|------|-------|----------|
| enabled | true | Master toggle for run notifications |
| budget_warning_threshold_pct | 80.0 | Budget usage % that triggers a warning |
| notify_on_completion | true | Notify when a workflow run completes |
| notify_on_failure | true | Notify when a workflow run fails |
| notify_channels | [telegram] | Channels to send notifications to |

### cost.yaml

| Path | Value | Controls |
|------|-------|----------|
| default_cache_ttl | 1h | Default prompt cache TTL for Anthropic agents |
| alerts.hourly_spend_limit_usd | 5.00 | Hourly spend alert threshold |
| alerts.per_agent_hourly_limit_usd | 2.00 | Per-agent hourly spend limit |
| alerts.min_cache_hit_rate | 0.40 | Minimum acceptable cache hit rate |
| emergency_downgrade_model | copilot:claude-haiku-4.5 | Emergency cost-reduction model |
| output_efficiency.high_threshold | 3.0 | Output/input ratio warning (>3x) |
| output_efficiency.critical_threshold | 5.0 | Output/input ratio critical (>5x) |

### providers.yaml

| Path | Value | Controls |
|------|-------|----------|
| anthropic.auth_mode | api_key | Anthropic authentication mode |
| anthropic.env_var | ANTHROPIC_API_KEY | Anthropic credential env var |
| openai.auth_mode | api_key | OpenAI authentication mode |
| openai.env_var | OPENAI_API_KEY | OpenAI credential env var |

### events.yaml

| Path | Value | Controls |
|------|-------|----------|
| transport | redis | Event transport backend |
| streams.default.maxlen | 10000 | Max stream length |
| streams.default.consumer_group | mc-workers | Consumer group name |
| consumer_timeout_ms | 5000 | Consumer poll timeout (ms) |
| dlq_enabled | true | Dead letter queue toggle |
| dlq_prefix | dlq | DLQ key prefix |
| exclude_event_types | [agent.response.chunk, session.cost.update] | High-volume types excluded from DB persistence |

### gateway.yaml

| Path | Value | Controls |
|------|-------|----------|
| default_policy | deny | Gateway access policy (allow_all/allowlist/deny) |
| channels.telegram.allowlist | [] | Telegram user ID allowlist |

### executions.yaml

| Path | Value | Controls |
|------|-------|----------|
| max_thinking_trace_length | 50000 | Max planning thinking trace characters |
| max_task_output_size_bytes | 1048576 | Max task output_data JSONB size |
| retention_days | 0 | Execution record retention (0 = forever) |
| default_page_size | 20 | Default pagination size |
| max_page_size | 100 | Maximum pagination size |
| persist_thinking_trace | true | Store planning agent thinking trace |
| persist_verification_details | true | Store full verification outcome JSON |

### research.yaml

| Path | Value | Controls |
|------|-------|----------|
| presets.quick.max_iterations | 2 | Quick preset iteration cap |
| presets.quick.max_sub_questions | 5 | Quick preset sub-question limit |
| presets.quick.max_sources_per_question | 5 | Quick preset sources per question |
| presets.quick.max_findings_for_synthesis | 25 | Quick preset findings cap |
| presets.quick.budget_usd | 5.00 | Quick preset budget |
| presets.moderate.max_iterations | 3 | Moderate preset iteration cap |
| presets.moderate.max_sub_questions | 7 | Moderate preset sub-question limit |
| presets.moderate.max_sources_per_question | 8 | Moderate preset sources per question |
| presets.moderate.max_findings_for_synthesis | 40 | Moderate preset findings cap |
| presets.moderate.budget_usd | 10.00 | Moderate preset budget |
| presets.comprehensive.max_iterations | 5 | Comprehensive preset iteration cap |
| presets.comprehensive.max_sub_questions | 10 | Comprehensive preset sub-question limit |
| presets.comprehensive.max_sources_per_question | 15 | Comprehensive preset sources per question |
| presets.comprehensive.max_findings_for_synthesis | 50 | Comprehensive preset findings cap |
| presets.comprehensive.budget_usd | 25.00 | Comprehensive preset budget |
| search.primary_provider | tavily | Primary search provider |
| search.fallback_provider | brave | Fallback search provider |
| search.max_concurrent_searches | 10 | Max parallel searches |
| extraction.max_content_length | 50000 | Max extracted content length |
| extraction.timeout_seconds | 15 | Extraction timeout |
| extraction.fallback_chain | [trafilatura, readability, jina] | Extraction fallback order |
| stopping.min_coverage_ratio | 0.8 | Minimum coverage to stop |
| stopping.diminishing_returns_threshold | 0.05 | Diminishing returns cutoff |
| stopping.min_sources_per_question | 2 | Min sources before stopping |

### security_assessment.yaml

| Path | Value | Controls |
|------|-------|----------|
| roe_opa_endpoint | http://localhost:8181/v1/data/roe/allow | OPA endpoint for ROE scope validation |
| scope_validation_mode | strict | Scope validation: strict (deny-by-default) or permissive (log-only) |
| scanner_max_file_size_bytes | 1048576 | Max file size for SAST scanners |
| tool_registry_path | config/security/tools.yaml | Security tool registry path |
| triage_fp_confidence_threshold | 0.75 | Min confidence for false-positive classification |
| triage_code_context_lines | 10 | Lines of code context for triage |
| triage_max_findings_per_run | 500 | Max findings per triage run |
| evidence_sha256_on_write | true | Hash every evidence file on write |
| evidence_base_path | @workspace/evidence/ | Base path for evidence files |
| cost_hard_limit_usd | 50.0 | Hard cost ceiling per engagement |
| cost_alert_threshold_pct | 0.80 | Alert at this % of hard limit |
| report_min_executive_summary_chars | 300 | Min chars for executive summary |
| report_risk_score_weights.cvss | 0.40 | CVSS weight in composite risk score |
| report_risk_score_weights.exploitability | 0.30 | Exploitability weight in composite risk score |
| report_risk_score_weights.business_impact | 0.30 | Business impact weight in composite risk score |

### environments.yaml

| Path | Value | Controls |
|------|-------|----------|
| default_mode | local | Default execution mode (local/container/worktree) |
| container.enabled | false | Container execution availability |
| worktree.enabled | false | Worktree execution availability |

---

## Feature Flags

Source: `config/settings/features.yaml`

| Flag | Default | Purpose |
|------|---------|---------|
| auth_require_email_verification | false | Require email verification on registration |
| auth_allow_api_key_creation | true | Allow users to create API keys |
| auth_rate_limit_enabled | true | Enable authentication rate limiting |
| auth_require_api_authentication | true | Require authentication for API endpoints |
| auth_allow_self_registration | false | Allow new user self-registration |
| api_detailed_errors | false | Return detailed error messages in API responses |
| api_request_logging | true | Log all API requests |
| channel_telegram_enabled | true | Enable Telegram channel adapter |
| channel_slack_enabled | false | Enable Slack channel adapter |
| channel_discord_enabled | false | Enable Discord channel adapter |
| channel_whatsapp_enabled | false | Enable WhatsApp channel adapter |
| gateway_enabled | false | Enable multi-channel gateway |
| gateway_websocket_enabled | false | Enable WebSocket gateway transport |
| gateway_pairing_enabled | false | Enable session pairing across channels |
| agent_coordinator_enabled | false | Enable agent coordinator orchestration |
| agent_streaming_enabled | false | Enable agent response streaming |
| mcp_enabled | false | Enable MCP (Model Context Protocol) server |
| a2a_enabled | false | Enable A2A (Agent-to-Agent) protocol |
| security_startup_checks_enabled | true | Run security validation checks at startup |
| security_headers_enabled | true | Inject security headers on responses |
| security_cors_enforce_production | true | Enforce strict CORS in production |
| experimental_background_tasks_enabled | true | Enable background task infrastructure |
| experimental_mission_plan_daemon_enabled | true | Background daemon for autonomous plan dispatch |

---

## Notes

1. `research.yaml` and `environments.yaml` exist on disk but are **NOT** wired into `AppConfig` — no schema validation or load path.
2. `config/agents/mission_control.yaml` is loaded separately via `get_mission_control_config()` for agent routing/guardrails/dispatch settings.
3. All YAML schemas use `extra="forbid"` — unknown keys cause immediate validation errors at load time.
