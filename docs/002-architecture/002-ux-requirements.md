# Agentic Workforce Platform — UX Requirements

**Version:** 1.0
**Date:** 2026-05-11
**Classification:** Internal
**Companion:** [Architecture Walkthrough](001-architecture-walkthrough.md), [Solution Architecture](../002-architecture/001-solution-architecture.md)

This document defines the user-facing capabilities, screens, and flows for every persona.

---

## 1. Personas and Landing Pages

### 1.1 Platform Admin

**Who:** Platform engineering team (2-3 people). Manages the platform itself.

**Landing page: Platform Dashboard**

```
┌─────────────────────────────────────────────────────────────────┐
│ Agentic Workforce Platform                    [Admin] [Logout]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Platform Health          Active Now            Cost (MTD)       │
│  ██████████ All OK        4 projects running    $142.30          │
│  DB ✅ Redis ✅ Foundry ✅  7 executions active  ▼12% vs last mo  │
│                           12 agents working                      │
│                                                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ Recent Errors    │  │ Pending Actions  │  │ Model Usage     │ │
│  │                  │  │                  │  │                 │ │
│  │ ❌ Budget exceed │  │ 🔔 3 learning    │  │ Sonnet 4.6: 62%│ │
│  │    Payments Q2   │  │   promotions     │  │ Haiku 4.5: 31% │ │
│  │ ❌ Timeout: scan │  │   awaiting       │  │ GPT-4o: 5%     │ │
│  │    Auth module   │  │   approval       │  │ Embed: 2%      │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
│                                                                  │
│  Sidebar:                                                        │
│  📊 Dashboard (this page)                                        │
│  🤖 Agent Catalog                                                │
│  📋 Templates                                                    │
│  👥 Users                                                        │
│  🧠 Platform Knowledge                                           │
│  💰 Cost & Models                                                │
│  🔒 Audit                                                        │
│  ⚙️ Configuration                                                │
│  🚨 Emergency Stop                                               │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 Project Member (Owner, Operator, Reviewer, Viewer)

**Who:** Everyone else. Participates in projects.

**Landing page: My Projects**

```
┌─────────────────────────────────────────────────────────────────┐
│ Agentic Workforce Platform              [🔔 3] [Herman] [Logout]│
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  My Projects                                    [+ New Project]  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ 🟢 Payments Security Q2                    Owner          │  │
│  │    Objective: Achieve OWASP compliance for payments       │  │
│  │    Budget: $142 / $500 ████████░░░ 28%                    │  │
│  │    2 executions active | 3 artifacts | 12 learnings       │  │
│  │    Last activity: 3 minutes ago                           │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ 🟡 UK Client Onboarding                   Reviewer        │  │
│  │    Objective: Onboard ABC Ltd                             │  │
│  │    ⚠️ 1 approval waiting for you                          │  │
│  │    Last activity: 12 minutes ago                          │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ 🔵 Daily AI Research                      Operator         │  │
│  │    Objective: Track daily AI industry developments        │  │
│  │    Scheduled: runs daily at 06:00                         │  │
│  │    Last run: today 06:00 — 7 new findings                 │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Filter: [All ▼] [Active ▼]     Search: [_______________]      │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Project Creation Flow

**Who:** Owner (anyone with `member` platform role can create a project)

### Step-by-step wizard:

```
Step 1: Template                    Step 2: Basics
┌──────────────────────────┐       ┌──────────────────────────┐
│ Choose a project template │       │ Name: [Payments Sec Q2 ] │
│                           │       │                          │
│ ○ Maker-Checker-Supervisor│       │ Objective:               │
│   Regulated ops           │       │ [Achieve OWASP compliance│
│                           │       │  for the payments module]│
│ ● Research-Report         │       │                          │
│   Analysis & reporting    │       │ Jurisdiction:            │
│                           │       │ ● South Africa           │
│ ○ Monitor-Triage-Escalate │       │ ○ United Kingdom         │
│   Continuous monitoring   │       │ ○ Global                 │
│                           │       │                          │
│ ○ Case-Investigator       │       │ Budget ceiling:          │
│   Exception investigation │       │ [$500.00            ]    │
│                           │       │                          │
│ ○ Blank (no template)     │       │                          │
│                           │       │                          │
│              [Next →]     │       │              [Next →]    │
└──────────────────────────┘       └──────────────────────────┘

Step 3: Team                        Step 4: Members
┌──────────────────────────┐       ┌──────────────────────────┐
│ Assign agents from catalog│       │ Invite team members      │
│                           │       │                          │
│ Required by template:     │       │ [search users...      ]  │
│ ✅ Supervisor (1 min)     │       │                          │
│    → planner              │       │ Herman      Owner   [x]  │
│ ✅ Researcher (1+ min)    │       │ Thabiso     Operator [x] │
│    → security.reviewer    │       │ Ockert      Reviewer [x] │
│    → code.analyst         │       │                          │
│ ✅ QA (1 min)             │       │ [+ Add member]           │
│    → quality.verifier     │       │                          │
│ ✅ Reporter (1 min)       │       │                          │
│    → report.writer        │       │                          │
│                           │       │                          │
│ [+ Add agent]             │       │                          │
│              [Next →]     │       │         [Create Project] │
└──────────────────────────┘       └──────────────────────────┘
```

After creation, the user lands on the **Project Dashboard**.

---

## 3. Project Dashboard

**Who:** All project members (content adapts to role permissions)

```
┌─────────────────────────────────────────────────────────────────┐
│ ← My Projects    Payments Security Q2              [⚙ Settings]│
│ Objective: Achieve OWASP compliance for the payments module     │
│ Budget: $142.30 / $500.00 ████████░░░░░░ 28%      Status: 🟢   │
├──────┬──────┬─────────┬────────┬──────┬──────┬──────┬─────────────┤
│ Over │ Chat │ Planner │Workflow│ Runs │Artif │Knowl │  Console    │
│ view │      │ (board) │        │      │acts  │edge  │             │
├──────┴──────────┴────────┴──────────┴──────┴──────┴────────────┤
│                                                                  │
│  [Overview tab content — see Section 3.1]                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.1 Overview Tab

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                  │
│  Quick Actions                         Recent Activity           │
│  [▶ Run execution]                     • Execution completed     │
│  [📋 Run workflow]                       "Scan auth module" 3m   │
│  [📎 Upload document]                 • Artifact created         │
│  [💬 Chat with agent]                   "Vuln Report" 3m ago     │
│                                        • Learning discovered     │
│  Team (5 agents)                         "Auth null refs..." 5m  │
│  🤖 planner (Supervisor)              • Document uploaded        │
│  🤖 security.reviewer (Researcher)      "OWASP Guide.pdf" 1h    │
│  🤖 code.analyst (Researcher)                                   │
│  🤖 quality.verifier (QA)             Active Executions          │
│  🤖 report.writer (Reporter)          🔵 "Scan payments API"    │
│                                           Running... 2m elapsed  │
│  Members (3 humans)                                              │
│  👤 Herman (Owner)                     Pending Approvals         │
│  👤 Thabiso (Operator)                ⏳ "Review scan findings"  │
│  👤 Ockert (Reviewer)                    Waiting for reviewer    │
│                                                                  │
│  Documents (4 files)                   Learnings (12)            │
│  📄 OWASP Guide.pdf                   ✅ [0.95] Don't use...    │
│  📄 Payments API Spec.docx            ✅ [0.85] Auth null...    │
│  📄 SARB Directive D6.pdf             ✅ [0.72] Git blame...    │
│  📄 Previous Pen Test.pdf             ⚠️ [0.50] XML format?    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Chat Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Chat                                [Agent: security.reviewer ▼] │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ You: Can you explain finding #1 in more detail? What's the     │
│ actual attack vector for the SQL injection?                      │
│                                                                  │
│ 🤖 security.reviewer:                                           │
│ The SQL injection in `PaymentProcessor.Execute()` at line 142   │
│ occurs because the `merchantId` parameter is concatenated       │
│ directly into the SQL string:                                    │
│                                                                  │
│ ```csharp                                                        │
│ var sql = $"SELECT * FROM payments WHERE merchant = '{id}'";    │
│ ```                                                              │
│                                                                  │
│ An attacker could supply: `' OR 1=1; DROP TABLE payments; --`   │
│                                                                  │
│ **Remediation:** Use parameterised queries:                      │
│ ```csharp                                                        │
│ var sql = "SELECT * FROM payments WHERE merchant = @id";        │
│ ```                                                              │
│                                                                  │
│ 🔧 tool.called: ReadFile("/src/payments/PaymentProcessor.cs")   │
│ 🔧 tool.result: [file content shown to agent, not to user]     │
│                                                                  │
│ ┌─────────────────────────────────────────────────────────┐     │
│ │ [Type your message...]                          [Send] │     │
│ └─────────────────────────────────────────────────────────┘     │
│                                                                  │
│ Sessions: [Current ▼] | [+ New session]                         │
└─────────────────────────────────────────────────────────────────┘
```

- User picks an agent from the project's team to chat with
- Chat happens within a session that carries project context (PCD, learnings)
- Streaming response displayed in real-time
- Tool calls shown inline (agent is searching files, running a scan, etc.)
- Agent can produce artifacts during chat (markdown rendered inline)
- Chat history persisted and resumable across sessions
- "Chat with the Workflow Agent to design a workflow" is a specific use case

### 3.3 Planner Board (Kanban)

The central task management view. Shows **every task** in the project — whether it came from a workflow, an ad-hoc execution, or was manually added by a human. Tasks are cards on a Kanban board with columns mapped to task statuses. Dependencies are shown as connector lines between cards.

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ Planner                                                  [+ Add Task] [Filter ▼] [View]│
├──────────────┬──────────────┬──────────────┬──────────────┬──────────────┬──────────────┤
│  PROPOSED    │  APPROVED    │  QUEUED      │  RUNNING     │  COMPLETED   │  FAILED      │
│  (3)         │  (2)         │  (1)         │  (2)         │  (5)         │  (1)         │
├──────────────┼──────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│              │              │              │              │              │              │
│ ┌──────────┐│ ┌──────────┐│ ┌──────────┐│ ┌──────────┐│ ┌──────────┐│ ┌──────────┐│
│ │Review    ││ │Scan      ││ │Generate  ││ │🔵 Scan   ││ │✅ Scan   ││ │❌ Apply  ││
│ │login     ││ │payment   ││ │summary   ││ │payments  ││ │auth      ││ │remediate ││
│ │flows     ││ │API       ││ │report    ││ │API       ││ │module    ││ │          ││
│ │          ││ │          ││ │          ││ │          ││ │          ││ │Budget    ││
│ │Agent:    ││ │Agent:    ││ │Agent:    ││ │Agent:    ││ │Agent:    ││ │exceeded  ││
│ │sec.rev   ││ │sec.rev   ││ │report.wr ││ │sec.rev   ││ │sec.rev   ││ │$1.02     ││
│ │          ││ │          ││ │          ││ │          ││ │          ││ │          ││
│ │Depends:  ││ │Depends:  ││ │Depends:  ││ │45s...    ││ │45s $0.12 ││ │[Retry]   ││
│ │scan-auth ││ │none      ││ │scan-pay  ││ │$0.08...  ││ │3 findings││ │[Details] ││
│ │          ││ │          ││ │scan-auth ││ │          ││ │          ││ │          ││
│ │[Approve] ││ │          ││ │          ││ │          ││ │          ││ │          ││
│ │[Reject]  ││ │          ││ │          ││ │          ││ │          ││ │          ││
│ └──────────┘│ └──────────┘│ └──────────┘│ └──────────┘│ └──────────┘│ └──────────┘│
│              │              │              │              │              │              │
│ ┌──────────┐│ ┌──────────┐│              │ ┌──────────┐│ ┌──────────┐│              │
│ │Scan      ││ │Fix XSS   ││              │ │🔵 Verify ││ │✅ Scan   ││              │
│ │session   ││ │in forms  ││              │ │auth      ││ │for XSS   ││              │
│ │mgmt     ││ │          ││              │ │findings  ││ │          ││              │
│ │          ││ │Source:   ││              │ │          ││ │0 findings││              │
│ │Source:   ││ │manual    ││              │ │8s...     ││ │30s $0.08 ││              │
│ │workflow  ││ │(human)   ││              │ │$0.01...  ││ │          ││              │
│ │          ││ │          ││              │ │          ││ │          ││              │
│ │[Approve] ││ │          ││              │ │          ││ │          ││              │
│ │[Reject]  ││ │          ││              │ │          ││ │          ││              │
│ └──────────┘│ └──────────┘│              │ └──────────┘│ └──────────┘│              │
│              │              │              │              │              │              │
│ ┌──────────┐│              │              │              │ ...          │              │
│ │Check     ││              │              │              │              │              │
│ │CORS      ││              │              │              │              │              │
│ │config    ││              │              │              │              │              │
│ │          ││              │              │              │              │              │
│ │Source:   ││              │              │              │              │              │
│ │manual    ││              │              │              │              │              │
│ │(human)   ││              │              │              │              │              │
│ │          ││              │              │              │              │              │
│ │[Approve] ││              │              │              │              │              │
│ │[Reject]  ││              │              │              │              │              │
│ └──────────┘│              │              │              │              │              │
└──────────────┴──────────────┴──────────────┴──────────────┴──────────────┴──────────────┘
```

#### Task Card Anatomy

Each card shows:

```
┌──────────────────┐
│ Task title        │
│                   │
│ Agent: sec.rev    │  ← which agent runs this
│ Source: workflow   │  ← workflow | manual | ad-hoc
│ Depends: scan-auth│  ← blocked until dependency completes
│                   │
│ 45s  $0.12        │  ← duration + cost (when completed)
│ 3 findings        │  ← one-line result summary
│                   │
│ [Approve] [Reject]│  ← actions (role-dependent)
└──────────────────┘
```

**Card colour/border:**
- Grey border = proposed (waiting for approval)
- Blue border = approved or queued
- Blue pulse = running (animated)
- Green border = completed
- Red border = failed
- Amber border = skipped/cancelled

**Running cards** show a live progress indicator (elapsed time + cost accumulating).

**Failed cards** show a brief error message and a `[Details]` button that opens the error drawer.

#### Columns (mapped to PlanTaskStatus)

| Column | Status | Cards Can Be Moved Here By |
|--------|--------|---------------------------|
| Proposed | `proposed` | System (from planner agent or workflow), human (manual add) |
| Approved | `approved` | Owner, Operator (approve action), or auto-approve in autonomous mode |
| Queued | `queued` | System (when dependencies met and ready to dispatch) |
| Running | `running` | System (when execution starts) |
| Completed | `completed` | System (when execution succeeds) |
| Failed | `failed` | System (when execution fails) |

Additional statuses shown as visual indicators, not separate columns:
- `skipped` — greyed out card in the Completed column with a skip icon
- `cancelled` — greyed out card with a cancel icon

#### Human Actions on the Board

| Action | Who | How |
|--------|-----|-----|
| **Add a task** | Owner, Operator | `[+ Add Task]` button → form: title, objective, agent, dependencies |
| **Approve a task** | Owner, Operator | `[Approve]` button on proposed card → moves to Approved |
| **Reject a task** | Owner, Operator | `[Reject]` button → card removed (status: cancelled) |
| **Approve at a gate** | Owner, Operator, **Reviewer** | `[Approve]` on a running card that's paused at a gate |
| **Retry a failed task** | Owner, Operator | `[Retry]` button on failed card → moves back to Approved |
| **Drag a card** | Owner, Operator | Drag from Proposed → Approved (implicit approve) |
| **Reorder tasks** | Owner | Drag within a column to change priority/execution order |

**Reviewers** can only interact with approval gates — they cannot add, move, or retry tasks.

#### Dependencies

Dependencies are shown as connector lines between cards. A task in the Queued column won't dispatch until all its dependencies are in the Completed column:

```
[Scan auth module] ──────────┐
       (completed)           │
                             ▼
                    [Generate summary report]
                         (queued — waiting)
[Scan payment API] ──────────┘
       (running)
```

In the UI, dependency lines are drawn as subtle SVG connectors (similar to the workflow graph but within the Kanban layout). A blocked task shows which dependencies are outstanding.

#### Error Detail Drawer

Clicking `[Details]` on a failed card opens a slide-out drawer on the right:

```
                                          ┌────────────────────────┐
                                          │ ❌ Apply remediation    │
                                          │                        │
                                          │ Status: Failed         │
                                          │ Agent: coder           │
                                          │ Duration: 12s          │
                                          │ Cost: $1.02            │
                                          │                        │
                                          │ Error:                 │
                                          │ BudgetExceededExceptn  │
                                          │ Agent ceiling: $1.00   │
                                          │ Actual cost: $1.02     │
                                          │                        │
                                          │ Stack trace:           │
                                          │ ┌──────────────────┐   │
                                          │ │ at BudgetEnforci │   │
                                          │ │   ngChatClient.  │   │
                                          │ │   GetResponseAs  │   │
                                          │ │   ync(...)       │   │
                                          │ │ at ChatClientAge│   │
                                          │ │   nt.RunCoreAs  │   │
                                          │ │   ync(...)       │   │
                                          │ └──────────────────┘   │
                                          │                        │
                                          │ Agent output (partial):│
                                          │ ┌──────────────────┐   │
                                          │ │ { "status": "fai │   │
                                          │ │   led",          │   │
                                          │ │   "tokens_used": │   │
                                          │ │   4200,          │   │
                                          │ │   "partial_outpu │   │
                                          │ │   t": "I was abo │   │
                                          │ │   ut to apply..."│   │
                                          │ └──────────────────┘   │
                                          │                        │
                                          │ Console events:        │
                                          │ 14:33:01 tool.called   │
                                          │   ReadFile(Program.cs) │
                                          │ 14:33:02 llm.response  │
                                          │   "Analyzing the..."   │
                                          │ 14:33:03 agent.failed  │
                                          │   BudgetExceededException│
                                          │                        │
                                          │ [Retry] [View full execution]│
                                          │               [Close ✕]│
                                          └────────────────────────┘
```

The drawer shows:
- Error type and message
- Stack trace (expandable)
- Partial agent output (if any was produced before failure)
- Console events for this task (filtered from the project console)
- Action buttons (Retry, View full execution detail)

#### Task Sources

Every card shows where it came from:

| Source | How It Got Here |
|--------|----------------|
| `workflow` | Created by the WorkflowInterpreter from a workflow definition node |
| `planner` | Created by the planner agent during a `run` command |
| `manual` | Added by a human via `[+ Add Task]` |
| `ad-hoc` | Created from `awp run "do something"` CLI command |
| `retry` | A retried version of a previously failed task |

#### Real-Time Updates

The board updates in real-time via SignalR:
- Cards move between columns automatically as status changes
- Running cards show live elapsed time and cost
- New tasks appear instantly when the planner agent creates them
- Failed tasks flash red briefly to draw attention
- Approval gate notifications highlight the relevant card

#### Filters

```
Filter: [All agents ▼] [All sources ▼] [All status ▼] [Show completed ☑]
```

By default, completed tasks are collapsed (show count only) to keep the board focused on active work. Toggle to see full history.

### 3.4 Workflows Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Workflows                              [+ New] [Import YAML]    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  My Workflows                                                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 📋 Security Assessment v2                    [Run] [Edit] │   │
│  │    7 nodes | 2 decision points | Last run: today 14:30   │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 📋 Quick Vulnerability Scan                  [Run] [Edit] │   │
│  │    3 nodes | 0 decision points | Last run: yesterday     │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Platform Templates                                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 📋 Standard Security Assessment              [Use]        │   │
│  │    Platform template | v3 | Used by 8 projects           │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Run History                                                     │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ✅ Security Assessment v2    today 14:30    $0.45  3m    │   │
│  │ ✅ Quick Vuln Scan           yesterday      $0.12  45s   │   │
│  │ ❌ Security Assessment v2    May 9          $0.32  2m    │   │
│  │    Failed: Budget exceeded at node "remediate"            │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Schedules                                                       │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ⏰ Quick Vuln Scan    Every weekday 06:00   [Pause][Edit] │   │
│  │    Next run: tomorrow 06:00                               │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 3.4.1 Workflow Visual Editor

```
┌─────────────────────────────────────────────────────────────────┐
│ Workflow Editor: Security Assessment v2   [Validate] [Save] [Run]│
├────────┬────────────────────────────────────────┬───────────────┤
│ Palette│              Canvas                    │  Properties   │
│        │                                        │               │
│ ┌────┐ │  ┌───────┐    ┌──────────┐             │ Selected:     │
│ │Start│ │  │ Start │───▶│Scan Auth │──┐          │ "Scan Auth"   │
│ └────┘ │  └───────┘    └──────────┘  │          │               │
│ ┌────┐ │                             ▼          │ Agent:        │
│ │Agent│ │               ┌───────────────┐       │ [security.rev]│
│ │Task │ │               │ AI Decision   │       │               │
│ └────┘ │               │ "Classify     │       │ Objective:    │
│ ┌────┐ │               │  severity"    │       │ [Scan /src/   │
│ │Human│ │               └──┬────────┬──┘       │  auth for...] │
│ │Decn │ │           critical│   acceptable     │               │
│ └────┘ │                   ▼        ▼          │ Timeout: 600s │
│ ┌────┐ │            ┌─────────┐  ┌─────┐       │ Budget: $2.00 │
│ │ AI │ │            │ Human   │  │ End │       │               │
│ │Decn │ │            │ Approve │  │ OK  │       │               │
│ └────┘ │            └──┬───┬──┘  └─────┘       │               │
│ ┌────┐ │          approve reject               │               │
│ │Para │ │              ▼     ▼                  │               │
│ │llel │ │           ┌─────┐┌──────┐             │               │
│ └────┘ │           │ Fix ││ End  │             │               │
│ ┌────┐ │           └─────┘│Reject│             │               │
│ │Sub- │ │                  └──────┘             │               │
│ │flow │ │                                       │               │
│ └────┘ │                                        │               │
│ ┌────┐ │                                        │               │
│ │Actn │ │  💬 Chat: [Ask Workflow Agent...]      │               │
│ └────┘ │                                        │               │
│ ┌────┐ │                                        │               │
│ │ End│ │                                        │               │
│ └────┘ │                                        │               │
├────────┴────────────────────────────────────────┴───────────────┤
│ Validation: ✅ All nodes reachable | ✅ All paths terminate      │
│             ✅ Decision nodes have ≥2 edges | ✅ Agents exist    │
└─────────────────────────────────────────────────────────────────┘
```

### 3.4.2 Workflow Execution Visualization (Live)

```
┌─────────────────────────────────────────────────────────────────┐
│ Running: Security Assessment v2         Elapsed: 2m 34s  $0.23  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────┐    ┌──────────┐    ┌───────────────┐                 │
│  │ Start │───▶│Scan Auth │───▶│ AI Decision   │                 │
│  │  ✅   │    │  ✅ 45s  │    │ "Classify"    │                 │
│  └───────┘    │  $0.12   │    │  ✅ → critical│                 │
│               └──────────┘    └──────┬────────┘                 │
│                                      │                           │
│                                      ▼                           │
│                               ┌──────────────┐                  │
│                               │ Human Approve │                  │
│                               │  ⏳ WAITING   │                  │
│                               │  for reviewer │                  │
│                               │              │                  │
│                               │ [Approve]     │ ← reviewer only │
│                               │ [Reject]      │                  │
│                               │ [Escalate]    │                  │
│                               │              │                  │
│                               │ Timeout: 3h  │                  │
│                               │ 58m remaining│                  │
│                               └──────────────┘                  │
│                                                                  │
│  Node Timeline:                                                  │
│  ████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░                    │
│  Start  Scan Auth  AI Decision  [waiting...]                     │
│  0s     0-45s      45-47s       47s-?                           │
│                                                                  │
│  [View Console] [View Artifacts So Far]                          │
└─────────────────────────────────────────────────────────────────┘
```

### 3.5 Runs Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Executions                                       [▶ Run Ad-hoc] │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ ✅ "Scan auth module"           today 14:30    $0.14   45s      │
│    security.reviewer → 3 findings | 1 artifact produced         │
│                                                                  │
│ ✅ "Review payment flows"       today 13:15    $0.23   2m       │
│    security.reviewer → 1 finding | 1 artifact produced          │
│                                                                  │
│ ❌ "Apply remediation"          today 13:20    $1.02   12s      │
│    coder → BudgetExceededException                              │
│                                                                  │
│ ✅ "Scan for XSS"              yesterday       $0.08   30s      │
│    security.reviewer → 0 findings                               │
│                                                                  │
│ Filter: [All status ▼] [All agents ▼] [Date range]              │
└─────────────────────────────────────────────────────────────────┘
```

Click an execution → Execution Detail:

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Executions    "Scan auth module"         Status: ✅ Completed  │
│ Agent: security.reviewer | Duration: 45s | Cost: $0.14          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Tasks:                                                           │
│ ✅ security.reviewer — Scan for injection vulnerabilities       │
│    Output: { "findings": 3, "critical": 1, "high": 2 }         │
│    [View full output ▶]                                         │
│    Artifacts: [OWASP Vulnerability Report]                      │
│    Duration: 42s | Cost: $0.12 | Tokens: 1,240 in / 3,100 out  │
│                                                                  │
│ ✅ quality.verifier — Verify scan completeness                  │
│    Output: { "passed": true, "coverage": 0.94 }                │
│    Duration: 3s | Cost: $0.02                                   │
│                                                                  │
│ Learnings extracted:                                             │
│ 🧠 "SQL injection in PaymentProcessor.Execute()" [0.50]        │
│                                                                  │
│ PCD changes:                                                     │
│ 📝 current_state.known_issues += "SQL injection in payments"    │
│                                                                  │
│ [View in Console] [Download output JSON]                         │
└─────────────────────────────────────────────────────────────────┘
```

### 3.6 Artifacts Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Artifacts                                                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ 📄 OWASP Vulnerability Report              markdown    today    │
│    By: security.reviewer | Execution: "Scan auth module"        │
│    [View] [Download]                                            │
│                                                                  │
│ 📊 Security Findings Summary               xlsx        today    │
│    By: report.writer | Execution: "Generate summary"            │
│    [Download]                                                    │
│                                                                  │
│ 📑 Remediation Plan                        pptx        May 9   │
│    By: report.writer | Execution: "Create remediation plan"     │
│    [Download]                                                    │
│                                                                  │
│ 💻 Auth Fix Patch                          code        May 8   │
│    By: coder | Language: csharp                                 │
│    [View] [Copy]                                                │
│                                                                  │
│ Filter: [All types ▼] [All agents ▼] [Date range]              │
└─────────────────────────────────────────────────────────────────┘
```

Click a markdown artifact → inline rendered view:

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Artifacts    OWASP Vulnerability Report    [Download] [Raw]   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  # OWASP Vulnerability Report                                   │
│  ## Payments Module — Authentication Subsystem                   │
│                                                                  │
│  ### Executive Summary                                           │
│  The scan identified 3 vulnerabilities in /src/payments/auth:   │
│                                                                  │
│  | # | Severity | Type           | File              | CVSS |   │
│  |---|----------|----------------|-------------------|------|   │
│  | 1 | CRITICAL | SQL Injection  | PaymentProcessor  | 9.8  |   │
│  | 2 | HIGH     | Auth Bypass    | TokenValidator    | 8.1  |   │
│  | 3 | HIGH     | Missing Input  | RequestHandler    | 7.5  |   │
│  |   |          | Validation     |                   |      |   │
│  ...                                                             │
└─────────────────────────────────────────────────────────────────┘
```

### 3.7 Knowledge Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Knowledge                                                        │
├───────┬────────────┬───────────┬──────────┬─────────────────────┤
│  PCD  │  Learnings │ Decisions │  Intent  │  Documents          │
├───────┴────────────┴───────────┴──────────┴─────────────────────┤
│                                                                  │
│ [Learnings tab shown]                                            │
│                                                                  │
│ 🌐 [platform] "Git blame context improves code reviews"         │
│    Promoted | Confidence: 0.95 | Confirmed across 4 projects    │
│                                                                  │
│ ✅ [0.95] "Don't use string concat for SQL in payments"         │
│    anti_pattern | Seen 4× | By: security.reviewer               │
│    Evidence: [exec-001] [exec-007] [exec-012] [exec-015]        │
│    Recommendation: "Use parameterised queries with Dapper"       │
│    [Retract] [Edit] [Propose promotion]                          │
│                                                                  │
│ ✅ [0.85] "Auth null refs from missing DI registration"         │
│    failure_pattern | Seen 2× | By: coder                        │
│    [Retract] [Edit]                                              │
│                                                                  │
│ ⚠️ [0.50] "Payments API may use XML not JSON"                   │
│    domain_insight | Seen 1× | By: researcher                    │
│    [Retract] [Edit]                                              │
│                                                                  │
│ 🚫 [retracted] "Settlement API uses SOAP"                       │
│    Retracted by: Herman | "Actually uses REST + ISO 20022"       │
│    Superseded by: "Settlement API uses REST + ISO 20022"         │
│                                                                  │
│ Filter: [All kinds ▼] [All agents ▼] [Min confidence ▼]         │
│         [Show retracted ☑]                                       │
└─────────────────────────────────────────────────────────────────┘
```

PCD sub-tab:

```
┌─────────────────────────────────────────────────────────────────┐
│ PCD  Learnings  Decisions  Intent  Documents                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ identity                                     [Edit section]      │
│   name: Payments Security Q2                                     │
│   objective: Achieve OWASP compliance for payments               │
│   tech_stack: [.NET 9, PostgreSQL, React]                       │
│                                                                  │
│ principles                                   [+ Add principle]   │
│   📌 "Always use parameterised queries"         Herman, May 8   │
│   📌 "Use repository pattern for data access"   Herman, May 7   │
│   📌 "All endpoints must validate input"        Thabiso, May 9  │
│                                                                  │
│ guardrails                                                       │
│   🔒 "Never store secrets in config" (inherited from template)  │
│   🔒 "Never modify production code directly"    Herman, May 7   │
│                                                                  │
│ current_state                                v12 | [View diff]   │
│   active_workstreams: ["OWASP scan", "Auth remediation"]        │
│   known_issues: ["SQL injection in PaymentProcessor"]            │
│   priorities: ["Fix critical finding first"]                     │
│                                                                  │
│ Version history: v1 → v2 → ... → v12                           │
│ [View full change history]                                       │
└─────────────────────────────────────────────────────────────────┘
```

### 3.8 Console Tab

Real-time event timeline (designed in ADR-005 and Solution Architecture Section 10.1). Log everything, blacklist noise.

```
┌─────────────────────────────────────────────────────────────────┐
│ Console                        [Filter ▼] [Auto-scroll ✅] [⏸] │
├─────────────────────────────────────────────────────────────────┤
│ 14:32:01 🤖 security.reviewer  agent.started                   │
│          "Scanning /src/payments for injection vulnerabilities"  │
│                                                                  │
│ 14:32:03 🔧 security.reviewer  tool.called                     │
│          RunSemgrep("/src/payments", "p/owasp-top-ten")         │
│                                                                  │
│ 14:32:45 🔧 security.reviewer  tool.result                     │
│          Semgrep: 3 findings (1 critical, 2 high)               │
│                                                                  │
│ 14:32:46 🤖 security.reviewer  llm.response                    │
│          "Found 3 vulnerabilities in the payments module..."     │
│          Tokens: 1,240 in / 3,100 out | Cost: $0.05             │
│                                                                  │
│ 14:32:47 📄 security.reviewer  artifact.created                 │
│          "OWASP Vulnerability Report" (markdown)                 │
│                                                                  │
│ 14:32:48 🧠 system             learning.created                 │
│          "SQL injection in PaymentProcessor.Execute()"           │
│          Confidence: 0.50 | Kind: failure_pattern               │
│                                                                  │
│ 14:32:49 ❌ coder              agent.failed                     │
│          BudgetExceededException: $1.02 exceeds $1.00 ceiling   │
│          ════════════════════════════════════════════════════    │
│                                                                  │
│ 14:33:01 ⏳ system             gate.requested                   │
│          "Review scan findings" — waiting for reviewer           │
│          [Approve] [Reject] [Escalate]  ← only for reviewers    │
│                                                                  │
│ Filter: [All types ▼] [All agents ▼] [All severity ▼]          │
│         [Show blacklisted ☐]                                     │
└─────────────────────────────────────────────────────────────────┘
```

### 3.9 Settings Tab

```
┌─────────────────────────────────────────────────────────────────┐
│ Settings                                        Owner only       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ General                                                          │
│   Name: [Payments Security Q2          ]                        │
│   Objective: [Achieve OWASP compliance...]                      │
│   Status: [Active ▼]                                            │
│   Jurisdiction: South Africa (cannot be changed after creation) │
│                                                                  │
│ Budget                                                           │
│   Ceiling: [$500.00    ] | Spent: $142.30 | Remaining: $357.70 │
│   Warning threshold: [80%]                                       │
│                                                                  │
│ Team (agents)                                                    │
│   🤖 planner (Supervisor)            [Remove]                   │
│   🤖 security.reviewer (Researcher)  [Remove]                   │
│   🤖 code.analyst (Researcher)       [Remove]                   │
│   🤖 quality.verifier (QA)           [Remove]                   │
│   🤖 report.writer (Reporter)        [Remove]                   │
│   [+ Add agent from catalog]                                     │
│                                                                  │
│ Members (humans)                                                 │
│   👤 Herman      Owner    [Transfer ownership]                   │
│   👤 Thabiso     Operator [Change role ▼] [Remove]               │
│   👤 Ockert      Reviewer [Change role ▼] [Remove]               │
│   [+ Invite member]                                              │
│                                                                  │
│ Danger Zone                                                      │
│   [Archive project] [Delete project]                             │
└─────────────────────────────────────────────────────────────────┘
```

---

## 4. Notification System

### 4.1 In-App Notifications

Bell icon with unread count in the header. Click to expand:

```
┌────────────────────────────────────────┐
│ 🔔 Notifications (3 unread)    [Mark all read] │
├────────────────────────────────────────┤
│                                        │
│ ⏳ Approval needed              2m ago │
│    "Review scan findings"              │
│    Payments Security Q2                │
│    [Go to approval →]                  │
│                                        │
│ ❌ Execution failed             15m ago│
│    "Apply remediation"                 │
│    Budget exceeded ($1.02 > $1.00)     │
│    [View execution →]                  │
│                                        │
│ ⚠️ Budget warning              1h ago │
│    Payments Security Q2 at 82%         │
│    [View project →]                    │
│                                        │
│ ✅ Execution completed          2h ago │
│    "Scan for XSS" — 0 findings        │
│    [View execution →]                  │
│                                        │
│ [View all notifications]               │
└────────────────────────────────────────┘
```

### 4.2 Notification Routing by Role

| Event | Owner | Operator | Reviewer | Viewer |
|-------|-------|----------|----------|--------|
| Approval gate waiting | Yes | Yes (if they triggered) | **Yes** (always) | No |
| Execution completed | Yes | Yes | No | No |
| Execution failed | **Yes** | **Yes** | No | No |
| Budget warning (80%) | **Yes** | No | No | No |
| Budget exceeded (100%) | **Yes** | Yes | No | No |
| New artifact created | Yes | Yes | Yes | No |
| Learning retracted | Yes | No | No | No |
| Scheduled run completed | Yes | Yes | No | No |

### 4.3 External Notification Channels

| Channel | How | Configuration |
|---------|-----|---------------|
| In-app | SignalR push + bell icon | Always on |
| Email | Azure Communication Services or SendGrid | Per-user opt-in |
| Telegram | Bot API (from prototype) | Per-user opt-in, bot token in config |
| Microsoft Teams | Teams webhook or Graph API | Per-project opt-in |

Users configure notification preferences in their profile settings.

---

## 5. Platform Admin Screens

### 5.1 Agent Catalog

```
┌─────────────────────────────────────────────────────────────────┐
│ Agent Catalog                                   [+ Create Agent] │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ 🤖 security.reviewer                     ✅ Enabled             │
│    Type: specialist | Model: claude-sonnet-4-6                  │
│    Used by: 4 projects | Cost MTD: $23.40                       │
│    [View] [Edit] [Disable]                                      │
│                                                                  │
│ 🤖 planner                               ✅ Enabled             │
│    Type: orchestrator | Model: claude-sonnet-4-6                │
│    Used by: 6 projects | Cost MTD: $15.20                       │
│    [View] [Edit] [Disable]                                      │
│                                                                  │
│ 🤖 quality.verifier                      ✅ Enabled             │
│    Type: specialist | Model: claude-haiku-4-5                   │
│    Used by: 5 projects | Cost MTD: $4.10                        │
│    [View] [Edit] [Disable]                                      │
│                                                                  │
│ 🤖 experimental.analyzer                 ⚫ Disabled             │
│    Type: specialist | Model: claude-sonnet-4-6                  │
│    Used by: 0 projects                                          │
│    [View] [Edit] [Enable]                                       │
│                                                                  │
│ Search: [_____________] Filter: [All types ▼] [All status ▼]   │
└─────────────────────────────────────────────────────────────────┘
```

Agent detail view:

```
┌─────────────────────────────────────────────────────────────────┐
│ ← Catalog    security.reviewer                   [Edit] [Test]  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Configuration                                                    │
│   Name: security.reviewer                                       │
│   Type: specialist                                              │
│   Version: 2.1.0                                                │
│   Model: claude-sonnet-4-6                                      │
│   Max budget: $1.00 per execution                               │
│   Max input: 32,000 tokens                                      │
│                                                                  │
│ System Prompt                            v3 | [View history]    │
│ ┌─────────────────────────────────────────────────────────┐     │
│ │ You are a senior security engineer specialising in      │     │
│ │ OWASP Top 10 vulnerability detection...                 │     │
│ └─────────────────────────────────────────────────────────┘     │
│ [Edit prompt]                                                    │
│                                                                  │
│ Tools                                                            │
│   🔧 file_read, file_search, web_search, RunSemgrep            │
│                                                                  │
│ Usage Statistics                                                 │
│   Projects: 4 | Executions MTD: 47 | Cost MTD: $23.40          │
│   Avg duration: 38s | Success rate: 94%                         │
│                                                                  │
│ Workshop (Sandbox Testing)                                       │
│ [Run in sandbox with test input]                                │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 Platform Knowledge

```
┌─────────────────────────────────────────────────────────────────┐
│ Platform Knowledge                                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Promoted Learnings (12)                                          │
│                                                                  │
│ 🌐 [0.95] "Git blame context improves code reviews"            │
│    Confirmed across: 4 projects | Promoted by: Herman           │
│    [Edit] [Demote]                                              │
│                                                                  │
│ 🌐 [0.88] "Semgrep timeouts on files >10K lines"              │
│    Confirmed across: 3 projects | Promoted by: Herman           │
│    [Edit] [Demote]                                              │
│                                                                  │
│ Pending Promotions (3)                                           │
│                                                                  │
│ ⏳ "Repository pattern reduces data access bugs by ~30%"       │
│    From: Platform Engineering | Confidence: 0.78 | 2 projects   │
│    [Approve] [Reject]                                           │
│                                                                  │
│ ⏳ "Azure Flex Server pgvector needs AVX2 verification"        │
│    From: Security Research | Confidence: 0.72 | 2 projects      │
│    [Approve] [Reject]                                           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.3 Cost and Model Dashboard

```
┌─────────────────────────────────────────────────────────────────┐
│ Cost & Models                             Period: [This month ▼] │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Total Cost: $342.10          Tokens: 12.4M in / 4.2M out        │
│                                                                  │
│ Cost by Model           Cost by Project        Cost by Agent     │
│ ┌───────────────┐      ┌───────────────┐      ┌──────────────┐ │
│ │Sonnet 4.6 $210│      │Payments  $142 │      │sec.rev  $89  │ │
│ │Haiku 4.5   $82│      │Onboard   $98  │      │planner  $67  │ │
│ │GPT-4o      $38│      │Research  $64  │      │coder    $54  │ │
│ │Embed       $12│      │Platform  $38  │      │report   $42  │ │
│ └───────────────┘      └───────────────┘      └──────────────┘ │
│                                                                  │
│ Cost Timeline (hourly)                                           │
│ $5 ┤                                                            │
│    │      ╭─╮                                                   │
│ $3 ┤  ╭───╯ ╰──╮                                               │
│    │──╯         ╰──────────────────                             │
│ $0 ┤                                                            │
│    └──────────────────────────────                              │
│     06:00  09:00  12:00  15:00                                  │
│                                                                  │
│ Quota Status                                                     │
│ claude-sonnet-4-6:  124K / 200K TPM  ██████████░░░░ 62%         │
│ claude-haiku-4-5:    31K / 100K TPM  ███░░░░░░░░░░ 31%         │
│ gpt-4o:              12K / 100K TPM  █░░░░░░░░░░░░  5%         │
│                                                                  │
│ Cache Performance                                                │
│ Prompt cache hit rate: 42% | Savings: $38.20 this month         │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 6. Search

### 6.1 Project-Level Search

Available from within any project — searches artifacts, learnings, executions, console events, documents, PCD:

```
┌─────────────────────────────────────────────────────────────────┐
│ Search: [SQL injection                              ] [Search]  │
│ Scope: [This project ▼] Mode: [Semantic ▼]                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Artifacts (2)                                                    │
│ 📄 OWASP Vulnerability Report — "...SQL injection in Payment..."│
│ 💻 Auth Fix Patch — "...parameterised queries replace concat..."│
│                                                                  │
│ Learnings (1)                                                    │
│ 🧠 "SQL injection in PaymentProcessor.Execute()" [0.50]        │
│                                                                  │
│ Console Events (3)                                               │
│ 14:32:46 security.reviewer — "Found 3 vulnerabilities..."       │
│ 14:32:48 system — learning.created "SQL injection..."            │
│ 13:15:22 security.reviewer — "Payment flow uses string concat"  │
│                                                                  │
│ Documents (1)                                                    │
│ 📄 OWASP Guide.pdf — Page 14: "...injection flaws occur when..." │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 Platform-Level Search (Platform Admin)

Searches across all projects:

```
┌─────────────────────────────────────────────────────────────────┐
│ Platform Search: [authentication patterns           ] [Search]  │
│ Scope: [All projects ▼] Mode: [Semantic ▼]                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Learnings across projects (4)                                    │
│ 🧠 Payments Q2: "Auth null refs from missing DI" [0.85]        │
│ 🧠 Platform Eng: "Repository pattern reduces bugs" [0.78]      │
│ 🧠 Onboarding: "OAuth token refresh needs retry" [0.65]        │
│ 🌐 Platform: "Git blame improves code reviews" [0.95]          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 7. User Profile and Preferences

```
┌─────────────────────────────────────────────────────────────────┐
│ Profile                                                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ Display Name: [Herman Young          ]                          │
│ Email: herman.young@investec.co.za (from Entra ID)              │
│ Platform Role: member                                            │
│                                                                  │
│ Notification Preferences                                         │
│ ☑ In-app notifications                                          │
│ ☑ Email for approvals and failures                              │
│ ☐ Telegram notifications                                        │
│ ☐ Teams notifications                                           │
│                                                                  │
│ API Keys                                                         │
│ 🔑 ci-pipeline (mc_k8f3...)  Created: May 1  Last used: today  │
│    Scopes: read, execute     [Revoke]                           │
│ [+ Create API key]                                              │
│                                                                  │
│ Active Sessions                                                  │
│ 💬 Payments Q2 / security.reviewer — 12 messages  [Resume]     │
│ 💬 Platform Eng / planner — 3 messages            [Resume]     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 8. Permission Matrix Summary

What each role sees and can do across all screens:

| Screen / Action | Platform Admin | Owner | Operator | Reviewer | Viewer |
|-----------------|---------------|-------|----------|----------|--------|
| Platform Dashboard | **Yes** | No | No | No | No |
| Agent Catalog (manage) | **Yes** | No | No | No | No |
| Template Management | **Yes** | No | No | No | No |
| User Management | **Yes** | No | No | No | No |
| Platform Knowledge | **Yes** | No | No | No | No |
| Cost & Model Dashboard | **Yes** | No | No | No | No |
| Platform Search | **Yes** | No | No | No | No |
| Emergency Stop | **Yes** | No | No | No | No |
| Create project | — | **Yes** (creator becomes Owner) | — | — | — |
| Project Dashboard | Override | **Yes** | Yes | Yes | Yes |
| Project Settings | Override | **Yes** | No | No | No |
| Run execution | Override | **Yes** | **Yes** | No | No |
| Run workflow | Override | **Yes** | **Yes** | No | No |
| Design workflow | Override | **Yes** | **Yes** | No | No |
| Chat with agent | Override | **Yes** | **Yes** | No | No |
| Approve at gate | Override | **Yes** | **Yes** | **Yes** | No |
| Upload document | Override | **Yes** | **Yes** | No | No |
| Add PCD principles | Override | **Yes** | **Yes** | No | No |
| Retract learning | Override | **Yes** | **Yes** | No | No |
| Edit learning | Override | **Yes** | No | No | No |
| View console | Override | **Yes** | Yes | Yes | Yes |
| View artifacts | Override | **Yes** | Yes | Yes | Yes |
| View knowledge | Override | **Yes** | Yes | Yes | Yes |
| View executions | Override | **Yes** | Yes | Yes | Yes |
| View costs | Override | **Yes** | Yes | Yes | Yes |
| Project search | Override | **Yes** | Yes | Yes | Yes |
| Export data | Override | **Yes** | Yes | Yes | Yes |
| Delete project | Override | **Yes** | No | No | No |

**Override** = Platform Admin can access all projects for audit/emergency, logged explicitly.

---

## 9. Responsive Design

| Surface | Priority | Notes |
|---------|----------|-------|
| Desktop (1280px+) | **Primary** | Full experience — all screens, visual editor, side panels |
| Tablet (768-1279px) | Secondary | Collapsed sidebar, simplified workflow viewer, full console |
| Mobile (< 768px) | Notifications only | Notification bell, approval actions, project status. Not for full workflow design. |

The visual workflow editor (React Flow) requires desktop. All other views are responsive.

---

## 10. Reference

| Capability | Architecture Doc | ADR |
|------------|-----------------|-----|
| Console view | Solution Architecture 10.1 | ADR-005 |
| Artifact gallery | Solution Architecture 5.6 | — |
| Knowledge view | Solution Architecture 5.8 | ADR-014 |
| Document library | Solution Architecture 5.7 | — |
| Workflow editor | Solution Architecture 7.1 | ADR-013 |
| Workflow execution viz | Solution Architecture 7.2 | ADR-013 |
| Chat interface | Solution Architecture 5.1 | ADR-003 |
| Notifications | Solution Architecture 10.1 | ADR-005 |
| Search (semantic) | Solution Architecture 5.5 | ADR-004 |
| Auth / role enforcement | Solution Architecture 8.2 | ADR-007 |
| Cost dashboard | Solution Architecture 7.3 | ADR-009 |
| Real-time updates | Solution Architecture 5 (Layer 5) | ADR-005 |
