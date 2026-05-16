<style>
</style>

**TECHNICAL
REQUIREMENTS DOCUMENT**

**Agentic Workforce Platform**

*Technical Specification, Architecture and Data
Model for a Microsoft Agent Framework Platform on Azure*

**Document Type:** Technical Requirements Document
(TRD)

**Version:** 0.1 — Initial
Draft

**Companion to:** Agentic
Workforce Platform — BRD v0.1

**Status:** Draft for Review

**Date:** April 2026

**Classification:** Internal
— Confidential

Document Control

Version History

| **Version** | **Date**       | **Author**           | **Summary of Changes**                            |
| ----------- | -------------- | -------------------- | ------------------------------------------------- |
| 0.1         | April<br> 2026 | L.<br> [Lead Author] | Initial<br> technical draft to accompany BRD v0.1 |

Design Authority Reviewers

| **Role**                           | **Name** | **Responsibility**                       |
| ---------------------------------- | -------- | ---------------------------------------- |
| Group<br> Chief Architect          | TBC      | Overall<br> architectural approval       |
| Head of Cloud Platform             | TBC      | Azure landing zone alignment             |
| Head of<br> Security Architecture  | TBC      | Security<br> design sign-off             |
| Head of Data Architecture          | TBC      | Data model and lineage sign-off          |
| Head of<br> Identity               | TBC      | Entra<br> ID, RBAC and workload identity |
| Principal AI / ML Engineer         | TBC      | Agent runtime and model governance       |
| Head of<br> Engineering Enablement | TBC      | DevSecOps<br> and SDLC integration       |
| Enterprise Risk — Operational Risk | TBC      | Risk and control conformance             |
| Group<br> Compliance — AI Lead     | TBC      | Regulatory<br> conformance               |

Table of Contents

[Document Control......................................................................................................................... 2](#_Toc227302542)

[Version History............................................................................................................................... 2](#_Toc227302543)

[Design Authority Reviewers.......................................................................................................... 2](#_Toc227302544)

[Table of Contents........................................................................................................................... 3](#_Toc227302545)

[1. Introduction................................................................................................................................ 6](#_Toc227302546)

[1.1 Purpose..................................................................................................................................... 6](#_Toc227302547)

[1.2 Scope........................................................................................................................................ 6](#_Toc227302548)

[1.3 Audience.................................................................................................................................. 6](#_Toc227302549)

[1.4 Relationship to Other Documents......................................................................................... 6](#_Toc227302550)

[2. Solution Overview...................................................................................................................... 7](#_Toc227302551)

[2.1 Architectural Principles........................................................................................................... 7](#_Toc227302552)

[2.2 Logical Architecture................................................................................................................ 7](#_Toc227302553)

[2.3 Plane Responsibilities............................................................................................................. 8](#_Toc227302554)

[3. Technology Stack....................................................................................................................... 9](#_Toc227302555)

[3.1 Core Stack................................................................................................................................ 9](#_Toc227302556)

[3.2 Frontend Stack Detail............................................................................................................ 10](#_Toc227302557)

[3.3 Backend Stack Detail............................................................................................................ 10](#_Toc227302558)

[4. Agent Template Model............................................................................................................. 11](#_Toc227302559)

[4.1 Class Hierarchy...................................................................................................................... 11](#_Toc227302560)

[4.2 Template Anatomy................................................................................................................. 11](#_Toc227302561)

[4.3 Inheritance and Resolution
 Semantics............................................................................... 12](#_Toc227302562)

[4.4 Template Definition Example................................................................................................ 12](#_Toc227302563)

[4.5 Team Archetypes................................................................................................................... 13](#_Toc227302564)

[5. Component Architecture........................................................................................................ 14](#_Toc227302565)

[5.1 Control-Plane Services......................................................................................................... 14](#_Toc227302566)

[5.1.1 Template Service................................................................................................................ 14](#_Toc227302567)

[5.1.2 Team Service....................................................................................................................... 14](#_Toc227302568)

[5.1.3 Deployment Service........................................................................................................... 15](#_Toc227302569)

[5.1.4 Policy Service...................................................................................................................... 15](#_Toc227302570)

[5.1.5 Identity & RBAC Service..................................................................................................... 15](#_Toc227302571)

[5.1.6 Approvals / Workflow Service........................................................................................... 15](#_Toc227302572)

[5.1.7 Catalog Service.................................................................................................................. 15](#_Toc227302573)

[5.1.8 Tenancy / Jurisdictions
 Service......................................................................................... 16](#_Toc227302574)

[5.1.9 Audit & Evidence Service................................................................................................... 16](#_Toc227302575)

[5.1.10 Observability Service....................................................................................................... 16](#_Toc227302576)

[5.1.11 Cost & FinOps Service..................................................................................................... 16](#_Toc227302577)

[5.2 Agent Runtime Plane............................................................................................................. 16](#_Toc227302578)

[5.2.1 Per-Team Runtime Boundary............................................................................................. 17](#_Toc227302579)

[5.2.2 Agent Process Model......................................................................................................... 17](#_Toc227302580)

[5.2.3 Tool Adapter Pattern........................................................................................................... 18](#_Toc227302581)

[6. Data Model............................................................................................................................... 18](#_Toc227302582)

[6.1 Logical ERD............................................................................................................................ 18](#_Toc227302583)

[6.2 Physical Schema — Selected
 Tables................................................................................... 19](#_Toc227302584)

[6.2.1 agent_template.................................................................................................................. 19](#_Toc227302585)

[6.2.2 team..................................................................................................................................... 20](#_Toc227302586)

[6.2.3 team_member.................................................................................................................... 21](#_Toc227302587)

[6.2.4 deployment......................................................................................................................... 21](#_Toc227302588)

[6.2.5 run_instance (Cosmos DB
 container).............................................................................. 22](#_Toc227302589)

[6.2.6 agent_action (immutable audit,
 Event Hub + WORM blob)........................................... 22](#_Toc227302590)

[6.2.7 escalation........................................................................................................................... 23](#_Toc227302591)

[6.3 Retention and Residency...................................................................................................... 23](#_Toc227302592)

[7. API Specification...................................................................................................................... 23](#_Toc227302593)

[7.1 Conventions........................................................................................................................... 24](#_Toc227302594)

[7.2 Template Service (selected
 endpoints)............................................................................... 24](#_Toc227302595)

[7.3 Team Service.......................................................................................................................... 25](#_Toc227302596)

[7.4 Deployment Service.............................................................................................................. 25](#_Toc227302597)

[7.5 Supervisor Workbench API................................................................................................... 26](#_Toc227302598)

[7.6 Policy Service (runtime
 evaluation)..................................................................................... 26](#_Toc227302599)

[8. Agent Runtime Design............................................................................................................. 26](#_Toc227302600)

[8.1 Runtime Responsibilities...................................................................................................... 27](#_Toc227302601)

[8.2 End-to-End Case Flow — Client
 Onboarding...................................................................... 27](#_Toc227302602)

[8.3 Error Handling and Resilience.............................................................................................. 28](#_Toc227302603)

[8.4 Prompt and Model Governance........................................................................................... 28](#_Toc227302604)

[9. Security Architecture............................................................................................................... 28](#_Toc227302605)

[9.1 Identity.................................................................................................................................... 28](#_Toc227302606)

[9.2 Network.................................................................................................................................. 29](#_Toc227302607)

[9.3 Data Protection...................................................................................................................... 29](#_Toc227302608)

[9.4 Threat Model Summary......................................................................................................... 29](#_Toc227302609)

[9.5 Secure Development............................................................................................................. 30](#_Toc227302610)

[10. Identity, RBAC and Policy...................................................................................................... 30](#_Toc227302611)

[10.1 Role Model........................................................................................................................... 30](#_Toc227302612)

[10.2 Policy-as-Code.................................................................................................................... 31](#_Toc227302613)

[10.3 Segregation of Duties
 Enforcement................................................................................... 31](#_Toc227302614)

[11. Integration Patterns............................................................................................................... 31](#_Toc227302615)

[11.1 Outbound — Agents to Enterprise
 Systems...................................................................... 32](#_Toc227302616)

[11.2 Inbound — Triggers.............................................................................................................. 32](#_Toc227302617)

[11.3 Data Platform....................................................................................................................... 32](#_Toc227302618)

[12. Observability, Audit and
 Evidence....................................................................................... 32](#_Toc227302619)

[12.1 Telemetry.............................................................................................................................. 32](#_Toc227302620)

[12.2 Audit Data............................................................................................................................ 32](#_Toc227302621)

[12.3 Evidence Packs.................................................................................................................... 33](#_Toc227302622)

[12.4 Dashboards.......................................................................................................................... 33](#_Toc227302623)

[13. Azure Deployment Topology................................................................................................. 33](#_Toc227302624)

[13.1 Subscription and Resource
 Organisation......................................................................... 34](#_Toc227302625)

[13.2 High Availability................................................................................................................... 34](#_Toc227302626)

[13.3 Disaster Recovery................................................................................................................ 34](#_Toc227302627)

[13.4 Environments....................................................................................................................... 35](#_Toc227302628)

[14. DevSecOps and Delivery...................................................................................................... 35](#_Toc227302629)

[14.1 Repositories and Branching............................................................................................... 35](#_Toc227302630)

[14.2 Pipelines............................................................................................................................... 35](#_Toc227302631)

[14.3 AI-Assisted Development................................................................................................... 35](#_Toc227302632)

[14.4 Evaluation Harness............................................................................................................. 36](#_Toc227302633)

[15. Non-Functional Design Targets............................................................................................ 36](#_Toc227302634)

[16. Migration and Rollout............................................................................................................ 37](#_Toc227302635)

[16.1 Strangler Pattern for Existing
 Automations....................................................................... 37](#_Toc227302636)

[16.2 Release Gates...................................................................................................................... 37](#_Toc227302637)

[17. Open Technical Issues.......................................................................................................... 37](#_Toc227302638)

[18. Document End....................................................................................................................... 38](#_Toc227302639)

*(Right-click the table
above and select 'Update Field' in Word to populate the contents.)*

1. Introduction

1.1 Purpose

This Technical
Requirements Document (TRD) defines the architectural, technical and
operational specification for the Agentic Workforce Platform (the
"Platform"). It is the companion to the Business Requirements
Document (BRD) and translates business and functional requirements into an
engineering blueprint that architecture, security, data and engineering teams
can use to produce detailed designs and implement the Platform.

1.2 Scope

The TRD covers:
solution architecture; component responsibilities and interactions; the agent
runtime design on the Microsoft Agent Framework; the logical and physical data
models; identity, security and policy architecture; integration patterns;
observability and audit; DevSecOps; Azure deployment topology; and
non-functional design targets. It does not replace detailed component-level
design documents, which will be produced by each delivery squad.

1.3 Audience

1.                          Solution, security, data and infrastructure
architects.

2.                          Engineering leads and developers across
control-plane, runtime and UI squads.

3.                          DevSecOps, platform and site-reliability
engineers.

4.                          Risk, compliance and internal-audit reviewers
performing technical assurance.

1.4 Relationship to Other Documents

| **Document**                             | **Relationship**                                                                                        | **Owner**                  |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------- |
| BRD<br> v0.1                             | Source<br> of business, functional and non-functional requirements this TRD implements.                 | Business<br> Sponsor       |
| Group Enterprise Architecture Principles | Authoritative principles that constrain the Platform's<br> technical choices.                           | Group Architecture         |
| Azure<br> Landing Zone Standards         | Prescriptive<br> Azure patterns for subscription, network, identity, policy and logging.                | Cloud<br> Platform         |
| Information Security Standards           | Controls baseline including encryption, network, identity,<br> logging.                                 | Information Security       |
| Model<br> Risk Management Policy         | Governance<br> for model-based systems; the Platform and all agent templates register<br> against this. | CRO<br> Function           |
| Data Protection and Privacy Standards    | Personal data handling, residency, cross-border and DPIA<br> process.                                   | Data & Privacy Office      |
| SDLC /<br> DevSecOps Standard            | Pipeline,<br> branch, gate and release requirements.                                                    | Engineering<br> Enablement |

2. Solution Overview

2.1 Architectural Principles

5.                          Security-by-design: least privilege,
identity-centric, zero-trust assumptions across all planes.

6.                          Governed-by-design: every artefact (template,
team, deployment, action) is versioned, approvable and auditable.

7.                          Policy-as-code: authorisation, data residency
and tool-use constraints are expressed as code and enforced at runtime.

8.                          Agent-runtime agnostic through abstraction: the
Platform uses Microsoft Agent Framework, but interfaces are designed to limit
vendor lock-in.

9.                          Event-driven and loosely coupled: services
communicate via well-defined APIs and events; no shared database between
domains.

10.                    Multi-region and jurisdiction-aware: data
residency and regional processing are first-class concerns.

11.                    Observability-first: every service and every
agent action emits structured telemetry using OpenTelemetry.

12.                    Reversibility: all deployments can be rolled
back; all destructive actions require explicit approval.

13.                    AI-assisted development: the Platform is built
in VS Code with GitHub Copilot and related tools, following the Group's
secure-coding standards.

2.2 Logical Architecture

The Platform is
organised into five logical planes: the Presentation Plane, the Control Plane,
the Agent Runtime Plane, the Data & Model Foundation and a cross-cutting
DevSecOps & Operations Plane. Integration with Group systems is exclusively
via a governed Tool Adapter layer.

![](data:image/png;base64,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)

*Figure 1. High-level
architecture of the Agentic Workforce Platform.*

2.3 Plane Responsibilities

| **Plane**                  | **Responsibility**                                                                                                           | **Illustrative Components**                                                                                                    |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Presentation               | Human<br> interaction with the Platform by designers, approvers, supervisors,<br> risk/compliance and operators.             | Management<br> Console, Supervisor Workbench, Template Designer, Team Composer, BFF /<br> GraphQL.                             |
| Control Plane              | Source of truth for templates, teams, deployments,<br> policies, identities and audit; orchestrates lifecycle and approvals. | Template, Team, Deployment, Policy, Identity/RBAC,<br> Approvals, Catalog, Audit, Observability, Cost services.                |
| Agent<br> Runtime          | Executes<br> agent teams on the Microsoft Agent Framework in per-team isolated boundaries.                                   | AKS<br> agent workloads, tool adapters, orchestrator, state/memory stores, egress<br> brokers.                                 |
| Data & Model Foundation    | Durable stores, model endpoints and grounding corpora used<br> by the Platform and its agents.                               | Azure AI Foundry, AI Search, Azure SQL, Cosmos DB,<br> Blob/ADLS, Event Hub/Service Bus, Key Vault/HSM, Purview.               |
| DevSecOps<br> & Operations | Build,<br> test, deploy, operate and observe the Platform and the workloads it hosts.                                        | GitHub<br> Enterprise + Copilot, Azure DevOps, Bicep/Terraform, policy gates, evaluation<br> harness, Log Analytics, Sentinel. |

3. Technology Stack

3.1 Core Stack

| **Layer**                 | **Technology**                                                                                                           | **Rationale**                                                                                               |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------- |
| Agent<br> runtime         | Microsoft<br> Agent Framework (.NET); hosted on AKS (primary) and Azure Container Apps<br> (lighter workloads).          | Strategic<br> Microsoft alignment; managed messaging, state and tool invocation; strong<br> .NET ecosystem. |
| Foundation models         | Azure AI Foundry — approved model catalogue (OpenAI<br> family, Phi, Llama and specialist models as approved).           | Enterprise-grade hosting, content safety, data isolation<br> and contractual protections.                   |
| Retrieval<br> / grounding | Azure<br> AI Search with hybrid (vector + keyword) search.                                                               | Native<br> Azure, private networking, enterprise search features.                                           |
| Control-plane services    | .NET 9 / C# on AKS; REST + gRPC; Service Bus / Event Hub<br> for events.                                                 | Performance, typing, tooling, skills alignment.                                                             |
| Frontend                  | Next.js<br> (App Router) with TypeScript, TanStack Query, Tailwind CSS, Radix UI,<br> shadcn/ui patterns.                | Modern<br> fintech UX patterns; strong accessibility; fast iteration.                                       |
| API gateway               | Azure API Management (APIM), fronted by Azure Front Door<br> and WAF.                                                    | Group-standard; supports internal and federated access<br> patterns.                                        |
| Policy-as-code            | Open<br> Policy Agent (OPA) with Rego; Cedar considered for specific use-cases; policy<br> bundles signed and versioned. | Industry-standard;<br> high-performance runtime evaluation.                                                 |
| Relational                | Azure SQL Database (Hyperscale); Azure Database for<br> PostgreSQL (Flexible Server) for analytic metadata.              | HA, managed, well-understood by Group DBAs.                                                                 |
| NoSQL /<br> state         | Azure<br> Cosmos DB (NoSQL API) for agent state and high-volume telemetry projections.                                   | Elastic<br> throughput; multi-region replication.                                                           |
| Object / artefact         | Azure Blob Storage and ADLS Gen2; immutable (WORM)<br> containers for audit.                                             | Regulatory retention, lifecycle policies, WORM support.                                                     |
| Secrets<br> / keys        | Azure<br> Key Vault (Premium) and Azure Managed HSM for CMK.                                                             | Customer-managed<br> keys with strict key rotation.                                                         |
| Messaging                 | Azure Service Bus (command/response, work queues); Azure<br> Event Hub (high-volume events and audit stream).            | Reliability, ordering, dead-letter support.                                                                 |
| Identity                  | Microsoft<br> Entra ID, managed workload identities, PIM, Conditional Access.                                            | Group-standard<br> identity provider.                                                                       |
| Observability             | Azure Monitor, Log Analytics, Application Insights,<br> Managed Grafana, OpenTelemetry SDKs.                             | End-to-end tracing and metrics standard.                                                                    |
| SIEM /<br> SOAR           | Microsoft<br> Sentinel integrated with Defender for Cloud.                                                               | Group-standard<br> security analytics.                                                                      |
| DevSecOps                 | GitHub Enterprise Cloud + Copilot, Azure DevOps Pipelines,<br> Bicep and Terraform, Defender for DevOps, CodeQL.         | Developer productivity with governance and supply-chain<br> security.                                       |

3.2 Frontend Stack Detail

14.                    Framework: Next.js 15+ App Router; React Server
Components where beneficial.

15.                    Language: TypeScript (strict); ESLint + Prettier
enforced in CI.

16.                    State / data: TanStack Query for server state;
Zustand for small ephemeral stores.

17.                    Styling and components: Tailwind CSS with the
Group design system tokens; Radix UI primitives wrapped in shadcn/ui patterns.

18.                    Data visualisation: Recharts for time-series and
queue metrics; ECharts or Visx for complex views.

19.                    Realtime: SignalR / WebSocket push for live
dashboards and queue updates, backed by Azure SignalR Service.

20.                    Accessibility: WCAG 2.2 AA verified via Axe-core
in CI; keyboard-first interaction patterns.

21.                    Testing: Playwright for end-to-end, Vitest for
unit, Storybook with Chromatic for component visual regression.

3.3 Backend Stack Detail

22.                    .NET 9 / C# for Platform services and agent
host; ASP.NET Core Minimal APIs; gRPC for hot paths.

23.                    EF Core (relational) + Cosmos SDK (NoSQL);
MediatR for intra-service command/query patterns.

24.                    Serialisation: System.Text.Json; schema-first
public contracts via OpenAPI and AsyncAPI.

25.                    Messaging: Azure Service Bus and Event Hub;
transactional outbox pattern for consistency.

26.                    Testing: xUnit, Testcontainers for integration,
Pact for contract tests.

27.                    Packaging: multi-stage Docker; distroless base
images; SBOM via Syft; vulnerability scan via Trivy + Defender for Containers.

4. Agent Template Model

The templated
agent model is the core conceptual abstraction of the Platform. It enables
object-oriented style composition: agents inherit from base templates; teams
are composed from agents; teams bind to business functions, jurisdictions and
playbooks. This section defines the template model, inheritance semantics and
resolution order.

4.1 Class Hierarchy

The template
hierarchy follows a classical single-inheritance pattern. Multiple inheritance
is not supported — composition is achieved through skills and tools rather than
parent chains. This keeps resolution deterministic and auditable.

AgentTemplate
(abstract)

  ├─ SupervisorBase

  │     └─ onboarding.supervisor.uk

  │           └─ onboarding.supervisor.uk.privateclient

  ├─ MakerBase

  │     ├─ onboarding.idv.uk

  │     ├─ onboarding.screening.uk

  │     ├─ onboarding.capture.uk

  │     └─ payments.repair.sa

  ├─ QACheckerBase

  │     ├─ onboarding.qa.uk

  │     └─ payments.qa.sa

  ├─ ResearcherBase

  └─ ReporterBase

*Figure 2. Example
template class hierarchy.*

4.2 Template Anatomy

A template is a
declarative artefact (YAML, stored as a versioned record in the Template
Service). A template has the following sections:

| **Section**   | **Purpose**                                                                                                    | **Inherited by default?**                                   |
| ------------- | -------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| identity      | Template<br> ID, display name, version, base template reference, owning function.                              | Yes,<br> but overridden per concrete template.              |
| attributes    | Descriptive attributes (jurisdiction, risk tier, data<br> classification allowed, max autonomy level).         | Yes — merged with child overrides.                          |
| skills[]      | Named<br> skill references with configuration (prompt assets, model preferences,<br> temperature, max tokens). | Yes —<br> additive, with override on collision by skill_id. |
| tools[]       | Named tool references with scope (region, rate limits,<br> field-level ACLs).                                  | Yes — additive, with override on collision by tool_id.      |
| data_scopes[] | Declarative<br> data domains the agent may read/write (e.g., 'kyc.uk.standard').                               | Yes —<br> child may narrow but not widen.                   |
| guardrails    | Runtime policy references (e.g., 'platform.core',<br> 'aml.uk', 'consumer-duty.uk').                           | Yes — child may add but not remove.                         |
| supervision   | Required<br> human-approval gates, dual-control flags, escalation triggers.                                    | Yes —<br> child may strengthen; may not weaken.             |
| lifecycle     | Hooks for startup, pre-task, post-task, shutdown<br> (optional).                                               | Yes — override by hook name.                                |
| metadata      | Owner,<br> reviewer, tags, MRM classification.                                                                 | Yes.                                                        |

4.3 Inheritance and Resolution
Semantics

When a concrete
template is instantiated as part of a deployed team, the Platform computes the
effective definition by resolving the inheritance chain using these rules:

28.                    Resolution walks from the root base template to
the concrete template, applying each level's overrides.

29.                    Scalar fields: child wins.

30.                    Lists (skills, tools): merged by key; child
values override parent values for the same key.

31.                    data_scopes: child may intersect (narrow) but
may not extend beyond the parent's declared scopes. Any attempt to widen is a
validation error.

32.                    guardrails: additive and monotonic — child may
add guardrails; child may not remove parent guardrails.

33.                    supervision: child may require more approvals
than parent; child may never require fewer.

34.                    The resolved template is hashed and stored
alongside the team deployment; runtime behaviour is pinned to the resolved
snapshot, not the live template chain.

4.4 Template Definition Example

The following
illustrates a specialised UK onboarding screening agent inheriting from
MakerBase.

# onboarding.screening.uk.yaml

identity:

  template_id: onboarding.screening.uk

  version: 1.2.1

  display_name: UK Onboarding - Screening Agent

  base: maker.base

  owning_function: client_onboarding

attributes:

  jurisdiction: uk

  risk_tier: medium

  data_classification_max: confidential

  max_autonomy_level: assisted   # cannot raise above parent

skills:

  - id: reasoning.plan-and-check

    config: { model: gpt-4o-mini, temperature:
0.0 }

  - id: screening.summarise-hit

    config: { model: gpt-4o, max_tokens: 800 }

tools:

  - id: screening.sanctions.v3

    scope: { regions: [uk], rate_limit_per_min:
60 }

  - id: ai-search.playbooks

    scope: { indexes: [pb-uk-onboarding-v12] }

  - id: audit.write-event

    scope: { levels: [info, warn, critical] }

data_scopes:

  - kyc.uk.standard              # narrowed from parent

  - screening.uk.public

guardrails:

  - platform.core                # inherited

  - aml.uk                       # added by child

  - pii.redact-on-egress         # added by child

supervision:

  requires_qa: true

  human_approval_for:

    - adverse_media_escalation

    - pep_match_true_positive

  dual_control_for: []

metadata:

  mrm_classification: model-assisted

  owner: onboarding-uk@group

  tags: [onboarding, uk, screening, maker]

4.5 Team Archetypes

Teams are
instances of Team Archetypes — reusable structural patterns that define the set
of roles, minimum composition rules and default approval flows.

| **Archetype**                      | **Minimum Composition**                                                                                           | **Typical Use**                                                            |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Maker-Checker-Supervisor<br> (MCS) | 1<br> Supervisor, greater-than-or-equal 1 Maker, greater-than-or-equal 1 QA                                       | Default<br> for regulated operational processes; onboarding, payments ops. |
| Research-Report                    | 1 Supervisor, greater-than-or-equal 1 Researcher,<br> greater-than-or-equal 1 QA, 1 Reporter                      | Periodic reviews, credit file preparation, adverse media<br> sweeps.       |
| Monitor-Triage-Escalate            | 1<br> Supervisor, greater-than-or-equal 1 Monitor, greater-than-or-equal 1 Triage,<br> greater-than-or-equal 1 QA | Continuous<br> monitoring workloads (low-severity alert triage).           |
| Case-Investigator                  | 1 Supervisor, greater-than-or-equal 1 Investigator,<br> greater-than-or-equal 1 QA                                | Exception investigation: reconciliations, payment repairs.                 |

Team archetypes
are themselves versioned artefacts. A team definition references exactly one
archetype and specifies concrete agent templates for each required role.

5. Component Architecture

This section
specifies the responsibilities, interfaces and interactions of each Platform
component. Each component is an independently deployable service with a
documented public contract.

5.1 Control-Plane Services

5.1.1 Template Service

35.                    Owns the lifecycle of agent templates: create,
version, validate, submit-for-approval, publish, retire.

36.                    Computes and stores the resolved effective
template for every concrete template version.

37.                    Exposes read APIs for the Template Designer,
Team Service and Deployment Service.

38.                    Emits events on every status transition
(published, retired).

39.                    Persists templates in Azure SQL; large prompt
assets in Blob Storage, referenced by URI and content hash.

5.1.2 Team Service

40.                    Owns the lifecycle of Team Definitions: compose,
version, approve, retire.

41.                    Validates team-level structural rules (archetype
composition, jurisdiction/playbook bindings).

42.                    Computes aggregated team-level permissions, data
scopes and tool access for display and approval.

43.                    Depends on Template Service (resolved templates)
and Catalog Service (tools, skills).

5.1.3 Deployment Service

44.                    Orchestrates deployment of approved Team
Definitions into target environments (dev, test, UAT, prod).

45.                    Materialises the deployment into
infrastructure-as-code (Bicep / Terraform) and invokes Azure DevOps pipelines.

46.                    Provisions per-team runtime boundaries:
namespace, workload identity, secrets scope, log analytics collection rules,
network policies.

47.                    Records a Deployment entity with immutable
lineage to the exact Team Definition and resolved templates.

48.                    Supports rollback by re-deploying the previous
Deployment entity.

5.1.4 Policy Service

49.                    Stores versioned policy bundles expressed as
Rego (OPA) or Cedar; signs bundles at publish.

50.                    Exposes a runtime evaluation API: given a
(subject, action, resource, context) tuple, returns permit/deny plus rationale.

51.                    Caches compiled policy in-memory on agent hosts
via a sidecar policy agent.

52.                    Provides a 'what-if' API used by the Team
Composer to preview whether a composition passes policy.

5.1.5 Identity & RBAC Service

53.                    Holds the platform role model and
segregation-of-duties rules; integrates with Entra ID for authentication.

54.                    Enforces that the same user cannot
simultaneously hold conflicting roles (e.g., Designer and Approver on the same
artefact).

55.                    Provides role-assignment APIs for the Management
Console; writes to an audit stream on every change.

5.1.6 Approvals / Workflow Service

56.                    Manages human-approval workflows for templates,
teams, deployments and runtime actions.

57.                    Supports single and dual-control gates; routes
notifications via Teams and email.

58.                    Persists approval records with full lineage to
the subject artefact version.

5.1.7 Catalog Service

59.                    Holds the approved catalogues of Skills, Tools,
Policies and Playbooks.

60.                    Only approved catalogue items may be referenced
from templates; changes require governance approval.

5.1.8 Tenancy / Jurisdictions
Service

61.                    Represents legal entities, regions,
jurisdictions and their mappings.

62.                    Provides residency policy defaults used by
Policy Service and Deployment Service.

5.1.9 Audit & Evidence Service

63.                    Receives append-only audit events from all
services and agent runtimes.

64.                    Writes to Event Hub, materialises projections in
Cosmos DB and immutable artefacts in Blob (WORM).

65.                    Generates Evidence Packs on demand: PDF plus
signed manifest of supporting records.

5.1.10 Observability Service

66.                    Aggregates OpenTelemetry metrics, logs and
traces from all Platform services and agent runtimes.

67.                    Provides platform dashboards and pre-built
alerts aligned to operational KPIs.

5.1.11 Cost & FinOps Service

68.                    Ingests cost signals from Azure Cost Management,
Foundry usage and telemetry-derived model call counts.

69.                    Allocates cost to team, business unit and
jurisdiction; enforces budgets and throttles.

5.2 Agent Runtime Plane

Each deployed
team runs as an isolated workload on the Agent Runtime Plane. The runtime is
built on the Microsoft Agent Framework and hosted primarily on AKS. The
following diagram illustrates the internal topology of a team using the Client
Onboarding reference team as the example.

![](data:image/png;base64,/9j/4AAQSkZJRgABAQAAkACQAAD/4QCARXhpZgAATU0AKgAAAAgABQESAAMAAAABAAEAAAEaAAUAAAABAAAASgEbAAUAAAABAAAAUgEoAAMAAAABAAIAAIdpAAQAAAABAAAAWgAAAAAAAACQAAAAAQAAAJAAAAABAAKgAgAEAAAAAQAAAeugAwAEAAAAAQAAAUYAAAAA/+0AOFBob3Rvc2hvcCAzLjAAOEJJTQQEAAAAAAAAOEJJTQQlAAAAAAAQ1B2M2Y8AsgTpgAmY7PhCfv/AABEIAUYB6wMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAAAAQIDBAUGBwgJCgv/xAC1EAACAQMDAgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUmJygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4+fr/xAAfAQADAQEBAQEBAQEBAAAAAAAAAQIDBAUGBwgJCgv/xAC1EQACAQIEBAMEBwUEBAABAncAAQIDEQQFITEGEkFRB2FxEyIygQgUQpGhscEJIzNS8BVictEKFiQ04SXxFxgZGiYnKCkqNTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqCg4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2dri4+Tl5ufo6ery8/T19vf4+fr/2wBDAAICAgICAgMCAgMFAwMDBQYFBQUFBggGBgYGBggKCAgICAgICgoKCgoKCgoMDAwMDAwODg4ODg8PDw8PDw8PDw//2wBDAQICAgQEBAcEBAcQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/3QAEAB//2gAMAwEAAhEDEQA/AP5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9D+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8Aglx/yYp8M/8AuNf+ni9r+QKv6/f+CXH/ACYp8M/+41/6eL2gD//R/n/ooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAr+v3/AIJcf8mKfDP/ALjX/p4va/kCr+v3/glx/wAmKfDP/uNf+ni9oA//0v5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9P+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8Aglx/yYp8M/8AuNf+ni9r+QKv6/f+CXH/ACYp8M/+41/6eL2gD//U/n/ooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAr+v3/AIJcf8mKfDP/ALjX/p4va/kCr+v3/glx/wAmKfDP/uNf+ni9oA//1f5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9b+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8Aglx/yYp8M/8AuNf+ni9r+QKv6/f+CXH/ACYp8M/+41/6eL2gD//X/n/ooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAr+v3/AIJcf8mKfDP/ALjX/p4va/kCr+v3/glx/wAmKfDP/uNf+ni9oA//0P5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9H+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8Aglx/yYp8M/8AuNf+ni9r+QKv6/f+CXH/ACYp8M/+41/6eL2gD//S/n/ooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAr+v3/AIJcf8mKfDP/ALjX/p4va/kCr+v3/glx/wAmKfDP/uNf+ni9oA//0/5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9T+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8Aglx/yYp8M/8AuNf+ni9r+QKv6/f+CXH/ACYp8M/+41/6eL2gD//V/n/ooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAr+v3/AIJcf8mKfDP/ALjX/p4va/kCr+v3/glx/wAmKfDP/uNf+ni9oA//1v5/6KKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAK/r9/wCCXH/Jinwz/wC41/6eL2v5Aq/r9/4Jcf8AJinwz/7jX/p4vaAP/9f+f+iiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACiiigAooooAKKKKACv6/f8A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)

*Figure 3. Agent team
topology — Client Onboarding reference team.*

5.2.1 Per-Team Runtime Boundary

70.                    Dedicated Kubernetes namespace with strict
network policies (deny-by-default egress) and resource quotas.

71.                    Dedicated workload managed identity per team; no
identity sharing across teams.

72.                    Dedicated Key Vault scope; secrets are
team-scoped and key rotations are automated.

73.                    Dedicated Log Analytics data collection rules;
team logs are tagged with team_id and jurisdiction.

74.                    Dedicated egress broker — all external calls
traverse a team-specific sidecar that enforces allow-lists and policy.

5.2.2 Agent Process Model

75.                    Each agent instance runs as a lightweight
process within the team's runtime, scheduled by the Microsoft Agent Framework
orchestrator.

76.                    The Supervisor agent is pinned and highly
available; Maker agents scale horizontally on queue depth; QA agents scale on
review-ready signals.

77.                    Agents communicate via the framework's messaging
substrate (backed by Service Bus for durability at team boundaries).

78.                    State is persisted to a team-scoped Cosmos DB
container; memory is addressed by correlation ID.

5.2.3 Tool Adapter Pattern

Agents do not
call enterprise systems directly. Every external capability is exposed as a
policy-wrapped Tool Adapter — a small, well-contracted service that:

79.                    Authenticates as the team's workload identity
(never as the agent's model invoker).

80.                    Evaluates policy on every call — tool+data
scope+jurisdiction+context must satisfy the team's policy bundle.

81.                    Applies data minimisation — only the fields
required for the task are returned to the agent.

82.                    Redacts sensitive data in error responses and
logs; emits full structured telemetry for audit.

83.                    Enforces rate limits, circuit breakers and
idempotency keys.

6. Data Model

6.1 Logical ERD

The primary
entities of the control plane and their relationships are shown below.

*Figure 4. Logical Data
Model for the control plane.*

6.2 Physical Schema — Selected
Tables

The following
tables present the physical schema for the most important entities. These are
indicative; final DDL will be produced by the data engineering squad during
detailed design.

6.2.1 agent_template

| **Column**            | **Type**         | **Constraints** | **Notes**                                                  |
| --------------------- | ---------------- | --------------- | ---------------------------------------------------------- |
| template_id           | uniqueidentifier | PK part<br> 1   | Stable<br> identifier across versions.                     |
| version               | nvarchar(20)     | PK part 2       | Semantic version (e.g., 1.2.1).                            |
| display_name          | nvarchar(200)    | NOT<br> NULL    | Human-readable<br> name.                                   |
| base_template_id      | uniqueidentifier | FK self         | Optional parent template.                                  |
| base_template_version | nvarchar(20)     | FK self         | Pinned<br> parent version.                                 |
| role                  | nvarchar(40)     | NOT NULL        | Enum: supervisor \| maker \| qa \| researcher \| reporter. |
| owning_function       | nvarchar(80)     | NOT<br> NULL    | Business<br> function owner.                               |
| jurisdiction          | nvarchar(20)     | NULL            | ISO-3166 country code or 'global'.                         |
| attributes_json       | nvarchar(max)    | NOT<br> NULL    | JSON:<br> attributes section.                              |
| skills_json           | nvarchar(max)    | NOT NULL        | JSON array.                                                |
| tools_json            | nvarchar(max)    | NOT<br> NULL    | JSON<br> array.                                            |
| data_scopes_json      | nvarchar(max)    | NOT NULL        | JSON array.                                                |
| guardrails_json       | nvarchar(max)    | NOT<br> NULL    | JSON<br> array.                                            |
| supervision_json      | nvarchar(max)    | NOT NULL        | JSON object.                                               |
| resolved_hash         | char(64)         | NOT<br> NULL    | SHA-256<br> of resolved effective template.                |
| status                | nvarchar(20)     | NOT NULL        | Enum: draft \| in_review \| approved \| retired.           |
| created_by            | nvarchar(200)    | NOT<br> NULL    | Entra<br> OID of creator.                                  |
| created_at            | datetime2        | NOT NULL        | UTC timestamp.                                             |
| approved_by           | nvarchar(200)    | NULL            | Entra<br> OID of approver.                                 |
| approved_at           | datetime2        | NULL            | UTC timestamp.                                             |

6.2.2 team

| **Column**             | **Type**         | **Constraints** | **Notes**                                       |
| ---------------------- | ---------------- | --------------- | ----------------------------------------------- |
| team_id                | uniqueidentifier | PK part<br> 1   | Stable<br> identifier.                          |
| version                | int              | PK part 2       | Monotonic version per team_id.                  |
| display_name           | nvarchar(200)    | NOT<br> NULL    | Human-readable<br> team name.                   |
| archetype_id           | nvarchar(80)     | FK archetype    | e.g., maker-checker-supervisor.                 |
| business_unit          | nvarchar(80)     | NOT<br> NULL    | Owning<br> business unit.                       |
| function               | nvarchar(80)     | NOT NULL        | e.g., client_onboarding.                        |
| jurisdiction           | nvarchar(20)     | NOT<br> NULL    | Owning<br> jurisdiction.                        |
| playbook_ref           | nvarchar(200)    | NOT NULL        | Reference to authoritative playbook.            |
| accountable_supervisor | nvarchar(200)    | NOT<br> NULL    | Entra<br> OID of accountable human.             |
| status                 | nvarchar(20)     | NOT NULL        | Enum: draft \| approved \| deployed \| retired. |
| created_by             | nvarchar(200)    | NOT<br> NULL    |                                                 |
| created_at             | datetime2        | NOT NULL        |                                                 |
| approved_by            | nvarchar(200)    | NULL            |                                                 |
| approved_at            | datetime2        | NULL            |                                                 |

6.2.3 team_member

| **Column**       | **Type**         | **Constraints**       | **Notes**                                                |
| ---------------- | ---------------- | --------------------- | -------------------------------------------------------- |
| id               | uniqueidentifier | PK                    |                                                          |
| team_id          | uniqueidentifier | FK team               |                                                          |
| team_version     | int              | FK team               |                                                          |
| template_id      | uniqueidentifier | FK agent_template     |                                                          |
| template_version | nvarchar(20)     | FK<br> agent_template |                                                          |
| alias            | nvarchar(80)     | NOT NULL              | Within-team alias (e.g., 'idv-1').                       |
| role_in_team     | nvarchar(40)     | NOT<br> NULL          | supervisor<br> \| maker \| qa \| researcher \| reporter. |
| min_instances    | int              | NOT NULL              | Lower bound on runtime instances.                        |
| max_instances    | int              | NOT<br> NULL          | Upper<br> bound on runtime instances.                    |
| overrides_json   | nvarchar(max)    | NULL                  | Team-level overrides applied at composition.             |

6.2.4 deployment

| **Column**             | **Type**         | **Constraints**   | **Notes**                                |
| ---------------------- | ---------------- | ----------------- | ---------------------------------------- |
| deployment_id          | uniqueidentifier | PK                |                                          |
| team_id                | uniqueidentifier | FK team           |                                          |
| team_version           | int              | FK team           |                                          |
| environment            | nvarchar(20)     | NOT NULL          | dev \| test \| uat \| prod.              |
| region                 | nvarchar(40)     | NOT<br> NULL      | Azure<br> region.                        |
| azure_namespace        | nvarchar(120)    | NOT NULL          | AKS namespace.                           |
| workload_identity      | nvarchar(200)    | NOT<br> NULL      | Managed<br> identity principal ID.       |
| status                 | nvarchar(20)     | NOT NULL          | active \| paused \| retired.             |
| deployed_by            | nvarchar(200)    | NOT<br> NULL      |                                          |
| deployed_at            | datetime2        | NOT NULL          |                                          |
| prev_deployment_id     | uniqueidentifier | FK<br> deployment | For<br> rollback lineage.                |
| resolved_manifest_hash | char(64)         | NOT NULL          | SHA-256 of the full deployment manifest. |

6.2.5 run_instance (Cosmos DB
container)

Agent case runs
are high-volume and short-to-medium-lived. They are stored in a Cosmos DB
container partitioned by deployment_id.

{

  "id": "<run_id>",

  "deployment_id":
"<uuid>",           //
partition key

  "correlation_id":
"<uuid>",

  "case_ref":
"KYC-UK-2026-0001234",

  "status": "active",                   //
pending|active|done|failed|escalated

  "outcome": null,

  "started_at":
"2026-04-17T08:15:31Z",

  "completed_at": null,

  "cost_tokens": 8342,

  "cost_usd": 0.193,

  "agents_involved":
["supervisor","idv-1","screening-1","qa-1"],

  "tags":
["uk","onboarding","private-client"]

}

6.2.6 agent_action (immutable audit,
Event Hub + WORM blob)

Every action
taken by an agent — a tool call, a decision, a message to another agent — is
written to Event Hub, projected into Cosmos DB for query and archived to a WORM
blob for immutability.

{

  "action_id":
"<uuid>",

  "run_id": "<uuid>",

  "deployment_id":
"<uuid>",

  "agent_alias":
"screening-1",

  "template_ref":
"onboarding.screening.uk@1.2.1",

  "action_type":
"tool_call",           // or
message|decision|escalation

  "tool_id":
"screening.sanctions.v3",

  "input_hash":
"sha256:...",

  "output_hash":
"sha256:...",

  "policy_result":
"permit",

  "policy_trace": "aml.uk:allow,
residency:uk-ok",

  "duration_ms": 287,

  "timestamp":
"2026-04-17T08:15:34.212Z",

  "correlation_id":
"<uuid>"

}

6.2.7 escalation

| **Column**      | **Type**         | **Constraints** | **Notes**                                 |
| --------------- | ---------------- | --------------- | ----------------------------------------- |
| escalation_id   | uniqueidentifier | PK              |                                           |
| run_id          | uniqueidentifier | FK run_instance |                                           |
| action_id       | uniqueidentifier | NULL            | The<br> action that triggered escalation. |
| reason_code     | nvarchar(80)     | NOT NULL        | Machine-readable reason.                  |
| raised_at       | datetime2        | NOT<br> NULL    |                                           |
| decision        | nvarchar(20)     | NULL            | approve \| reject \| modify \| takeover.  |
| decided_by      | nvarchar(200)    | NULL            | Entra<br> OID.                            |
| second_approver | nvarchar(200)    | NULL            | For dual control.                         |
| decided_at      | datetime2        | NULL            |                                           |
| rationale       | nvarchar(max)    | NULL            | Mandatory for any non-approve outcome.    |

6.3 Retention and Residency

84.                    Control-plane metadata (templates, teams,
deployments): retained indefinitely; soft-deletes only.

85.                    Run instances and agent actions: retained per
jurisdictional requirements, minimum 7 years for UK/SA financial crime records;
archived to WORM blob after 90 days.

86.                    Personal data within run artefacts: retention
driven by the applicable records schedule; data-subject erasure handled via a
dedicated redaction pipeline that preserves audit completeness via hashes.

87.                    All customer personal data is stored in the
jurisdiction of the owning entity; cross-border access requires an explicit,
logged broker call.

7. API Specification

The Platform
exposes internal APIs between its services and external APIs through Azure API
Management. All APIs are OpenAPI 3.1 documented, versioned and contract-tested.
The following excerpt illustrates the control-plane API surface.

7.1 Conventions

88.                    Base URL:
https://platform-api.agentic.group.internal/v1

89.                    Authentication: Entra ID bearer tokens (human
users) or workload tokens (services); Conditional Access applies.

90.                    All responses: application/json; problem+json
for errors (RFC 7807).

91.                    Idempotency: mutating endpoints accept an
Idempotency-Key header; the service returns the original response for duplicate
keys within 24 hours.

92.                    Pagination: cursor-based; responses include
next_cursor when more data is available.

93.                    Filtering: simple filter= query parameter with
documented keys; no OData.

7.2 Template Service (selected
endpoints)

POST   /templates                      # Create draft template

GET    /templates                      # List, with
filter+cursor

GET    /templates/{id}/versions        # List all versions of a template

GET    /templates/{id}/versions/{ver}  # Get a specific version

GET    /templates/{id}/versions/{ver}/resolved   # Resolved effective template

POST   /templates/{id}/versions/{ver}:submit-for-approval

POST   /templates/{id}/versions/{ver}:approve

POST   /templates/{id}/versions/{ver}:retire

POST   /templates/{id}/versions/{ver}:validate   # Run validation only

Example — create draft

POST /templates

Content-Type:
application/yaml

Idempotency-Key:
9f3a...

identity:

  template_id: onboarding.screening.uk

  version: 1.2.2

  base: maker.base

...

201 Created

Location:
/templates/4b2e.../versions/1.2.2

{

  "template_id": "4b2e...",

  "version": "1.2.2",

  "status": "draft",

  "resolved_hash":
"sha256:...",

  "validation": { "passed":
true, "warnings": [] }

}

7.3 Team Service

POST   /teams                          # Compose new team
(draft)

GET    /teams                          # List with
filter+cursor

GET    /teams/{id}/versions/{ver}      # Get team definition

GET    /teams/{id}/versions/{ver}/aggregated-permissions

POST   /teams/{id}/versions/{ver}:approve

POST   /teams/{id}/versions/{ver}:retire

Example team
composition payload (abbreviated)

{

  "display_name": "UK Onboarding
— Private Client",

  "archetype_id":
"maker-checker-supervisor@1.0.0",

  "business_unit":
"wealth-uk",

  "function":
"client_onboarding",

  "jurisdiction": "uk",

  "playbook_ref":
"pb-uk-onboarding-v12",

  "accountable_supervisor":
"alice@group",

  "members": [

    { "alias":
"supervisor", "template":
"onboarding.supervisor.uk@1.3.0",

      "role": "supervisor",
"min": 1, "max": 1 },

    { "alias": "idv",
"template": "onboarding.idv.uk@1.1.0",

      "role": "maker",
"min": 1, "max": 4 },

    { "alias": "screening",
"template": "onboarding.screening.uk@1.2.1",

      "role": "maker",
"min": 1, "max": 2 },

    { "alias": "qa",
"template": "onboarding.qa.uk@1.1.0",

      "role": "qa",
"min": 1, "max": 2 }

  ]

}

7.4 Deployment Service

POST   /deployments                    # Deploy a team version

GET    /deployments                    # List with filter+cursor

GET    /deployments/{id}               # Get deployment

POST   /deployments/{id}:pause

POST   /deployments/{id}:resume

POST   /deployments/{id}:retire

POST   /deployments/{id}:rollback      # Roll back to prev deployment

POST   /deployments/{id}:kill-switch   # Immediate halt (heavy auth)

Example — request
deployment

POST /deployments

{

  "team_id": "8c01...",

  "team_version": 3,

  "environment": "uat",

  "region": "uksouth",

  "approvers":
["bob@group","carol@group"]

}

202 Accepted

Location:
/deployments/df31.../status

{ "deployment_id":
"df31...", "status": "provisioning" }

7.5 Supervisor Workbench API

GET    /queue                          # Items awaiting
supervisor action

GET    /queue/{item_id}                # Item detail: context,
evidence, proposal

POST   /queue/{item_id}:approve

POST   /queue/{item_id}:reject

POST   /queue/{item_id}:modify

POST   /queue/{item_id}:takeover

GET    /runs/{run_id}/timeline         # Full case timeline

GET    /runs/{run_id}/evidence-pack    # Generate evidence pack

Example queue item

{

  "item_id": "q-0001",

  "run_id": "9a2f...",

  "deployment_id":
"df31...",

  "team_name": "UK Onboarding —
Private Client",

  "case_ref":
"KYC-UK-2026-0001234",

  "reason_code":
"pep_match_possible",

  "priority": "high",

  "proposed_action": {
"type": "classify_as_pep_true_positive" },

  "evidence_refs":
["blob://evidence/.../match.json"],

  "awaiting_dual_control": false

}

7.6 Policy Service (runtime
evaluation)

POST /policy/evaluate

{

  "subject": { "team_id":
"8c01...", "agent_alias": "screening-1",

               "template":
"onboarding.screening.uk@1.2.1",

               "jurisdiction":
"uk" },

  "action":  { "type": "tool_call",
"tool_id": "screening.sanctions.v3" },

  "resource":{
"data_domain": "kyc.uk.standard",

               "fields":
["name","dob","country","address"] },

  "context": {
"region_processing": "uksouth",

               "time_utc":
"2026-04-17T08:15:34Z" }

}

200 OK

{

  "decision": "permit",

  "rationale":
["aml.uk:allow", "residency:uk-in-uk",
"rate-limit:ok"],

  "policy_bundle":
"platform.core@7,aml.uk@3,pii.redact-on-egress@2"

}

8. Agent Runtime Design

The Agent
Runtime Plane executes deployed teams on the Microsoft Agent Framework. This
section describes the runtime's responsibilities, an end-to-end flow for a
single case and the controls applied throughout.

8.1 Runtime Responsibilities

94.                    Orchestrate agent processes according to the
team definition (Supervisor decomposes; Makers execute; QA validates).

95.                    Provide durable messaging, state and memory via
the framework's managed primitives.

96.                    Mediate all tool calls through policy-wrapped
adapters; enforce egress controls.

97.                    Emit structured telemetry for every message,
tool call, decision and escalation.

98.                    Hand off to human supervisors through the
Workbench when escalations are triggered.

8.2 End-to-End Case Flow — Client
Onboarding

The following
illustrates a single onboarding case traversing the Client Onboarding reference
team.

1. Trigger

   Event 'case.onboarding.new' published to the
team's input queue,

   containing the case reference, applicant
data pointer and jurisdiction.

2. Supervisor Agent —
   plan

   - Loads playbook 'pb-uk-onboarding-v12' via
AI Search.

   - Decomposes the case into sub-tasks: IDV,
data capture, screening, risk rating.

   - Writes plan to state store; emits
'plan.created' telemetry.

3. Delegation to
   Makers

   - Supervisor dispatches tasks to idv-1,
capture-1, screening-1 in parallel.

   - Each Maker executes under its resolved
template; every tool call passes

     through the policy sidecar (Policy
Service) and Tool Adapter.

4. Maker execution
   (example: screening-1)

   - Prepares query payload from case data
(data minimisation applied).

   - Calls screening.sanctions.v3 adapter.

     - Adapter authenticates with team workload
identity.

     - Policy evaluated: subject+action+resource+context
-> permit.

     - Call made, PII redacted on egress to
third party.

   - Summarises matches; classifies as 'no hit'
| 'possible hit' | 'confirmed hit'.

   - If 'possible hit' or above, raises a
supervision ticket; does NOT self-classify.

   - Writes AgentAction audit record.

5. Risk Rating

   - rating-1 aggregates factors from makers +
playbook rules.

   - Produces risk rating; writes action
record.

6. QA agent

   - Pulls finalised outputs; runs control
tests (e.g., evidence completeness,

     document freshness, four-eyes on adverse
findings).

   - Raises issues if any control fails.

7. Completion or
   escalation

   - Clean path: Supervisor emits
'case.completed' with outcome package.

   - Escalation path: Supervisor routes to
Accountable Human Supervisor via

     the Workbench queue; case paused pending
human decision.

   - All events persisted to audit store with
correlation ID.

8.3 Error Handling and Resilience

99.                    Tool call failures use exponential backoff with
jitter; after N retries the task is escalated rather than silently failing.

100.             Model call rate limits and content filter trips
are treated as errors and logged; they do not result in silent fallbacks.

101.             Long-running cases checkpoint state after each
major step; resumption is supported after infrastructure events.

102.             Every step has a configurable deadline; deadline
expiry triggers escalation, not abandonment.

8.4 Prompt and Model Governance

103.             Prompt assets are stored in Blob Storage,
content-addressed by hash, and referenced from skills.

104.             Changes to prompts follow the same approval flow
as code — pull request, review, policy-as-code validation, governance approval
for high-impact changes.

105.             Model selection is constrained to the approved
catalogue in Azure AI Foundry; temperature defaults are low and bounded.

106.             Each model used is registered in the Group's
Model Risk Management inventory with classification, owner and review cadence.

9. Security Architecture

9.1 Identity

107.             Human users authenticate via Entra ID SSO with
Conditional Access (device compliance, location, risk).

108.             Platform services authenticate via managed
identities; no stored secrets for service-to-service calls.

109.             Each deployed team has its own managed identity;
agent instances assume this identity, not a personal or shared account.

110.             Privileged operations (template approval,
production deployment, kill-switch) require PIM-activated roles.

9.2 Network

111.             Hub-and-spoke VNet topology per region, aligned
to the Group landing zone standard.

112.             All Azure PaaS services accessed via Private
Endpoints; public endpoints disabled by policy.

113.             Egress from agent workloads traverses the team's
egress broker and an Azure Firewall with an allow-list of approved
destinations.

114.             No direct developer access to production
runtime; access via Azure Bastion with PIM + session recording.

9.3 Data Protection

115.             Encryption in transit: TLS 1.3 with approved
cipher suites.

116.             Encryption at rest: Azure-managed by default;
customer-managed keys (CMK) in Key Vault / Managed HSM for customer data and
audit data.

117.             Secrets rotation automated; no long-lived shared
secrets.

118.             Data minimisation and redaction at Tool Adapter
boundaries.

9.4 Threat Model Summary

| **Threat**             | **Vector**                                                           | **Mitigation**                                                                                                                                     |
| ---------------------- | -------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Prompt<br> injection   | Malicious<br> content in documents or external data fed to an agent. | Input<br> filtering; structured tool contracts; model-side instruction defences;<br> per-tool output validation; policy checks on every tool call. |
| Tool abuse             | Agent invokes a tool with unintended scope or parameters.            | Tool allow-list per template; parameter schema validation;<br> policy evaluation; egress allow-list.                                               |
| Data<br> exfiltration  | Sensitive<br> data leaked via agent output, logs or tool call.       | Redaction<br> on egress; WAF; DLP on audit stream; egress broker allow-list; tenant<br> isolation.                                                 |
| Privilege escalation   | Agent or user performs an action outside their role.                 | SoD enforced; PIM for privileged actions; policy-as-code;<br> dual-control on sensitive operations.                                                |
| Supply<br> chain       | Malicious<br> dependency in container or IaC.                        | Signed<br> builds; SBOM; Defender for DevOps; base-image pinning; internal registry with<br> signature verification.                               |
| Insider / rogue prompt | Insider modifies a prompt to change behaviour.                       | Pull request review; content-hash pinning; approval<br> workflow for prompt asset changes; audit of all asset updates.                             |
| Denial<br> of service  | Flood<br> of tasks drives cost or degrades service.                  | Rate<br> limits per tenant / team; quotas; circuit breakers; cost guardrails; WAF.                                                                 |
| Audit tampering        | Attempt to alter audit records.                                      | Append-only Event Hub; WORM blob; SIEM alerts on anomalous<br> audit patterns.                                                                     |

9.5 Secure Development

119.             All repositories enforce branch protection,
signed commits, mandatory reviews and CI gates.

120.             Secrets scanning, SAST (CodeQL), dependency
scanning (Defender for DevOps), container scanning (Trivy).

121.             Infrastructure-as-code is scanned with Checkov
and Azure Policy compliance checks.

122.             AI-assisted development via GitHub Copilot is
permitted under the Group's AI development standard, with block-on-license and
sensitive-data filters enabled.

10. Identity, RBAC and Policy

10.1 Role Model

| **Role**                 | **Key Permissions**                                                           | **SoD Conflicts**                                    |
| ------------------------ | ----------------------------------------------------------------------------- | ---------------------------------------------------- |
| Template<br> Designer    | Create,<br> edit, submit-for-approval templates.                              | Cannot<br> be Approver on own templates.             |
| Template Approver        | Approve or reject submitted templates.                                        | Cannot be Designer on same template.                 |
| Team<br> Designer        | Compose<br> and submit team definitions.                                      | Cannot<br> be Approver on own teams.                 |
| Team Approver            | Approve team definitions and deployments to UAT.                              | Cannot be Designer on same team.                     |
| Production<br> Approver  | Approve<br> deployments to production (PIM-activated).                        | Cannot<br> be Designer or Supervisor on same team.   |
| Accountable Supervisor   | Operate a deployed team via Workbench; approve runtime<br> actions.           | Cannot be Designer or Approver on own team.          |
| Auditor                  | Read-only<br> access to audit trail, evidence packs and governance artefacts. | None;<br> strictly read-only.                        |
| Platform Operator        | Observe and operate the Platform (not business teams).                        | No access to run artefact content.                   |
| Platform<br> Admin (PIM) | Emergency<br> administration including kill-switch.                           | Access<br> only via PIM with just-in-time elevation. |

10.2 Policy-as-Code

Policy is
expressed as signed Rego bundles managed by the Policy Service. Bundles are
composed by scope: global (platform.core), jurisdiction (aml.uk, aml.sa),
function (onboarding.core) and data-class (pii.redact-on-egress). A deployed
team's policy bundle is the composition of the scopes it is bound to.

Illustrative
policy snippet — jurisdictional residency enforcement:

package residency

default allow = false

allow {

  input.subject.jurisdiction == "uk"

  input.context.region_processing ==
"uksouth"

  not exfil_risk

}

exfil_risk {

  some f

  f := input.resource.fields[_]

  sensitive_field(f)

  not input.context.explicit_broker_call

}

sensitive_field(f) {

  f == "name_full"

} {

  f == "date_of_birth"

} {

  f == "passport_number"

}

10.3 Segregation of Duties
Enforcement

123.             Platform enforces SoD at assignment time (cannot
grant conflicting roles to same user for same scope) and at action time (the
user cannot both submit and approve).

124.             SoD rules themselves are policy artefacts —
versioned, approvable, auditable.

11. Integration Patterns

11.1 Outbound — Agents to Enterprise
Systems

125.             All agent calls to Group systems traverse Tool
Adapters running in Azure Container Apps, one adapter per external capability.

126.             Adapters are thin: authentication, policy
evaluation, parameter validation, data minimisation, structured logging, then
call the underlying system.

127.             Adapters are owned by the team that owns the
underlying system (federated ownership model), with the Platform providing an
adapter SDK and conformance tests.

11.2 Inbound — Triggers

128.             Event-driven: Service Bus topic subscriptions
per team for cases emitted by upstream systems (e.g., CRM publishes
'case.onboarding.new').

129.             Scheduled: Azure Functions timer invokes the
team's input queue (used by KYC refresh, reconciliations, reporting teams).

130.             On-demand: Management Console can enqueue a case
manually (with audit) for test or exception work.

11.3 Data Platform

131.             Platform publishes telemetry and audit data to
the Group data platform via Event Hub, with schema registry and contract tests.

132.             Reference data (e.g., jurisdiction codes,
playbook metadata) flows from the data platform into the Platform via
Purview-governed exports.

12. Observability, Audit and
    Evidence

12.1 Telemetry

133.             Every service and agent emits OpenTelemetry
traces, metrics and structured logs.

134.             Correlation ID propagates from case trigger
through every agent, tool call and downstream system call.

135.             Core metrics: task throughput, success rate,
escalation rate, queue depth, mean time to decision, cost per case, policy-deny
count.

136.             Custom spans on agent reasoning boundaries:
plan, delegate, tool-call, validate, summarise.

12.2 Audit Data

137.             Append-only Event Hub topic per region;
consumers project into queryable Cosmos DB and archive to WORM Blob.

138.             Audit records are signed with the region's
Managed HSM signing key; tamper-evidence is verifiable.

139.             Retention meets the strictest applicable
requirement; default 7 years.

12.3 Evidence Packs

140.             On-demand generation: input specifies team, time
range and scope; output is a PDF plus a signed manifest referencing all
underlying records.

141.             Standard content: team definition, resolved
templates, policy bundle, approvals, deployment lineage, case-level timelines,
agent actions, escalations, supervisor decisions, model inventory.

142.             Consumable by internal audit and regulators
without access to the live Platform.

12.4 Dashboards

143.             Executive: Group-wide team status, business-unit
KPIs, risk posture heatmap.

144.             Operations: queue depth, backlog, error rates,
open escalations, SLA attainment.

145.             Risk/Compliance: policy-deny trends, escalation
reason-code distribution, supervisor workload, dual-control compliance.

146.             FinOps: cost by team / BU / jurisdiction, trend
vs. budget, per-case cost.

13. Azure Deployment Topology

The Platform is
deployed in a multi-region, multi-jurisdiction topology aligned to the Group's
Azure landing zone standard. Primary regions for the initial rollout are UK
South (serving UK, Channel Islands and Switzerland per policy) and South Africa
North (serving South Africa and Mauritius per policy). Each region runs its own
control plane, agent runtime and data stores; cross-region replication is
limited to platform metadata and is never used for customer personal data.

*Figure 5. Azure
deployment topology.*

13.1 Subscription and Resource
Organisation

147.             Dedicated management group 'Agentic Platform'
under the Group's Azure hierarchy, with child management groups per
environment.

148.             Subscriptions per region per environment (e.g.,
'agentic-prod-uksouth', 'agentic-prod-saN').

149.             Hub VNet per region carrying ExpressRoute, Azure
Firewall, Private DNS, Bastion; spoke VNets for control plane, agent runtime
and data/integration.

150.             Each deployed team gets a dedicated AKS
namespace within the regional agent runtime cluster.

13.2 High Availability

151.             AKS clusters use availability zones; nodes
across at least 2 zones.

152.             Azure SQL Hyperscale with geo-replica in the
paired region (metadata only).

153.             Cosmos DB with zone redundancy; multi-region
writes only for cross-region metadata projections.

154.             Service Bus Premium (zone-redundant) and Event
Hub (Dedicated where volumes justify).

13.3 Disaster Recovery

155.             Active-passive across the region pair per NFR-06
(RTO 4 hours, RPO 15 minutes, production tier).

156.             Quarterly DR exercises with documented RTO/RPO
attainment.

157.             DR scope includes Platform services; customer
data is not replicated across jurisdictions by default.

13.4 Environments

| **Environment** | **Data**                          | **Purpose**                                                  | **Access**                                 |
| --------------- | --------------------------------- | ------------------------------------------------------------ | ------------------------------------------ |
| dev             | Synthetic<br> only                | Engineering<br> development of Platform and templates.       | Engineering<br> squads.                    |
| test            | Synthetic only                    | Automated test, contract tests, evaluation harness.          | CI / QA.                                   |
| uat             | De-identified<br> production-like | Business<br> acceptance of templates, teams and deployments. | Use-case<br> product owners + approvers.   |
| prod            | Live regulated data               | Production workloads.                                        | Strictly least-privilege; PIM for changes. |

14. DevSecOps and Delivery

14.1 Repositories and Branching

158.             Monorepo per domain (platform-services,
agent-runtime, ui, templates, infra) in GitHub Enterprise.

159.             Trunk-based development with short-lived
branches; protected main; signed commits required.

14.2 Pipelines

160.             Azure DevOps Pipelines orchestrate build, test,
scan and deploy.

161.             Gates: SAST (CodeQL), SCA, container scan, IaC
scan, policy-as-code validation, evaluation harness for agent changes.

162.             Promotion: dev -> test -> uat -> prod
with manual approval at uat and prod; prod requires dual approval.

14.3 AI-Assisted Development

163.             VS Code with GitHub Copilot (Business) is the
primary IDE pattern for engineers.

164.             Copilot configured with enterprise controls: no
training on Group code, content filters on, indexing restricted to approved
repositories.

165.             Developer productivity patterns (e.g.,
scaffolding a new Tool Adapter from template, generating Bicep for a new team
deployment) documented and promoted as internal playbooks.

14.4 Evaluation Harness

166.             Each agent template ships with a battery of test
scenarios (happy path, edge cases, known failure modes, adversarial prompts).

167.             Harness executes against a non-production
environment with synthetic data; results gated in CI.

168.             Regression on model or prompt change triggers
automatic policy review.

15. Non-Functional Design Targets

The following
design targets operationalise the NFRs from the BRD. Each is owned by a named
engineering lead and tracked against production telemetry from first go-live.

| **BRD NFR**              | **Target**                                                    | **How Measured**                                    |
| ------------------------ | ------------------------------------------------------------- | --------------------------------------------------- |
| NFR-01<br> Scale         | 100<br> teams, 10,000 active agent instances at steady state. | Load<br> testing per release; production telemetry. |
| NFR-02 UI latency        | p95 < 2s dashboard render under design load.                  | RUM + synthetic monitoring.                         |
| NFR-03<br> Task dispatch | p95<br> < 3s queue to first action.                           | Orchestrator<br> telemetry.                         |
| NFR-04 Elasticity        | Absorb 5x surge without manual action.                        | Game day / chaos test.                              |
| NFR-05<br> Availability  | 99.9%<br> monthly prod tier.                                  | Availability<br> tests + incident attribution.      |
| NFR-06 DR                | RTO 4h / RPO 15m.                                             | DR exercises quarterly.                             |
| NFR-08<br> Security      | Group<br> InfoSec baseline pass.                              | Annual<br> pen test + quarterly attestation.        |
| NFR-12 Residency         | 0 unauthorised cross-border flows.                            | Policy-deny metric + SIEM alerts.                   |
| NFR-19<br> Accessibility | WCAG<br> 2.2 AA.                                              | Axe-core<br> in CI + annual audit.                  |
| NFR-26 Observability     | 100% action coverage with correlation.                        | Audit sampling + coverage metric.                   |
| NFR-28<br> Cost          | Per-team<br> allocation with chargeback.                      | FinOps<br> dashboards.                              |

16. Migration and Rollout

16.1 Strangler Pattern for Existing
Automations

169.             Where existing RPA, workflow or point AI
solutions perform subsets of a target business process, the Platform will
onboard them incrementally.

170.             New teams are deployed in parallel to legacy
flows, routed a small share of traffic, then scaled up as confidence grows.

171.             Legacy components are retired only when the new
team demonstrably meets or exceeds performance and control baselines.

16.2 Release Gates

172.             G1 — Architecture acceptance (Design Authority).

173.             G2 — Security acceptance (threat model + pen
test).

174.             G3 — Data protection acceptance (DPIA +
residency validation).

175.             G4 — Model risk acceptance (MRM inventory +
evaluation harness).

176.             G5 — Business acceptance (UAT by use-case
product owner).

177.             G6 — Production readiness (runbooks, on-call,
SLO definitions).

178.             G7 — Regulatory awareness (Compliance sign-off,
regulator notification where required).

17. Open Technical Issues

The following
are open technical questions flagged for resolution during detailed design.
They do not block finalisation of this TRD for review but should be closed
before build start.

| **ID** | **Issue**                                                                                                                                                                                  | **Owner**                              | **Target Resolution**       |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------- | --------------------------- |
| OI-01  | Final<br> choice between OPA and Cedar for policy-as-code, including performance at<br> expected evaluation volumes.                                                                       | Security<br> Architecture              | Detailed<br> design phase   |
| OI-02  | Agent state durability in Microsoft Agent Framework for<br> very long-running cases (greater-than 24h); validate checkpointing semantics.                                                  | Principal AI Engineer                  | Prototype in Phase 1        |
| OI-03  | Multi-tenant<br> isolation strength required per jurisdiction; whether AKS namespace + network<br> policy is sufficient or dedicated clusters are required for specific<br> jurisdictions. | Cloud<br> Platform                     | Design<br> Authority review |
| OI-04  | Evidence pack digital signing authority — Group CA vs.<br> dedicated HSM; interaction with external audit requirements.                                                                    | Security Architecture + Internal Audit | Detailed design phase       |
| OI-05  | Exact<br> boundary between Platform FinOps and Group Cloud FinOps for chargeback.                                                                                                          | Cost<br> & FinOps                      | Phase 1                     |
| OI-06  | Fallback model strategy if primary Foundry model is<br> unavailable or degraded.                                                                                                           | Principal AI Engineer                  | Phase 1                     |

18. Document End

This document
is the initial draft of the Technical Requirements Document for the Agentic
Workforce Platform. It is intended as a working basis for review, challenge and
iteration with the Design Authority and delivery squads, in parallel with the
review of the companion BRD.

*Subsequent
revisions will incorporate review feedback, resolve the open technical issues
in Section 17 and expand component-level detail where the Design Authority
identifies gaps.*
