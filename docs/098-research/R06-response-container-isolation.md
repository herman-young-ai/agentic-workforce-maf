# Azure Sandboxing Options for AI Agent Code Execution — Reference Architecture Comparison (May 2026)

## TL;DR

- **For a regulated bank running per-mission sandboxes from a C# AI agent platform on Azure Container Apps, the strongest fit is Azure Container Apps Dynamic Sessions with custom-container session pools (Hyper-V isolation, sub-second startup, native managed-identity auth, optional egress disable).** Use the *Shell* container type when missions need arbitrary shell commands, git, dotnet, npm and file I/O; use *PythonLTS* / *NodeLTS* code-interpreter pools only for snippet-style execution.
- **The native MAF / Foundry Code Interpreter tool is Python-only, runs inside Microsoft-managed ACA Dynamic Sessions in the same region as your project, has no outbound network access, and cannot run shell, git, or build tools.** For anything beyond data analysis or charting you must either use the *Custom Code Interpreter* MCP pattern (Python SDK / REST only at present — no first-party C# SDK as of May 2026) or, preferably for a C# bank platform, treat Dynamic Sessions / ACI as MCP / function tools your agent calls directly.
- **AKS with Kata Containers (`KataMshvVmIsolation`) or Kata Confidential Containers (`KataCcIsolation`, AMD SEV-SNP) gives the highest theoretical isolation guarantee** (per-pod microVM with its own kernel, optional hardware memory encryption, network policies, full control of CSI and secrets), but adds the most operational complexity. Docker-in-Docker on Container Apps is **not supported** (privileged containers are disabled platform-wide), so DinD is effectively off the table for ACA-hosted agents.

---

## Key Findings

### 1. Comparison Matrix

| Criterion | ACA Dynamic Sessions | Azure Container Instances (ACI) | ACA Jobs | MAF / Foundry Code Interpreter | DinD on ACA | AKS pod-per-mission (Kata) |
|---|---|---|---|---|---|---|
| **Startup latency** | **Milliseconds** from prewarmed pool (sub-second); cold pool spin-up minutes | ~30 s to several minutes (image pull + scheduling); slower in busy regions | ~10 – 30 s replica startup typically | Sub-second (uses Dynamic Sessions under the hood) | N/A — not supported | Kata pods 5 – 15 s warm node; node pool create minutes |
| **Persistent workspace across tasks** | Yes, **within a single session** keyed by your `identifier` query string; destroyed after `cooldownPeriodInSeconds` idle (Timed) or `maxAlivePeriodInSeconds` (OnContainerExit, up to 86 400 s / 24 h) | **Yes, durable** via Azure Files SMB mount; survives restart, redeploy, deletion | Per-execution only; can mount Azure Files for cross-execution state | No — sandbox tied to agent thread, ~1 h max with 30-min idle timeout, files via Foundry Files API only | N/A | Yes — PVCs (Azure Disk/Files), or per-pod ephemeral with `emptyDir` |
| **Custom runtime / install any tool** | **Yes** with custom-container pools (any Linux/amd64 image, your own Dockerfile). Built-in interpreters limited to Python LTS, Node LTS, or Shell | **Yes** — any Linux image | **Yes** — any Linux image | **No** for built-in (Python + curated data-science libs only). Custom Code Interpreter (preview, Python SDK / REST) lets you ship a custom container image, but C#/JS/Java SDKs do **not** yet expose it | N/A | **Yes** — full Kubernetes container freedom |
| **Git operations** | Yes in custom-container or Shell pool (install git in image) | Yes | Yes | **No** — sandbox is not a shell, no git binary, and outbound network is blocked | N/A | Yes |
| **File upload / download** | Built-in Python/JS interpreters: REST `/files/upload`, `/files/download`, `/files` list endpoints (files land in `/mnt/data`). Custom containers: whatever endpoints you expose | Via Azure Files mount, `az container exec`, or app endpoints | Via Azure Files / Blob | Foundry Files API for inputs; outputs returned as file IDs in messages. No SAS or arbitrary URL ingress; for the Custom Code Interpreter you must use SAS URLs / data URLs | N/A | Standard k8s patterns (kubectl cp, volumes, sidecars) |
| **Arbitrary shell commands** | **Yes** — *Shell* container type (`containerType: Shell`, with built-in MCP `runShellCommandInRemoteEnvironment` tool) or custom container exposing your own endpoint. Code-interpreter sessions execute *only* Python or JS snippets, not raw shell | Yes (your container's entrypoint is shell-capable) | Yes | **No** — Python `code_interpreter` runs sandboxed snippets; no `subprocess` to host shell, no PATH access to installed CLIs | N/A | Yes |
| **Network isolation** | Per-pool `sessionNetworkConfiguration.status`: **EgressDisabled** (default) or **EgressEnabled**. Hyper-V isolated. Custom-container pools require workload-profile environment; can run inside an internal Container Apps Environment with private endpoint | NSGs on delegated subnet, optional VNet integration; per-container fine-grained outbound rules require Azure Firewall in front | Same as ACA app — VNet + NSGs at environment level; no per-job egress toggle | **Sandbox cannot make outbound network requests** and does **not** inherit the agent's subnet config. No way to allowlist | N/A | Calico / Cilium / Azure NPM `NetworkPolicy`, full NSGs, can run airgapped |
| **Max session / job duration** | Timed lifecycle: cooldown-driven, no hard cap aside from idle timer. OnContainerExit lifecycle: `maxAlivePeriodInSeconds` up to **86 400 s (24 h)** | No hard ceiling for running container groups; commonly used for hours-long batch | Default `replicaTimeout` 30 min; configurable, but multi-hour jobs have historically been flaky (`DeadlineExceeded` reports) | **1 h active per session, 30-min idle timeout**, reset on activity | N/A | Bound only by pod lifecycle / node lifetime |
| **Cost model** | **Code-interpreter pool: $0.03 per allocated session-hour, billed in 1-hour increments per allocated session.** Custom-container pool: billed on the Dedicated plan — pool runs on dedicated **E16** instances; you pay for `nodeCount` × E16 vCPU/GiB-seconds plus session activity | Per-second vCPU + GiB-second at the container-group level; vCPU rounded up to whole, memory to nearest 0.1 GiB; Windows surcharge applies | Active vCPU-seconds + GiB-seconds during execution; jobs don't get the idle rate; first 180 000 vCPU-s & 360 000 GiB-s/month free per subscription | "Additional charges beyond Azure OpenAI tokens" (per Microsoft) — effectively the underlying ACA Dynamic Sessions session-hour billing flows through | N/A | Node-pool VM-hours + storage + egress; bin-packing helps but you pay for idle nodes |
| **Operational complexity** | **Low** — managed pools, no VMs, no k8s | **Low–Medium** — single-resource lifecycle, no scheduler | **Low** — same control plane as ACA | **Lowest** — fully managed, but inflexible | N/A | **Highest** — node pools, runtime classes, CSI, network policy, OS upgrades |
| **Aspire / ACA integration** | **First-class.** `Aspire.Hosting.Azure.AppContainers` (NuGet 13.x) and `Azure.Provisioning.AppContainers.SessionPool` model session pools as Bicep-emitting resources alongside your AppHost | Good — ACI hosting integration in Aspire community packages | Strong — `PublishAsAzureContainerAppJob` extension is built into Aspire | Strong from MAF/.NET via `Microsoft.Agents.AI.Foundry`, but the *code-interpreter* tool is fixed — no way to swap in custom runtimes | N/A | OK — Aspire deploys AKS but doesn't model node-pool runtime classes |

### 2. Suitability for a regulated-bank C# agent platform

| Option | Bank-grade verdict |
|---|---|
| **ACA Dynamic Sessions (Custom Container, Shell or BYO image)** | **Recommended primary**. Hyper-V isolation per session, network egress disabled by default, managed identity for image pull, scoped per-mission session identifiers (must be cryptographically random), full audit trail via `AppEnvSession*` Log Analytics tables, integrates with private endpoints on the parent ACA environment. |
| **ACA Dynamic Sessions (Code Interpreter)** | Use only for safe Python/JS analytics inside a mission; not a shell. |
| **ACI (with Confidential Containers / AMD SEV-SNP)** | **Strong fallback** when you need durable Azure Files workspace per mission and don't need a pool. Confidential Containers add memory encryption + attestation. Note **CVE‑2026‑21522** (privilege escalation in ACI Confidential Containers, disclosed early 2026 — verify patch status before relying on the TEE boundary as a sole control). |
| **ACA Jobs** | Suitable for *batch* style "execute mission, return result, exit"; not for interactive multi-step shells. Not Hyper-V isolated; same kernel as the host pool. |
| **Foundry Code Interpreter** | Acceptable only for the narrow "agent does data analysis on attached CSV" use case. Inadequate for git/dotnet/npm/shell missions. |
| **Self-managed DinD on ACA** | **Not viable** — ACA explicitly disallows privileged containers, and ACI also blocks Docker socket / privileged mode. |
| **AKS pod-per-mission with Kata** | **Strongest theoretical isolation**; ideal if you already operate AKS at scale and need network policy granularity. Significantly higher operational tax (node pool with `--workload-runtime KataMshvVmIsolation` on Gen-2 VMs with nested virt; Azure Linux only). For confidential workloads, `KataCcIsolation` adds AMD SEV-SNP. |

---

## Details

### Azure Container Apps Dynamic Sessions — what you actually get in May 2026

Dynamic Sessions ship as an Azure resource type `Microsoft.App/sessionPools`. Two pool dimensions matter:

**Pool management type** is always `Dynamic` for sandbox use. **Container type** can be:
- `PythonLTS` — built-in Python code interpreter (Jupyter-style, `/mnt/data` workspace, GA, also powers Microsoft Copilot's Advanced Data Analytics at >400 000 sessions/day).
- `NodeLTS` — built-in JavaScript/Node.js code interpreter (public preview, GA path).
- `Shell` — Linux shell environment with a platform-managed MCP endpoint exposing `runShellCommandInRemoteEnvironment` (accepts either `shellCommand` string or `execCommandAndArgs` array). This is the only built-in pool type that lets an agent run arbitrary shell commands without bringing your own image.
- `CustomContainer` — your own image; you define the HTTP API the session exposes; Azure handles pool warm-up, Hyper-V sandboxing, and lifecycle. Requires a workload-profiles–enabled ACA environment.

**Session lifecycle** (`dynamicPoolConfiguration.lifecycleConfiguration.lifecycleType`, API 2025-01-01+):
- `Timed`: session deleted after `cooldownPeriodInSeconds` of inactivity; every request resets the timer.
- `OnContainerExit`: session ends when the container process exits or `maxAlivePeriodInSeconds` is reached. Examples in the REST docs use `86400` (24 hours), which is the practical upper bound for a "mission".

**Scaling**: `scaleConfiguration.maxConcurrentSessions` (the cap; portal validation typically allows hundreds; Microsoft has run pools at 500+ in published examples), and `readySessionInstances` (prewarm count for sub-second allocation).

**Network**: `sessionNetworkConfiguration.status` is `EgressDisabled` by default; setting `EgressEnabled` opens internet access for code in the session. There is *no* per-session egress allowlist — if you need allowlisted destinations, front the pool with Azure Firewall on the parent environment, or keep egress disabled and proxy outbound HTTP through your agent app. Custom-container pools live inside an ACA environment and inherit its VNet, private endpoints, and DNS configuration; you can deploy the parent environment as Internal-only to keep the management endpoint off the public internet.

**Identity & auth**: All requests to `https://<region>.dynamicsessions.io/...` need an Entra ID bearer token whose `aud` claim is `https://dynamicsessions.io`. The caller principal needs the **Azure ContainerApps Session Executor** role on the pool (and Contributor for management ops). Pool-level managed identity can pull private images from ACR and — if explicitly enabled, *and Microsoft warns to use this with extreme caution* — be exposed to in-session code so it can mint Entra tokens.

**Session identifier** is a free-form string supplied via the `identifier=` query parameter on every call. **Treat it as sensitive**: it scopes both the routing target and the security context. Microsoft's guidance is to generate it cryptographically per user/mission, never let end users supply it, and use HTTPS end-to-end. For a per-mission bank pattern, hash `(missionId, tenantId, agentRunId)` with HMAC-SHA256 and use that.

**API & SDK reality from C#**:
- Control plane: `Aspire.Hosting.Azure.AppContainers` 13.x and `Azure.Provisioning.AppContainers.SessionPool` provision pools from .NET / Aspire AppHost. The class `SessionPool : ProvisionableResource` is in the published Azure.Provisioning.AppContainers SDK.
- Data plane: there is **no first-party `Azure.AI.DynamicSessions` C# SDK** as of May 2026. Microsoft's documented integrations are Python (LangChain `SessionsPythonREPLTool`, LlamaIndex, Semantic Kernel, AutoGen `ACADynamicSessionsCodeExecutor`). For C#, the standard pattern is: `DefaultAzureCredential().GetTokenAsync(new TokenRequestContext(new[] { "https://dynamicsessions.io/.default" }))` and call the REST endpoints with `HttpClient`. Semantic Kernel has shown samples doing exactly this in the official tutorial. The `runShellCommandInRemoteEnvironment` MCP endpoint can also be wired into `Microsoft.Agents.AI` via its MCP client, giving you a strongly-typed tool surface in C#.
- Built-in MCP server is enabled with `mcpServerSettings.isMCPServerEnabled: true` (preview API `2025-02-02-preview`+, requires `az feature register --namespace Microsoft.App --name SessionPoolsSupportMCP`). It exposes the endpoint at `properties.mcpServerEndpoint` and uses an **API key**, retrieved via `fetchMCPServerCredentials`, in the `x-ms-apikey` header — different from the bearer-token auth used for the regular pool management API.

**Pricing**: Microsoft's GA blog states **$0.03 per session-hour** for the built-in code-interpreter pool, billed in 1-hour increments per *allocated* session (the Container Apps billing doc confirms "from time it's allocated until it's deallocated, in increments of one hour"). Custom-container pools are billed under the Container Apps Dedicated plan, with the pool running on dedicated **E16** instances; `nodeCount` is exposed as a property and scales with active+ready sessions.

### Azure Container Instances

ACI's appeal for bank workloads is durability + simplicity: one resource, one billing line, mounts an Azure Files share that survives forever.

- **Per-second billing** at the container-group level. vCPU rounds up to whole numbers, memory to nearest 0.1 GiB. Up to 4 vCPU and (in most regions) up to 16 GiB memory per vCPU.
- **Restart policy**: `Always` (default), `OnFailure`, or `Never`. For a "mission container that runs to completion and stops", `Never` or `OnFailure` is correct.
- **Azure Files mount**: SMB share mounted via `--azure-file-volume-account-name/--azure-file-volume-share-name/--azure-file-volume-account-key/--azure-file-volume-mount-path`. Only Linux containers, only SMB (NFS not supported), and **identity-based / managed-identity SMB mounting is not supported** — you must inject the storage key (e.g., from Key Vault). For per-mission isolation, create a dedicated share per mission ID or per tenant.
- **Privileged / DinD**: ACI does not expose the host Docker socket and does not allow privileged containers; rootless Docker daemons inside the container fail because Linux user-namespace capabilities aren't granted. DinD is **not supported**.
- **Confidential Containers (GA)**: deploy in a container group with Hyper-V isolation + AMD SEV-SNP TEE, full guest attestation via Microsoft Azure Attestation, and a CCE policy generated by `az confcom` that pins exactly which images, env vars, and commands may run. **Note CVE-2026-21522** — Microsoft assigned a CVE in early 2026 for an EoP flaw in ACI Confidential Containers; verify the platform fix is in place and don't treat the TEE boundary as your only isolation control.
- **Cold start**: typically 30 s – 2 min depending on image size and region load. Microsoft's troubleshooting docs explicitly call out long pulls in busy regions. This makes ACI a poor fit for *interactive* missions where the user is waiting, but a fine fit for queued mission execution where another minute doesn't matter.

### Azure Container Apps Jobs

- Trigger types: `Manual`, `Schedule` (cron), `Event` (KEDA scale rules — Service Bus, queues, etc.).
- Per-execution `replicaTimeout` (seconds) — default 30 min, configurable. Despite high values being permitted, the Container Apps repo has open issues where executions are killed at the 30-min mark or never marked complete; long-running (>1 h) jobs should be tested carefully or run on AKS / ACI instead.
- Same kernel as the rest of the ACA workload-profile node — **not** Hyper-V isolated. Suitable for *trusted* code execution; **not** suitable as a per-mission sandbox for untrusted code execution.
- No privileged containers, same as ACA apps. Linux/amd64 only.
- Aspire support is first-class via `PublishAsAzureContainerAppJob`.

### MAF / Foundry Code Interpreter

The Microsoft Foundry agent service ships a built-in `code_interpreter` tool. As of May 2026:

- **Language**: Python only. JavaScript/TypeScript is *not* a supported language for the built-in tool (the Node.js code interpreter exists separately as an ACA Dynamic Sessions container type).
- **Runtime**: Microsoft-managed Hyper-V isolated sandbox **running on Azure Container Apps Dynamic Sessions**, in the same Azure region as your Foundry project. Each conversation/thread invocation creates its own session; concurrent invocations get separate sessions.
- **Session lifetime**: 1 hour active, 30-minute idle timeout. Concurrent code-interpreter calls in different threads spawn parallel sessions, each billed independently.
- **Network**: The sandbox **cannot make outbound network requests** and does **not** inherit your agent subnet. There is no public allowlist mechanism.
- **Tools / shell / git**: **Not exposed.** The tool is for "writing and running Python code iteratively to solve data analysis and math tasks, and to generate charts." There is no `subprocess` shell-out, no git, no dotnet, no npm. Pre-installed packages are the standard data-science set (pandas, numpy, matplotlib, scikit-learn, etc.). For custom packages, the only path is the **Custom Code Interpreter** preview.
- **Custom Code Interpreter** (preview, `azure-ai-projects` Python SDK + REST only — **C#/JS/Java SDKs do not yet support this feature**, per the Foundry docs): Microsoft provisions a custom MCP server backed by a session pool you can configure (Python image, packages, CPU/memory). Provisioning via `infra.bicep` "can take up to one hour, depending on the number of standby instances you request." Files in/out require SAS URLs or data URLs — there is no file-store integration.
- **C# SDK**: `Microsoft.Agents.AI.Foundry` and `Azure.AI.Projects` (preview) expose the `CodeInterpreterToolDefinition` and let you upload files via `OpenAIFileClient.UploadFile(... FileUploadPurpose.Assistants)`. Sample code is in the Foundry docs and the Azure SDK for .NET repo.
- **Pricing**: "additional charges beyond token-based fees"; effectively the underlying ACA Dynamic Sessions session-hour rate flows through.

**Bottom line for a bank**: Foundry's built-in code interpreter is too constrained for "agent runs git clone, npm install, dotnet test, shell pipeline". Use it only for the data-analysis sub-tools, and route everything else to your own ACA Dynamic Sessions custom-container pool exposed as either an MCP tool or a function tool.

### MAF function-tool architecture for delegating to a remote container

The pattern Microsoft recommends and demonstrates in the `microsoft/agent-framework` and `Azure-Samples/container-apps-dynamic-sessions-samples` repos:

1. **C# AI agent** runs in your ACA app (a `WebApplication` with `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Foundry`). It is registered with `DefaultAzureCredential` locally and `ManagedIdentityCredential` in production.
2. **Per-mission session identifier** is generated server-side (HMAC of mission ID) and stored in your mission record. **Never** let the LLM or end-user choose the identifier.
3. The agent is given function tools (via `[Description]`-annotated methods or `AIFunctionFactory.Create(...)`) such as `RunShellAsync(string command)`, `WriteFileAsync(string path, string contents)`, `ReadFileAsync(string path)`, `GitCloneAsync(string repo)`. Each tool implementation:
   - Acquires a token: `await new DefaultAzureCredential().GetTokenAsync(new TokenRequestContext(new[] { "https://dynamicsessions.io/.default" }))`.
   - POSTs to `<poolEndpoint>/<api-path>?identifier=<missionSessionId>&api-version=2025-10-02-preview` with the bearer token.
   - For Shell pools, calls the platform MCP server: `POST <mcpServerEndpoint>` with `x-ms-apikey: <key>` and a JSON-RPC `tools/call` body invoking `runShellCommandInRemoteEnvironment`.
   - Returns stdout/stderr/exit-code as a structured tool result the LLM can reason about.
4. Because the same `identifier` is used across the mission, ACA routes every call to the same warm session — **state persists** (file system at `/mnt/data` and process memory) until the cooldown or `maxAlivePeriodInSeconds` expires.
5. End-of-mission cleanup: explicit `POST .../management/stopSession?identifier=...` to deallocate immediately and stop billing.
6. Telemetry: enable **OpenTelemetry** in MAF (`setup_observability` / equivalent in .NET) and route to Application Insights — you get spans for `invoke_agent`, `chat`, and each tool call, plus AppEnvSession logs from the session pool. Microsoft's "Even simpler to safely execute AI-generated code…" Tech Community blog (Nov 2025) walks through the wiring end-to-end.

For ACI as the back end, the same pattern works but the tool implementation creates an ACI container group via the `Azure.ResourceManager.ContainerInstance` SDK on first call (with `OnFailure` restart policy and an Azure Files mount keyed off mission ID), `az container exec`'s the command, and either keeps the group alive for the mission's duration or tears it down after each call. Cold start is the trade-off vs. Dynamic Sessions' pre-warmed pool.

### Self-managed Docker-in-Docker on Container Apps

**Not supported.** Microsoft's official Container Apps documentation states: *"Privileged containers: Azure Container Apps doesn't allow privileged containers mode with host-level access."* This rules out classic DinD (which needs `--privileged` and `/var/lib/docker` access). ACI also blocks privileged mode and host Docker socket, per the Microsoft Q&A response on ACI + Docker. Workarounds people attempt — Podman rootless, sysbox, kaniko for image builds — are partial: kaniko works for *building* images without DinD, but you still cannot run arbitrary `docker run` from inside an ACA container. If your missions truly need an isolated container engine, your options are:

- **AKS with Kata Containers** (each "DinD" workload becomes a Kata pod — VM-level isolation provides the host the DinD pattern is trying to replicate).
- **ACI per mission** with `kaniko` or `buildah` for image building and ACR Tasks for builds.
- **Use ACA Dynamic Sessions custom containers**: instead of running Docker, hand the agent a pre-built image with the languages and tools it needs, and let it execute things directly. This is the simplest answer for 90 % of "agent needs to run code" requirements.

### AKS pod-per-mission with Kata / gVisor

Microsoft documents two production-grade isolation runtimes on AKS:

- **`KataMshvVmIsolation`** — Pod Sandboxing GA. Each pod runs in its own lightweight VM on Microsoft Hypervisor + Cloud Hypervisor VMM, with its own kernel. Requires Azure Linux node OS and a Gen-2 VM SKU with nested virtualization (Dsv3, Dsv4, Dsv5, Esv5, etc.). Pod runtime class is `kata-mshv-vm-isolation`. Default pod VM memory is 512 MiB; your pod's `resources.limits.memory` becomes the VM size, so size accordingly (Kata agent + guest kernel consume some). CPU limits round up to whole vCPUs at the VM boundary.
- **`KataCcIsolation`** — Confidential Containers (preview/GA-track). Builds on Pod Sandboxing but uses AMD SEV-SNP capable VM SKUs (e.g., `Standard_DC4as_v5`) — the pod's memory is hardware-encrypted and attested. Targeted at "data clean rooms, banking, healthcare, public sector" by Microsoft. Pod runtime class is `kata-cc`.

**gVisor on AKS**: gVisor is *not* an Azure-supported workload runtime on AKS. The kubernetes-sigs `agent-sandbox` CRD project (built specifically for AI agent workloads) supports gVisor and Kata as backends, but on AKS you would self-manage gVisor on a custom node pool — there is no `--workload-runtime gVisor` switch. Microsoft's path for AI-agent isolation on AKS is Kata.

For a bank, the AKS-Kata deployment pattern would be: dedicated `kata` node pool with `taints kata=enabled:NoSchedule` and a label, mission scheduler creates a Job/Pod with `runtimeClassName: kata-mshv-vm-isolation`, `nodeSelector: kata=enabled`, an ephemeral PVC for `/workspace`, and `NetworkPolicy` denying all egress except to your allowlisted services. Pod completion deletes the pod; PVC tears down; VM is reclaimed.

### .NET Aspire / Container Apps integration quality

The C# agent platform itself is presumably an Aspire AppHost project deployed via `azd up` to ACA. From there:

- **Dynamic Sessions** is modeled in `Aspire.Hosting.Azure.AppContainers` (NuGet 13.2+) and `Azure.Provisioning.AppContainers.SessionPool`. You can declare the pool in C#, wire role assignments to your agent app's managed identity, and emit Bicep on `aspire publish`. This is the only option in the matrix that has true first-party Aspire modeling.
- **ACA Jobs** is similarly first-class via `PublishAsAzureContainerAppJob` (Aspire 9.x+).
- **ACI** is supported through community packages and `Azure.ResourceManager.ContainerInstance` for runtime provisioning.
- **AKS** is modeled by Aspire only at the deployment level (publishing manifests); runtime classes, node pools, and Kata configuration are out-of-band Bicep / az CLI work.

### Pricing summary (May 2026, public list prices)

- **ACA Dynamic Sessions, code interpreter**: **US$0.03 per session-hour**, billed in 1-hour increments from allocation to deallocation per session.
- **ACA Dynamic Sessions, custom container**: Container Apps Dedicated-plan pricing on E16 instances, scaled to active+ready sessions. There is no separate "premium" charge for Dynamic Sessions on the Dedicated plan.
- **ACA apps (Consumption)**: per-second vCPU + GiB; first 180 000 vCPU-s, 360 000 GiB-s, and 2 M requests/month free per subscription.
- **ACA Jobs**: same per-second resource billing, always at the active rate (no idle rate).
- **ACI**: per-second vCPU-s + GiB-s; vCPU rounded up to whole, memory to nearest 0.1 GiB; container-group duration measured from first image pull start to group stop.
- **AKS Kata pods**: standard AKS VM-hour billing for the underlying nodes plus AKS uptime SLA (where enabled). Confidential VM SKUs (`DCa[s]_v5`, `ECa[s]_v5`) carry a premium versus standard `D[s]_v5`.

---

## Recommended Reference Architecture for a Regulated Bank

1. **Baseline sandbox**: ACA Dynamic Sessions **custom-container pool** built from a hardened base image with the exact toolchain your agents need (git, curl, dotnet 9 SDK, Node 20 LTS, Python 3.12, jq, ripgrep, etc.) — pre-built and signed in Azure Container Registry, pulled with managed identity. Lifecycle `OnContainerExit` with `maxAlivePeriodInSeconds: 86400`. `EgressDisabled` by default.
2. **Per-mission session ID** = HMAC-SHA256 of `(missionId, tenantId, agentRunId)`. Stored server-side; never round-tripped through the LLM.
3. **State persistence**: rely on the in-session file system for the mission's lifetime; if state must outlive the session, snapshot to a per-mission Azure Blob container at mission end via a server-side hook.
4. **Network**: parent ACA environment is Internal-only with a private endpoint; outbound traffic to required corporate services (artifact feeds, Git server, package mirrors) goes through Azure Firewall with FQDN allowlists; the session pool itself has `EgressEnabled` only if required *and* the firewall enforces the allowlist.
5. **Auth**: agent app uses a user-assigned managed identity with `Azure ContainerApps Session Executor` on the pool. The pool's own managed identity is used **only** for ACR image pull, never exposed to in-session code (the docs warn against this for untrusted code).
6. **Audit**: send `AppEnvSessionConsoleLogs` and `AppEnvSessionLifecycleLogs` to a regulated Log Analytics workspace; correlate via OpenTelemetry traces from MAF's tool spans (`invoke_agent`, tool, sandbox call).
7. **Higher-assurance missions** (e.g., handling production cardholder data): route to **AKS Kata-CC node pool** (AMD SEV-SNP) instead of Dynamic Sessions, with `NetworkPolicy` deny-all-egress, ephemeral PVC, and remote attestation before secret release via Azure Attestation.
8. **Long-lived/durable workspace missions** (e.g., 6-hour batch reconciliation): route to **ACI Confidential Container groups** with Azure Files mount, OnFailure restart policy, CCE policy locked to a specific image digest. Verify CVE-2026-21522 patch state and don't rely on the TEE boundary as the sole control.
9. **Avoid**: DinD-on-ACA (impossible), MAF built-in code interpreter for anything beyond pure analytics, ACA Jobs as a sandbox for *untrusted* code (no Hyper-V boundary).

---

## Caveats

- **Regional availability** for ACA Dynamic Sessions is still smaller than ACA's overall footprint; confirm your bank's required Azure region from the portal location dropdown before committing. Microsoft's docs explicitly note "regional availability may change."
- **No first-party C# SDK for the Dynamic Sessions data plane** as of May 2026 — Python (LangChain, AutoGen, Semantic Kernel, LlamaIndex) has rich integrations; .NET callers must use raw `HttpClient` + `DefaultAzureCredential` against the documented REST API, or wire in MCP via `Microsoft.Agents.AI`'s MCP client. The Aspire-side resource provisioning for `SessionPool` is GA in Azure.Provisioning.AppContainers.
- **Custom Code Interpreter for MAF agents is Python SDK / REST only**; the Foundry docs explicitly call out "C#, JavaScript/TypeScript, and Java SDKs do not yet support this feature." If your agent is C#, this means the tool definition + MCP wiring is feasible (MCP is language-neutral) but the *helper SDK* is not.
- **Container Apps Jobs `replicaTimeout`** has historical defects around the 30-minute mark (open GitHub issues `azure-container-apps#986`, `#1071`) — long-running jobs (>1 h) need integration testing or should run on ACI / AKS instead.
- **CVE-2026-21522** — Microsoft assigned a CVE for an elevation-of-privilege flaw in ACI Confidential Containers in early 2026. The hardware TEE provides defense in depth, but never treat it as the sole control; layer with execution policies (CCE), network egress restrictions, and short-lived credentials.
- **gVisor is not natively supported** on AKS — Microsoft's documented sandboxing runtimes on AKS are Kata-based (`KataMshvVmIsolation`, `KataCcIsolation`). Earlier blog posts that say "AKS pod sandboxing uses gVisor" predate the GA announcement and are inaccurate; the implementation uses Kata Containers + Microsoft Hypervisor + Cloud Hypervisor.
- **DinD on Container Apps**: definitively unsupported. Some StackOverflow / community workarounds describe sysbox or rootless Podman; none are production-supportable on Microsoft-managed ACA. If you genuinely need an isolated Docker daemon per mission, that's an AKS-Kata or VM-per-mission decision, not an ACA decision.
- **Foundry pricing transparency**: Microsoft documents code-interpreter charges as "additional charges beyond token-based fees" without always publishing a per-call price line. The most-quoted figure ($0.03 per session-hour for the underlying ACA Dynamic Sessions code interpreter) is from Microsoft's GA announcement, but Foundry's billing model layers Memory ($0.25 per 1K events for short-term memory, etc., billed June 2026 onward) and tool charges that may evolve. Validate current pricing against the Azure Pricing page in your contracted currency before committing capacity plans.
- **Numbers labeled as throughput claims** in vendor blogs (e.g., "Microsoft Copilot uses 400 000+ Dynamic Sessions per day") are *Microsoft's stated production usage*, not benchmarks you should rely on for capacity sizing. Run your own load tests with `readySessionInstances` tuned to peak.
- All pricing and feature statuses captured here reflect Microsoft documentation accessible up to **May 2026**. Pre-GA APIs (e.g., `2025-02-02-preview`, `2025-10-02-preview`, `2026-01-01`) are still subject to breaking change; pin to the GA `2025-07-01` API for production resource definitions where possible.