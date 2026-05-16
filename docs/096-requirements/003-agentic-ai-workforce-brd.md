<style>
</style>

**BUSINESS
REQUIREMENTS DOCUMENT**

**Agentic Workforce Platform**

*A Framework for Deploying, Managing and Governing Digital Worker
Teams in Financial Services*

**Document Type:** Business Requirements Document
(BRD)

**Version:** 0.1 — Initial
Draft

**Status:** Draft for Review

**Date:** April 2026

**Classification:** Internal
— Confidential

# Document Control

### Version History

| **Version** | **Date**       | **Author**           | **Summary of Changes**                |
| ----------- | -------------- | -------------------- | ------------------------------------- |
| 0.1         | April<br> 2026 | L.<br> [Lead Author] | Initial<br> draft for internal review |

### Reviewers and Approvers

| **Role**                     | **Name** | **Responsibility**                           |
| ---------------------------- | -------- | -------------------------------------------- |
| Executive<br> Sponsor        | TBC      | Strategic<br> alignment and funding          |
| Group CTO                    | TBC      | Technology direction approval                |
| Group<br> COO                | TBC      | Operational<br> readiness approval           |
| Chief Risk Officer           | TBC      | Risk and control sign-off                    |
| Chief<br> Compliance Officer | TBC      | Regulatory<br> sign-off across jurisdictions |
| Head of Information Security | TBC      | Security architecture approval               |
| Head of<br> Data & Privacy   | TBC      | Data<br> governance and privacy approval     |
| Business Unit Heads          | TBC      | Use-case and business benefit validation     |

# Table of Contents

[Document Control............................................................................................................................. 2](#_Toc227302473)

[Version History........................................................................................................................... 2](#_Toc227302474)

[Reviewers and Approvers............................................................................................................ 2](#_Toc227302475)

[Table of Contents............................................................................................................................... 3](#_Toc227302476)

[1. Executive Summary........................................................................................................................ 6](#_Toc227302477)

[2. Business Context and
 Drivers.......................................................................................................... 6](#_Toc227302478)

[2.1 Strategic Context...................................................................................................................... 6](#_Toc227302479)

[2.2 Business Drivers........................................................................................................................ 7](#_Toc227302480)

[2.3 Strategic Alignment.................................................................................................................. 7](#_Toc227302481)

[2.4 Opportunity Statement............................................................................................................. 7](#_Toc227302482)

[3. Business Objectives
 and Success Criteria......................................................................................... 7](#_Toc227302483)

[3.1 Business Objectives................................................................................................................... 7](#_Toc227302484)

[3.2 Success Criteria and
 Key Performance Indicators........................................................................ 8](#_Toc227302485)

[Business Value............................................................................................................................ 8](#_Toc227302486)

[Operational Performance............................................................................................................ 8](#_Toc227302487)

[Risk and Control......................................................................................................................... 8](#_Toc227302488)

[Adoption.................................................................................................................................... 9](#_Toc227302489)

[3.3 Out of Scope............................................................................................................................. 9](#_Toc227302490)

[4. Stakeholders.................................................................................................................................. 9](#_Toc227302491)

[5. Scope of the Platform................................................................................................................... 10](#_Toc227302492)

[5.1 In Scope................................................................................................................................. 10](#_Toc227302493)

[5.2 Scope by Release.................................................................................................................... 11](#_Toc227302494)

[5.3 Out of Scope
 (Confirmed)........................................................................................................ 11](#_Toc227302495)

[6. Business Requirements................................................................................................................. 11](#_Toc227302496)

[6.1 Digital Worker Team
 Lifecycle................................................................................................. 11](#_Toc227302497)

[6.2 Agent Template and
 Role Definition........................................................................................ 12](#_Toc227302498)

[6.3 Human Supervision and
 Oversight........................................................................................... 12](#_Toc227302499)

[6.4 Governance, Risk and
 Control.................................................................................................. 13](#_Toc227302500)

[6.5 Operations and
 Monitoring..................................................................................................... 14](#_Toc227302501)

[6.6 Client, Product and
 Process Scope for Reference Teams........................................................... 14](#_Toc227302502)

[7. Functional
 Requirements.............................................................................................................. 14](#_Toc227302503)

[7.1 Agent Template
 Management................................................................................................. 15](#_Toc227302504)

[7.2 Team Composition.................................................................................................................. 15](#_Toc227302505)

[7.3 Deployment........................................................................................................................... 15](#_Toc227302506)

[7.4 Runtime Orchestration............................................................................................................ 16](#_Toc227302507)

[7.5 Management Console............................................................................................................. 16](#_Toc227302508)

[7.6 Supervisor Workbench............................................................................................................ 17](#_Toc227302509)

[7.7 Observability, Audit
 and Reporting.......................................................................................... 17](#_Toc227302510)

[7.8 Identity, Access and
 Policy....................................................................................................... 18](#_Toc227302511)

[8. Non-Functional
 Requirements....................................................................................................... 18](#_Toc227302512)

[8.1 Performance and
 Scalability.................................................................................................... 18](#_Toc227302513)

[8.2 Availability and
 Resilience....................................................................................................... 19](#_Toc227302514)

[8.3 Security.................................................................................................................................. 19](#_Toc227302515)

[8.4 Privacy and Data
 Protection.................................................................................................... 19](#_Toc227302516)

[8.5 Compliance and
 Regulatory..................................................................................................... 20](#_Toc227302517)

[8.6 Usability and
 Accessibility....................................................................................................... 20](#_Toc227302518)

[8.7 Maintainability and
 Extensibility.............................................................................................. 20](#_Toc227302519)

[8.8 Observability.......................................................................................................................... 21](#_Toc227302520)

[8.9 Cost....................................................................................................................................... 21](#_Toc227302521)

[9. Business Processes
 Supported....................................................................................................... 21](#_Toc227302522)

[9.1 Client Onboarding
 (Reference Team)....................................................................................... 21](#_Toc227302523)

[9.2 Payments Operations
 (Reference Team).................................................................................. 21](#_Toc227302524)

[9.3 Future Teams
 (Illustrative)...................................................................................................... 21](#_Toc227302525)

[10. Assumptions,
 Dependencies and Constraints............................................................................... 22](#_Toc227302526)

[10.1 Assumptions......................................................................................................................... 22](#_Toc227302527)

[10.2 Dependencies....................................................................................................................... 22](#_Toc227302528)

[10.3 Constraints........................................................................................................................... 23](#_Toc227302529)

[11. Risks and Mitigations.................................................................................................................. 23](#_Toc227302530)

[12. Benefits Realisation.................................................................................................................... 24](#_Toc227302531)

[12.1 Benefit Categories................................................................................................................ 24](#_Toc227302532)

[12.2 Benefits Tracking.................................................................................................................. 25](#_Toc227302533)

[13. Regulatory
 Considerations.......................................................................................................... 25](#_Toc227302534)

[13.1 Cross-cutting Themes............................................................................................................ 26](#_Toc227302535)

[14. Implementation
 Approach.......................................................................................................... 26](#_Toc227302536)

[14.1 Delivery Principles................................................................................................................ 26](#_Toc227302537)

[14.2 Delivery Roadmap................................................................................................................. 26](#_Toc227302538)

[14.3 Organisation......................................................................................................................... 27](#_Toc227302539)

[15. Glossary..................................................................................................................................... 27](#_Toc227302540)

[16. Document End........................................................................................................................... 28](#_Toc227302541)

*(Right-click the table
above and select 'Update Field' in Word to populate the contents.)*

# 1. Executive Summary

The financial
services industry is entering a period of accelerated transformation driven by
agentic AI — systems in which autonomous software agents, supervised by humans,
execute multi-step business processes end-to-end. Peer institutions are already
piloting agentic architectures for onboarding, payments, compliance, and
servicing; the window to establish a differentiated, enterprise-grade platform
is now.

This document
defines the business requirements for the Agentic Workforce Platform (the
"Platform"), a strategic capability that will enable the Group to
design, deploy, operate and govern teams of digital workers (AI agents) across
its banking, wealth and advisory businesses. The Platform is conceived as a
reusable internal product — not a single-use automation — built on the
Microsoft Agent Framework and hosted in the Group's Azure environment.

The Platform
introduces a templated, object-oriented agent model: each agent inherits a role
definition (attributes, permissions, skills, tools, data access scopes and
regulatory constraints) from a function-aligned template. Agent teams (e.g.,
client onboarding, payments operations, KYC refresh) are composed from these
templates and deployed as isolated, auditable workloads. A modern fintech-style
management console provides human supervisors with the oversight, intervention
and evidence-gathering tools required to operate these teams safely within the
Group's risk appetite and across its regulated jurisdictions (UK, South Africa,
Channel Islands, Switzerland, India, Mauritius, UAE).

The business
outcomes targeted are: (i) material reduction in cycle time and unit cost for
high-volume, rules-bound operational processes; (ii) improved consistency and
auditability of regulated outcomes; (iii) a scalable foundation that allows
business units to stand up new digital worker teams in weeks rather than
months; and (iv) a governance-first posture that positions the Group favourably
with regulators and external auditors as agentic AI matures.

Approval is
sought to proceed to detailed design and a funded delivery programme structured
around an initial 12–18 month horizon, starting with two reference teams
(Client Onboarding and Payments Operations) and expanding to a Platform
general-availability release.

# 2. Business Context and Drivers

## 2.1 Strategic Context

The Group
operates across multiple regulated jurisdictions with differing supervisory
regimes, operational processes and product sets. Despite substantial investment
in workflow, RPA and point AI solutions, many front-to-back processes remain
labour-intensive, fragmented across systems, and sensitive to operational
variability. The Group has already invested in foundational enterprise AI
tooling; agentic AI represents the next evolution — from copilots that assist
individuals to digital workers that execute processes.

## 2.2 Business Drivers

•    Cost-to-serve pressure in high-volume operational
functions (onboarding, payments, reconciliations, servicing) where
headcount-linear cost curves are no longer sustainable.

•    Regulatory complexity requiring consistent, auditable
execution of jurisdiction-specific playbooks — particularly KYC/CDD, sanctions
screening, payments controls and client reporting.

•    Talent constraints and attrition in operational roles,
and an increasing expectation from the workforce that routine tasks are
automated.

•    Client experience expectations: faster onboarding,
near-real-time servicing, and proactive issue resolution.

•    Competitive pressure from peer institutions and fintech
entrants deploying agentic automation at scale.

•    Regulator and board expectations that the firm can
evidence control, explainability and human oversight of any AI system that acts
on customer or transactional data.

## 2.3 Strategic Alignment

The Platform
directly supports the Group's published technology and operating model
priorities, specifically: modernising the operating model, scaling AI
responsibly, deepening Azure-native capability, and strengthening
multi-jurisdictional control. It complements, rather than replaces, existing
investments in core banking, data platform modernisation, and enterprise
copilots.

## 2.4 Opportunity Statement

A standardised,
governed platform for digital worker teams allows the Group to industrialise
agentic automation. Rather than each business unit commissioning bespoke agent
solutions — which would create control gaps, duplication and regulatory
exposure — the Platform provides a single, auditable substrate for deploying,
running and supervising digital workers enterprise-wide. This is analogous to
the shift from bespoke integrations to an enterprise integration platform a
decade ago.

# 3. Business Objectives and Success Criteria

## 3.1 Business Objectives

| **ID** | **Requirement**                                                                                                                                                      | **Priority** |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| BO-01  | Deliver<br> a reusable enterprise platform that enables business units to compose, deploy<br> and operate teams of AI agents aligned to specific business functions. | Must         |
| BO-02  | Reduce cycle time for targeted operational processes<br> (initial focus: client onboarding, payments operations) by at least 50%<br> against current baseline.       | Must         |
| BO-03  | Reduce<br> unit cost of targeted processes by at least 30% on a fully-loaded basis over<br> 24 months from go-live.                                                  | Must         |
| BO-04  | Improve first-time-right rates and reduce QA rework across<br> targeted processes by at least 40%.                                                                   | Should       |
| BO-05  | Establish<br> a defensible, regulator-ready governance model for agentic AI that<br> demonstrably meets obligations across all operating jurisdictions.              | Must         |
| BO-06  | Enable a new digital worker team to be designed, deployed<br> and operating in a non-production environment within 4 weeks of use-case<br> approval.                 | Should       |
| BO-07  | Provide<br> the executive with a single, consolidated view of all agentic work in flight<br> across the Group, with traceable performance and risk metrics.          | Must         |
| BO-08  | Maintain or improve employee experience in affected<br> operational roles through augmentation, upskilling and meaningful supervisory<br> work.                      | Should       |

## 3.2 Success Criteria and Key Performance Indicators

Success will be
measured across four lenses: business value, operational performance, risk and
control, and adoption.

### Business Value

•    Cumulative operational cost avoidance realised over 24
months from first production go-live.

•    Revenue uplift from improved conversion or retention
tied to faster onboarding and servicing.

•    Reduction in fully-loaded cost per transaction for
in-scope processes.

### Operational Performance

•    End-to-end cycle time for targeted processes (median
and 95th percentile).

•    Straight-through processing rate (percentage of cases
completed without human intervention).

•    Agent task success rate (percentage of tasks completed
without escalation or rollback).

•    Mean time to detect and mean time to remediate agent
incidents.

### Risk and Control

•    Zero regulatory breaches attributable to agentic
processing.

•    100% of agent actions producing an immutable, queryable
audit trail.

•    Human supervisor approval recorded on 100% of defined
high-risk actions.

•    All models and agents covered by the Group's Model Risk
Management inventory.

### Adoption

•    Number of business functions with at least one digital
worker team in production.

•    Number of active agent templates in the Group
catalogue.

•    Time from use-case idea to first deployed
non-production team.

•    Supervisor satisfaction (measured quarterly).

## 3.3 Out of Scope

To manage
expectations and scope, the following are explicitly out of scope for the
initial Platform release:

•    Fully autonomous agents acting without any form of
human oversight on customer-facing or transactional processes.

•    Customer-facing conversational agents (chatbots) —
these remain with the Group's existing conversational AI programme.

•    Training or hosting of proprietary foundation models —
the Platform will consume approved models via Azure AI Foundry.

•    Replacement of the Group's existing workflow, BPM, RPA
or core banking platforms.

•    Agent capabilities for trading, market-making or
discretionary investment decisions.

# 4. Stakeholders

The Platform
touches a wide set of executive, business, control and technology stakeholders.
Effective engagement across all of these is critical to delivery and adoption.

| **Stakeholder Group**                 | **Interest in the Platform**                                       | **Primary Engagement**               |
| ------------------------------------- | ------------------------------------------------------------------ | ------------------------------------ |
| Group<br> Executive Committee         | Strategic<br> investment, risk appetite, value realisation.        | Quarterly<br> steering review.       |
| Group CTO & CIO community             | Architecture alignment, delivery ownership, Azure<br> footprint.   | Platform design authority.           |
| Group<br> COO & Operations Heads      | Primary<br> consumers; process transformation, workforce planning. | Use-case<br> sponsors.               |
| Chief Risk Officer function           | Operational, model, conduct and third-party risk.                  | Risk design authority.               |
| Chief<br> Compliance Officer function | Regulatory<br> compliance across jurisdictions.                    | Regulatory<br> approval gates.       |
| Information Security                  | Identity, secrets, data protection, threat modelling.              | Security design authority.           |
| Data<br> & Privacy Office             | Data<br> classification, lineage, DPIA, cross-border flows.        | Data<br> governance approval.        |
| Internal Audit                        | Independent assurance over controls and evidence.                  | Audit observer and reviewer.         |
| Human<br> Resources                   | Role<br> redesign, upskilling, change management.                  | Workforce<br> transition lead.       |
| Business Unit Heads                   | Business outcomes, cost and service improvements.                  | Value owners per use-case.           |
| Front-line<br> Operations staff       | Day-to-day<br> supervisors of digital workers.                     | User<br> research and co-design.     |
| Regulators                            | Visibility of AI systems acting on regulated activities.           | Proactive engagement via Compliance. |
| External<br> Auditors                 | Evidence<br> of controls and reporting accuracy.                   | Evidence<br> walkthroughs.           |

# 5. Scope of the Platform

## 5.1 In Scope

The Platform in
scope for this BRD comprises the following logical components:

•    Agent Template Library — a versioned catalogue of
reusable agent archetypes (e.g., Supervisor, Maker, Checker/QA, Researcher,
Reporter) with inheritable attributes, skills, tools and policies.

•    Team Composer — a configuration-driven capability
allowing authorised designers to compose digital worker teams from templates
and bind them to specific business functions, playbooks and jurisdictions.

•    Deployment Engine — automated, policy-gated deployment
of composed teams into the Group's Azure environment, with environment
promotion (dev / test / UAT / prod).

•    Runtime Orchestration — a managed execution environment
for agents based on the Microsoft Agent Framework, providing messaging, state,
tool invocation, memory and supervisor hand-off.

•    Management Console — a modern web UI for designing,
configuring, deploying, monitoring and governing digital worker teams.

•    Supervisor Workbench — a dedicated operational UI for
human supervisors to oversee live teams, review escalations, approve high-risk
actions and manage queues.

•    Observability and Audit Services — end-to-end tracing,
conversation capture, evidence store and regulator-ready reporting.

•    Integration Layer — secure, governed connectivity to
Group systems (core banking, CRM, case management, document stores, screening,
ticketing), exposed via policy-wrapped tools.

•    Governance Services — policy engine, role and
permission management, model and agent inventory integration, and approvals
workflow.

## 5.2 Scope by Release

Scope is phased
to manage delivery risk and enable early realisation of value.

| **Release**                   | **Scope Summary**                                                                                                                                                                                                  | **Indicative Timing** |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------- |
| R1 —<br> Foundation           | Platform<br> core; template library (Supervisor, Maker, QA); Team Composer; deployment<br> engine; Management Console (minimum viable); observability; reference team:<br> Client Onboarding (pilot jurisdiction). | Months<br> 0–6        |
| R2 — Expansion                | Supervisor Workbench; Payments Operations reference team;<br> second jurisdiction onboarding pilot; enhanced policy engine; external audit<br> evidence pack.                                                      | Months 6–12           |
| R3 —<br> General Availability | Self-service<br> team creation for accredited designers; full multi-jurisdictional rollout;<br> catalogue of advanced templates; marketplace of approved tools; Platform SLAs<br> formalised.                      | Months<br> 12–18      |

## 5.3 Out of Scope (Confirmed)

See Section
3.3.

# 6. Business Requirements

Business
Requirements (BR-xx) describe what the Platform must deliver for the business,
independent of technology. They are prioritised using MoSCoW (Must / Should /
Could / Won't-for-now).

## 6.1 Digital Worker Team Lifecycle

| **ID** | **Requirement**                                                                                                                                                                                            | **Priority** |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| BR-01  | The<br> Platform must enable authorised designers to compose a digital worker team<br> for a specified business function by selecting from a catalogue of approved<br> agent templates.                    | Must         |
| BR-02  | The Platform must enforce that every team has at least one<br> designated Supervisor agent and at least one QA/Checker agent for any team<br> operating on regulated processes.                            | Must         |
| BR-03  | The<br> Platform must allow teams to be bound to a specific business unit,<br> jurisdiction and regulatory playbook at composition time, and must prevent<br> deployment if required bindings are missing. | Must         |
| BR-04  | The Platform must support a full team lifecycle: design,<br> review, approve, deploy, operate, pause, retire and archive — each stage with<br> defined approvals and evidence.                             | Must         |
| BR-05  | The<br> Platform must provide a non-production sandbox environment in which a team<br> can be exercised against synthetic or de-identified data before promotion to<br> production.                        | Must         |
| BR-06  | The Platform should enable a designer to clone and adapt<br> an existing approved team as the starting point for a new team.                                                                               | Should       |
| BR-07  | The<br> Platform could provide a marketplace of approved tools and skills that<br> templates can consume.                                                                                                  | Could        |

## 6.2 Agent Template and Role Definition

| **ID** | **Requirement**                                                                                                                                                                                    | **Priority** |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| BR-08  | Agent<br> templates must expose a declarative definition of: identity, role, inherited<br> attributes, permitted tools, permitted data domains, prompt/policy assets and<br> operating guardrails. | Must         |
| BR-09  | Templates must support inheritance — a specialised<br> template (e.g., 'UK Onboarding Maker') can inherit from a base template<br> ('Maker') and selectively override properties.                  | Must         |
| BR-10  | Templates<br> must be versioned; deployed teams reference a specific, immutable template<br> version.                                                                                              | Must         |
| BR-11  | Templates must be approved by a defined governance body<br> before being made available for use in production-bound teams.                                                                         | Must         |
| BR-12  | Templates<br> must support jurisdiction-specific variants (e.g., UK, SA, Channel Islands)<br> without forking the base template.                                                                   | Must         |
| BR-13  | The Platform should provide a test harness to exercise a<br> template against a battery of scenarios prior to promotion.                                                                           | Should       |

## 6.3 Human Supervision and Oversight

| **ID** | **Requirement**                                                                                                                                                                            | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| BR-14  | Every<br> deployed team must have a named human Accountable Supervisor with the<br> authority and tooling to pause, override or intervene in any agent action.                             | Must         |
| BR-15  | The Platform must support configurable human-in-the-loop<br> approval gates on defined categories of action (e.g., high-value payments,<br> adverse KYC decisions, client communications). | Must         |
| BR-16  | The<br> Supervisor Workbench must present the supervisor with a prioritised queue of<br> items requiring attention, with clear context and recommended actions.                            | Must         |
| BR-17  | The Platform must allow a supervisor to take over a case<br> from an agent, annotate the reason, and return it (or not) to the team.                                                       | Must         |
| BR-18  | The<br> Platform must record all supervisor decisions, overrides and interventions<br> with user identity, timestamp and rationale, and make these records<br> queryable.                  | Must         |
| BR-19  | The Platform should support a four-eyes (dual-control)<br> mode for defined sensitive actions, requiring two supervisors to approve.                                                       | Should       |

## 6.4 Governance, Risk and Control

| **ID** | **Requirement**                                                                                                                                                                                     | **Priority** |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| BR-20  | The<br> Platform must integrate with the Group's Model Risk Management framework,<br> ensuring every agent template and each deployed team is registered,<br> classified and periodically reviewed. | Must         |
| BR-21  | The Platform must enforce role-based access control<br> aligned with the Group's identity model, including segregation-of-duties<br> between designers, approvers, supervisors and auditors.        | Must         |
| BR-22  | The<br> Platform must provide an immutable, end-to-end audit trail covering team<br> lifecycle events, agent decisions, tool invocations, data accesses and<br> supervisor actions.                 | Must         |
| BR-23  | The Platform must support jurisdiction-specific controls,<br> including data residency, cross-border flow restrictions and local regulatory<br> reporting requirements.                             | Must         |
| BR-24  | The<br> Platform must provide, on demand, a regulator-ready evidence pack for any<br> deployed team covering a specified time period.                                                               | Must         |
| BR-25  | The Platform must support a kill-switch capability to<br> immediately halt all agent activity in a specified team, business unit or<br> across the Group.                                           | Must         |
| BR-26  | The<br> Platform must integrate with the Group's operational risk and incident<br> management processes.                                                                                            | Must         |
| BR-27  | The Platform must allow policies (e.g., allowed tools,<br> data scopes, jurisdictional rules) to be defined centrally and inherited by<br> templates and teams.                                     | Must         |

## 6.5 Operations and Monitoring

| **ID** | **Requirement**                                                                                                                                                                     | **Priority** |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| BR-28  | The<br> Platform must provide a Management Console presenting the current state of<br> all deployed teams, their health, throughput, queue depth and exceptions.                    | Must         |
| BR-29  | The Platform must surface cost and consumption telemetry<br> per team, per template version and per business unit.                                                                  | Must         |
| BR-30  | The<br> Platform must support configurable alerts to supervisors and operations staff<br> on defined thresholds (e.g., escalation backlog, failed tool calls, policy<br> breaches). | Must         |
| BR-31  | The Platform should provide trend analytics and<br> benchmarking across teams executing similar processes in different<br> jurisdictions or business units.                         | Should       |
| BR-32  | The<br> Platform should expose a read-only API for integration with executive<br> dashboards and corporate performance reporting.                                                   | Should       |

## 6.6 Client, Product and Process Scope for Reference

Teams

| **ID** | **Requirement**                                                                                                                                                                                                          | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| BR-33  | The<br> Client Onboarding reference team must execute the Group's regulated<br> onboarding playbook for a target segment and jurisdiction, including identity<br> verification, screening, risk rating and data capture. | Must         |
| BR-34  | The Payments Operations reference team must execute<br> defined exception-handling, investigation and repair tasks for in-scope<br> payment types, within sanctioned limits.                                             | Must         |
| BR-35  | Reference<br> teams must handle exception paths by escalation to human supervisors; they<br> must not attempt to resolve scenarios outside their sanctioned scope.                                                       | Must         |

# 7. Functional Requirements

Functional
Requirements (FR-xx) describe what the Platform must do. They elaborate the
business requirements into product behaviours. FRs are also prioritised using
MoSCoW.

## 7.1 Agent Template Management

| **ID** | **Requirement**                                                                                                                                                                                                                                                                    | **Priority** |
| ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| FR-01  | The<br> Platform shall provide a Template Designer capability allowing an authorised<br> user to create, edit, version, submit for approval, publish and retire agent<br> templates.                                                                                               | Must         |
| FR-02  | Templates shall expose the following inheritable fields:<br> template ID, display name, description, base template, attribute set, skill<br> set, tool bindings, data access scopes, guardrails/policies, supervision<br> requirements, lifecycle hooks, owning business function. | Must         |
| FR-03  | The<br> Template Designer shall display effective (inherited + overridden) values for<br> any selected template version.                                                                                                                                                           | Must         |
| FR-04  | The Platform shall validate a template before approval,<br> running automated checks covering policy conformance, tool availability, data<br> scope legality and prompt/policy asset integrity.                                                                                    | Must         |
| FR-05  | The<br> Platform shall require dual approval (business owner and risk owner) for any<br> template change that affects permissions, data scopes, tool bindings or<br> guardrails.                                                                                                   | Must         |
| FR-06  | The Platform shall support staged rollout of a new<br> template version — active deployments shall continue to reference the pinned<br> version until explicitly upgraded.                                                                                                         | Must         |

## 7.2 Team Composition

| **ID** | **Requirement**                                                                                                                                                                                                                        | **Priority** |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| FR-07  | The<br> Platform shall provide a Team Composer UI allowing a designer to define a<br> team by selecting a team archetype, assigning agents from templates, and<br> binding the team to a business function, jurisdiction and playbook. | Must         |
| FR-08  | The Team Composer shall enforce team-level structural<br> rules (e.g., one-and-only-one Supervisor, at least one QA on regulated teams,<br> maximum team size).                                                                        | Must         |
| FR-09  | The<br> Team Composer shall calculate and display the aggregated permissions, data<br> scopes and tool access of the composed team, highlighting any items requiring<br> additional approval.                                          | Must         |
| FR-10  | The Team Composer shall produce a Team Definition artefact<br> that is versioned, approvable and deployable.                                                                                                                           | Must         |
| FR-11  | The<br> Team Composer shall support reusable team archetypes (e.g.,<br> 'Maker-Checker-Supervisor'), distinct from individual agent templates.                                                                                         | Should       |

## 7.3 Deployment

| **ID** | **Requirement**                                                                                                                                                    | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| FR-12  | The<br> Platform shall deploy approved Team Definitions into target Azure<br> environments (dev, test, UAT, prod) through an automated, policy-gated<br> pipeline. | Must         |
| FR-13  | Deployment shall provision an isolated runtime boundary<br> per team, including dedicated identity, secrets scope, logging scope and<br> network policy.           | Must         |
| FR-14  | Deployment<br> to production shall require documented approvals from designated business,<br> risk and technology approvers.                                       | Must         |
| FR-15  | The Platform shall support blue-green and canary<br> deployment patterns for updates to an existing team's configuration.                                          | Should       |
| FR-16  | The<br> Platform shall support rollback to the previously deployed Team Definition<br> version within a defined recovery time objective.                           | Must         |

## 7.4 Runtime Orchestration

| **ID** | **Requirement**                                                                                                                                                                            | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| FR-17  | The<br> Platform shall host agents on the Microsoft Agent Framework runtime within<br> Azure, providing managed messaging, state, memory and tool invocation.                              | Must         |
| FR-18  | The Supervisor agent within a team shall be responsible<br> for task decomposition, delegation to Maker agents, and routing of outputs to<br> QA agents prior to completion or escalation. | Must         |
| FR-19  | All<br> inter-agent messages and tool invocations shall be captured with correlation<br> IDs enabling full end-to-end traceability of a case.                                              | Must         |
| FR-20  | Agents shall operate under a least-privilege identity;<br> tools shall be invoked via policy-wrapped adapters that enforce data scope,<br> rate limits and jurisdictional rules.           | Must         |
| FR-21  | The<br> Platform shall provide durable task queues, retry semantics and idempotency<br> guarantees for long-running operations.                                                            | Must         |
| FR-22  | The Platform shall support scheduled, event-driven and<br> on-demand task initiation for teams.                                                                                            | Must         |

## 7.5 Management Console

| **ID** | **Requirement**                                                                                                                                                     | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| FR-23  | The<br> Management Console shall provide a unified view of all deployed teams across<br> the Group, filterable by business unit, jurisdiction, function and status. | Must         |
| FR-24  | The Console shall present team-level dashboards showing<br> throughput, success rate, queue depth, exceptions and cost.                                             | Must         |
| FR-25  | The<br> Console shall expose lifecycle controls (deploy, pause, resume, retire) to<br> authorised users, with appropriate confirmation and approval flows.          | Must         |
| FR-26  | The Console shall present a catalogue view of all approved<br> templates and team archetypes with metadata, lineage and usage telemetry.                            | Must         |
| FR-27  | The<br> Console shall provide a regulator/audit view that surfaces governance<br> artefacts (approvals, DPIAs, risk assessments, evidence packs) per team.          | Must         |
| FR-28  | The Console shall support dark mode and high-contrast<br> accessibility modes.                                                                                      | Should       |

## 7.6 Supervisor Workbench

| **ID** | **Requirement**                                                                                                                                                     | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| FR-29  | The<br> Workbench shall present a prioritised queue of items requiring supervisor<br> attention across the teams that the supervisor is accountable for.            | Must         |
| FR-30  | Each queue item shall display the case context, the action<br> the agent proposes to take (or has taken), the supporting evidence and the<br> recommended decision. | Must         |
| FR-31  | The<br> Workbench shall allow the supervisor to approve, reject, modify or take over<br> an agent's proposed action, with mandatory rationale capture.              | Must         |
| FR-32  | The Workbench shall provide a timeline view of every<br> action taken on a case by any agent or human, including tool calls and data<br> accesses.                  | Must         |
| FR-33  | The<br> Workbench shall support dual-control (four-eyes) for configured high-risk<br> actions.                                                                      | Should       |
| FR-34  | The Workbench shall allow supervisors to annotate patterns<br> and feed observations back into template improvement workflows.                                      | Should       |

## 7.7 Observability, Audit and Reporting

| **ID** | **Requirement**                                                                                                                                                 | **Priority** |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| FR-35  | The<br> Platform shall capture structured telemetry for every agent action, tool<br> invocation, data access, decision, escalation and supervisor intervention. | Must         |
| FR-36  | Audit records shall be written to tamper-evident storage<br> with defined retention aligned to regulatory requirements per jurisdiction.                        | Must         |
| FR-37  | The<br> Platform shall provide a configurable evidence-pack generator that assembles<br> governance and operational records for a specified team and period.    | Must         |
| FR-38  | The Platform shall support export of audit and telemetry<br> data to the Group SIEM and enterprise data platform.                                               | Must         |
| FR-39  | The<br> Platform shall provide standard regulatory reports (e.g., material AI system<br> register) and extensibility for jurisdiction-specific reports.         | Should       |

## 7.8 Identity, Access and Policy

| **ID** | **Requirement**                                                                                                                                  | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| FR-40  | The<br> Platform shall integrate with the Group's identity provider (Entra ID) for<br> human authentication and single sign-on.                  | Must         |
| FR-41  | Agents shall be assigned managed workload identities;<br> human-to-agent impersonation shall not be permitted.                                   | Must         |
| FR-42  | The<br> Platform shall enforce fine-grained RBAC covering template management, team<br> composition, deployment, supervision and audit roles.    | Must         |
| FR-43  | The Platform shall enforce policy-as-code for runtime<br> decisions (e.g., can this agent call this tool on this data in this<br> jurisdiction). | Must         |
| FR-44  | The<br> Platform shall provide a break-glass procedure with heightened logging and<br> notification.                                             | Must         |

# 8. Non-Functional Requirements

Non-Functional
Requirements (NFR-xx) describe how well the Platform must perform. They are a
contract between the business and delivery on quality attributes.

## 8.1 Performance and Scalability

| **ID** | **Requirement**                                                                                                                                                            | **Priority** |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-01 | The<br> Platform shall support concurrent operation of at least 100 deployed teams<br> and 10,000 active agent instances without material degradation at steady<br> state. | Must         |
| NFR-02 | The Management Console shall render primary dashboards<br> within 2 seconds at the 95th percentile under design load.                                                      | Must         |
| NFR-03 | Agent<br> task dispatch from queue to first agent action shall complete within 3<br> seconds at the 95th percentile for interactive workloads.                             | Must         |
| NFR-04 | The Platform shall scale elastically to absorb a 5× surge<br> in task volume without manual intervention.                                                                  | Must         |

## 8.2 Availability and Resilience

| **ID** | **Requirement**                                                                                                                                                                               | **Priority** |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-05 | The<br> Platform runtime shall achieve 99.9% monthly availability for production-tier<br> teams.                                                                                              | Must         |
| NFR-06 | The Platform shall provide cross-region disaster recovery<br> with RTO of 4 hours and RPO of 15 minutes for production-tier teams.                                                            | Must         |
| NFR-07 | The<br> Platform shall degrade gracefully: if a downstream tool or dependency is<br> unavailable, affected tasks shall pause safely and alert supervisors rather<br> than fail destructively. | Must         |

## 8.3 Security

| **ID** | **Requirement**                                                                                                                                                                                              | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| NFR-08 | The<br> Platform shall comply with the Group's Information Security standards,<br> including secrets management, encryption in transit and at rest<br> (customer-managed keys) and network segmentation.     | Must         |
| NFR-09 | The Platform shall pass the Group's threat modelling,<br> penetration testing and secure code review prior to each major release.                                                                            | Must         |
| NFR-10 | Prompt-injection,<br> tool-abuse, data exfiltration and model-manipulation risks shall be addressed<br> via layered controls including input/output filtering, tool allow-lists and<br> egress restrictions. | Must         |
| NFR-11 | The Platform shall not train any foundation or frontier<br> model on the Group's data or customer data.                                                                                                      | Must         |

## 8.4 Privacy and Data Protection

| **ID** | **Requirement**                                                                                                                                                             | **Priority** |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-12 | The<br> Platform shall honour data classification, residency and cross-border rules<br> per jurisdiction, with automated enforcement at the tool and data-access<br> layer. | Must         |
| NFR-13 | Data Protection Impact Assessments shall be required<br> before any team that processes personal data is deployed into production.                                          | Must         |
| NFR-14 | Data<br> minimisation shall be enforced — agents shall receive only the data required<br> for the specific task at hand.                                                    | Must         |
| NFR-15 | The Platform shall support data subject rights requests<br> (access, rectification, erasure) for any personal data it processes or<br> retains.                             | Must         |

## 8.5 Compliance and Regulatory

| **ID** | **Requirement**                                                                                                                                                                                                                                                                     | **Priority** |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-16 | The<br> Platform shall be designed to meet obligations under applicable regimes in<br> each operating jurisdiction, including but not limited to the EU AI Act, UK<br> regulatory expectations for AI, the South African POPIA and FCA/PRA<br> operational resilience requirements. | Must         |
| NFR-17 | The Platform shall support classification of each agent<br> team against the applicable AI risk framework and apply proportionate<br> controls.                                                                                                                                     | Must         |
| NFR-18 | The<br> Platform shall support supervisory engagement, including the ability to<br> demonstrate controls in a regulator walkthrough at short notice.                                                                                                                                | Must         |

## 8.6 Usability and Accessibility

| **ID** | **Requirement**                                                                                                        | **Priority** |
| ------ | ---------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-19 | The<br> Management Console and Supervisor Workbench shall comply with WCAG 2.2 AA<br> accessibility guidelines.        | Must         |
| NFR-20 | The UI shall support responsive rendering on modern<br> desktop and tablet form factors.                               | Should       |
| NFR-21 | Core<br> supervisor tasks shall be achievable within three interactions (clicks/taps)<br> from the primary queue view. | Should       |
| NFR-22 | The UI shall provide internationalisation support, with<br> initial locales English (UK) and English (SA).             | Should       |

## 8.7 Maintainability and Extensibility

| **ID** | **Requirement**                                                                                                                            | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| NFR-23 | The<br> Platform shall be designed as a set of loosely-coupled services, each<br> independently deployable, with defined public contracts. | Must         |
| NFR-24 | New agent templates and tool adapters shall be addable<br> without changes to the Platform core.                                           | Must         |
| NFR-25 | The<br> Platform shall maintain a reference architecture, developer documentation and<br> a curated set of example templates.              | Should       |

## 8.8 Observability

| **ID** | **Requirement**                                                                                                                                              | **Priority** |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| NFR-26 | The<br> Platform shall provide structured logging, metrics and distributed tracing<br> for every service and every agent action, aligned with OpenTelemetry. | Must         |
| NFR-27 | The Platform shall expose health, readiness and cost<br> telemetry via standard endpoints for Group monitoring integration.                                  | Must         |

## 8.9 Cost

| **ID** | **Requirement**                                                                                                                        | **Priority** |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------- | ------------ |
| NFR-28 | The<br> Platform shall provide per-team cost allocation and chargeback reporting to<br> business units.                                | Must         |
| NFR-29 | The Platform shall implement cost guardrails (budgets,<br> throttles, alerts) at team and tenant level to prevent runaway consumption. | Must         |

# 9. Business Processes Supported

## 9.1 Client Onboarding (Reference Team)

The Client
Onboarding team executes the Group's regulated onboarding playbook for a target
segment in a target jurisdiction. The team comprises a Supervisor, multiple
Onboarding Maker agents and a QA agent. Inputs include a client application,
supporting documents and the applicable jurisdictional playbook. Outputs
include a completed onboarding case, a risk rating and a handoff package to the
servicing business.

Within the
team: the Supervisor agent decomposes the case into tasks; Maker agents perform
identity verification, document analysis, data capture, screening queries and
risk-factor collation; the QA agent applies control checks and tests for
exceptions; any adverse findings or out-of-appetite signals are escalated to
the human Accountable Supervisor. Every artefact is retained in the evidence
store.

## 9.2 Payments Operations (Reference Team)

The Payments
Operations team manages defined exception-handling tasks for in-scope payment
types — for example, investigation of payment repairs, sanctions hits triage
for categories defined as low-complexity and status reconciliations. The team
comprises a Supervisor, Payments Operations Maker agents and a QA agent. Agents
operate strictly within limits and matrices defined by the payments risk and
compliance functions; out-of-scope scenarios are escalated.

## 9.3 Future Teams (Illustrative)

•    KYC Periodic Refresh — periodic re-verification and
risk re-rating of existing clients.

•    Client Reporting — generation, quality-check and
dispatch of scheduled client reports.

•    Adverse Media and Sanctions Review — continuous
monitoring and triage of low-severity alerts.

•    Credit File Preparation — assembly of credit papers for
relationship manager review.

•    Reconciliations — routine break investigation and
resolution.

•    Vendor Due Diligence — scheduled refresh of third-party
risk assessments.

# 10. Assumptions, Dependencies and Constraints

## 10.1 Assumptions

•    The Group's enterprise AI strategy and its commitment
to an Azure-native agentic platform remain in place throughout delivery.

•    The Microsoft Agent Framework remains actively
supported by Microsoft and is suitable for production financial-services
workloads.

•    Approved foundation models are available through Azure
AI Foundry under enterprise contractual terms consistent with the Group's data
protection requirements.

•    Existing Group systems of record (core banking, CRM,
screening, document stores) expose APIs or can be wrapped in a service layer
suitable for agent tool invocation.

•    Business units will nominate accountable executives,
process experts and supervisors to support co-design and operation of reference
teams.

## 10.2 Dependencies

| **Dependency**                                                                             | **Owner**                 | **Implication if Unavailable**                                            |
| ------------------------------------------------------------------------------------------ | ------------------------- | ------------------------------------------------------------------------- |
| Azure<br> tenant, subscription hierarchy and landing zones for AI workloads.               | Group<br> Cloud Platform  | Deployment<br> and isolation requirements cannot be met.                  |
| Entra ID tenant configuration, managed identities and role<br> mapping.                    | Identity & Access Team    | Authentication, RBAC and segregation of duties cannot be<br> implemented. |
| Enterprise<br> data platform and master data management for customer and transaction data. | Group<br> Data            | Agents<br> cannot access the right data with lineage.                     |
| Access to core banking, CRM, screening, document and case<br> management platforms.        | Business Technology teams | Reference teams cannot execute end-to-end.                                |
| Model<br> Risk Management framework and operational risk management framework.             | CRO<br> Function          | Governance<br> cannot be operationalised.                                 |
| Group Compliance interpretation of regional AI<br> regulations.                            | CCO Function              | Jurisdictional controls cannot be calibrated.                             |

## 10.3 Constraints

•    Azure-native: the Platform must be built on the Group's
approved Azure services and patterns.

•    Microsoft Agent Framework: the Platform's agent runtime
must be based on the Microsoft Agent Framework to align with the Group's
strategic tooling choice.

•    Regulatory: the Platform must at all times demonstrate
compliance with the most restrictive applicable requirement across operating
jurisdictions.

•    Data residency: customer personal data must be
processed and stored in the jurisdiction of the owning entity, with defined
exceptions.

•    Human oversight: no fully autonomous customer-impacting
action is permitted without supervisor approval in the initial releases.

•    Change control: all production deployments must follow
the Group's release and change management standards.

# 11. Risks and Mitigations

| **Risk**                          | **Description**                                                                                          | **Impact** | **Mitigation**                                                                                                     |
| --------------------------------- | -------------------------------------------------------------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------ |
| R1.<br> Regulatory mis-step       | Deployment<br> of agentic AI in regulated processes draws supervisory attention or adverse<br> findings. | High       | Regulator-ready<br> governance from day one; staged rollout by jurisdiction; active engagement<br> via Compliance. |
| R2. Prompt injection / tool abuse | Adversaries manipulate agent inputs to trigger<br> unauthorised actions.                                 | High       | Layered input/output filtering; tool allow-lists; egress<br> controls; red-teaming and continuous testing.         |
| R3.<br> Model behavioural drift   | Underlying<br> models change or degrade, causing silent quality regression.                              | Medium     | Continuous<br> evaluation harness; canary deployments; MRM-aligned monitoring and model<br> pinning.               |
| R4. Over-automation               | Processes automated without adequate human oversight lead<br> to systemic errors at scale.               | High       | Mandatory Supervisor and QA roles; configurable approval<br> gates; kill-switch; four-eyes on high-risk actions.   |
| R5.<br> Data leakage              | Sensitive<br> data exposed via agent reasoning, tool calls or logs.                                      | High       | Data<br> minimisation; redaction; classified data domains; egress policies; audit of<br> all accesses.             |
| R6. Workforce impact              | Change impact on staff in affected operational roles<br> creates industrial-relations and morale issues. | Medium     | Early engagement with HR; co-design with front-line;<br> upskilling and role redesign; transparent communication.  |
| R7.<br> Vendor concentration      | Deep<br> dependency on a single vendor's agent framework creates lock-in.                                | Medium     | Abstraction<br> of agent runtime interfaces; portable templates; defined exit considerations.                      |
| R8. Delivery complexity           | The Platform is a complex, cross-cutting programme with<br> many dependencies.                           | High       | Phased delivery; clear reference teams; strong design<br> authority and product ownership.                         |
| R9.<br> Cost runaway              | Unbounded<br> agent consumption inflates cloud and model costs.                                          | Medium     | Budgets<br> and throttles per team; cost telemetry and chargeback; FinOps integration.                             |
| R10. Shadow agents                | Teams build agentic automations outside the Platform,<br> undermining governance.                        | Medium     | Make the Platform the easiest path; active policy that<br> governed use-cases route through the Platform.          |

# 12. Benefits Realisation

## 12.1 Benefit Categories

•    Hard financial benefits — operational cost avoidance,
reduced error and rework costs, reduced third-party handling costs.

•    Soft financial benefits — reduced cost of regulatory
action, reduced cost of client attrition tied to service delays.

•    Capacity benefits — freeing experienced staff to focus
on judgement-based work and exception handling.

•    Control benefits — improved auditability, reduced
control testing effort, better evidence of regulatory compliance.

•    Strategic benefits — optionality to industrialise
further processes rapidly as confidence and capability mature.

## 12.2 Benefits Tracking

Benefits will
be tracked through a dedicated Benefits Register, owned by the Platform's
business sponsor and reported quarterly to the steering committee. Each
reference team and subsequent team will have defined baseline, target and
realised measures. The Platform's telemetry will feed directly into benefit
measurement where possible to avoid manual reporting overhead.

# 13. Regulatory Considerations

The Platform
operates in a multi-jurisdictional regulated environment. The following
summarises key regulatory lenses; detailed analysis is owned by Compliance per
jurisdiction.

| **Jurisdiction / Regime**                   | **Key Considerations**                                                                                                                                                     | **Platform Response**                                                                                                              |
| ------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| EU AI<br> Act                               | Classification<br> of AI systems; obligations for high-risk systems including risk management,<br> data governance, logging, transparency, human oversight and conformity. | Platform<br> provides classification tooling, full logging, mandatory human oversight and<br> generates conformity evidence packs. |
| UK (FCA / PRA / DSIT)                       | Principles-based supervisory expectations for AI;<br> operational resilience; Consumer Duty implications where AI impacts outcomes.                                        | Explicit supervisor accountability; operational resilience<br> testing; Consumer Duty impact assessed per team.                    |
| South<br> Africa (POPIA, SARB expectations) | Personal<br> information processing; data residency; supervisory expectations for<br> technology risk.                                                                     | Data<br> residency controls per entity; DPIA workflow; local evidence pack generation.                                             |
| Channel Islands (JFSC, GFSC)                | Supervisory focus on outsourcing, operational resilience<br> and conduct.                                                                                                  | Platform treated as in-house capability; clear<br> accountability and resilience evidence.                                         |
| Switzerland<br> (FINMA)                     | Outsourcing<br> and operational risk guidance; data protection under FADP.                                                                                                 | Clear<br> responsibility allocation; data residency support where required.                                                        |
| India (RBI, DPDP Act)                       | Localisation and data protection rules; critical<br> operations risk management.                                                                                           | Region-specific data scopes; localisation-capable<br> deployment topology.                                                         |
| Mauritius<br> (Data Protection Act, FSC)    | Cross-border<br> data flows and personal data handling.                                                                                                                    | DPIA<br> and cross-border flow controls.                                                                                           |
| UAE (ADGM / DIFC / CBUAE)                   | Data protection and technology risk expectations;<br> regulator receptiveness to AI pilots with strong governance.                                                         | Region-specific data scopes; early engagement via<br> Compliance.                                                                  |

## 13.1 Cross-cutting Themes

•    Human oversight as the Platform's default posture, with
documented accountability for each team.

•    Model and agent inventory maintained centrally, linked
to the Group's Model Risk Management register.

•    Right of regulator access to evidence on demand,
without platform-specific training required.

•    Ongoing horizon-scanning for regulatory change, owned
by Compliance, with change impact tracked against the Platform backlog.

# 14. Implementation Approach

## 14.1 Delivery Principles

•    Product-led — the Platform is a product with a
long-lived product team, not a project with a finite end date.

•    Reference-team driven — each release is anchored by a
real, measurable business use-case.

•    Governance from day one — controls are built-in from
the first release, not retrofitted.

•    Engineered for evidence — every design choice supports
auditability and regulatory engagement.

•    Incremental and reversible — releases are small,
observable and reversible.

## 14.2 Delivery Roadmap

| **Phase**                       | **Key Outcomes**                                                                                                                                                      | **Indicative Timing** |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------- |
| Phase 0<br> — Mobilisation      | Business<br> case approval; programme set-up; architecture baselining; governance<br> structures established; reference team selection confirmed.                     | Months<br> 0–1        |
| Phase 1 — Foundation            | Platform core services; template library v1; Team<br> Composer; deployment engine; Management Console v1; Client Onboarding<br> reference team in pilot jurisdiction. | Months 1–6            |
| Phase 2<br> — Expansion         | Supervisor<br> Workbench; Payments Operations reference team; second jurisdiction; enhanced<br> policy engine; external audit evidence pack.                          | Months<br> 6–12       |
| Phase 3 — General Availability  | Self-service team composition for accredited designers;<br> multi-jurisdictional rollout; richer template catalogue; formalised SLAs.                                 | Months 12–18          |
| Phase<br> 4+ — Scale and Evolve | New<br> functions (KYC refresh, client reporting, reconciliations); advanced agent<br> capabilities; continued regulatory engagement.                                 | Months<br> 18+        |

## 14.3 Organisation

•    Business sponsor: accountable for outcomes and benefits
realisation.

•    Platform product owner: accountable for the Platform
backlog, prioritisation and release.

•    Use-case product owners: accountable for each
business-facing reference or subsequent team.

•    Design authority: architecture, security, data and risk
representation making decisions on cross-cutting design.

•    Delivery squads: aligned to Platform components and to
reference teams, AI-assisted via approved developer copilots.

•    Oversight forum: chaired by the executive sponsor, with
risk, compliance, audit and business unit representation.

# 15. Glossary

| Agent                         | An<br> autonomous software component, constructed from a template, that performs a<br> bounded role within a digital worker team, interacting with tools, data and<br> other agents under policy. |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Agent<br> Template            | A declarative, versioned definition of an agent's<br> attributes, role, skills, tools, data access scopes and guardrails; the<br> 'class' from which concrete agents are instantiated.            |
| Team                          | A<br> composed set of agents bound to a specific business function, jurisdiction<br> and playbook.                                                                                                |
| Team<br> Archetype            | A reusable structural pattern for teams (e.g.,<br> Maker-Checker-Supervisor).                                                                                                                     |
| Supervisor<br> Agent          | The<br> agent within a team responsible for task decomposition, delegation and<br> escalation.                                                                                                    |
| Accountable<br> Supervisor    | The named human with accountability for a deployed team's<br> outcomes.                                                                                                                           |
| QA /<br> Checker Agent        | An<br> agent dedicated to quality and control checks on outputs produced by Maker<br> agents.                                                                                                     |
| Maker<br> Agent               | An agent that performs the substantive work of a task (the<br> 'doer').                                                                                                                           |
| Playbook                      | A<br> documented, regulated procedure for a business function in a jurisdiction.                                                                                                                  |
| Tool                          | A governed capability (e.g., an API to a core system, a<br> search function, a document generator) an agent is permitted to invoke.                                                               |
| Policy-as-code                | Runtime-enforced<br> rules governing what agents may do under which conditions.                                                                                                                   |
| Digital<br> Worker            | A human-readable label for an agent team deployed to<br> perform a business function.                                                                                                             |
| Kill-switch                   | An<br> authorised, immediate halt of agent activity at team, function or enterprise<br> scope.                                                                                                    |
| Evidence<br> Pack             | A time-bounded, regulator-ready export of governance and<br> operational records for a team.                                                                                                      |
| Microsoft<br> Agent Framework | The<br> Microsoft framework for building and orchestrating AI agents, forming the<br> runtime foundation of the Platform.                                                                         |

# 16. Document End

This document
is the initial draft of the Business Requirements Document for the Agentic
Workforce Platform. It is intended as a working basis for review, challenge and
iteration with the nominated reviewers and approvers.

*The
companion Technical Requirements Document translates these business
requirements into the architectural, technical and operational specifications
required for detailed design and build.*
