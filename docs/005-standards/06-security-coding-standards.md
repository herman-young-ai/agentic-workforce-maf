# Security Coding Standards

Daily security rules for C# development on this platform.
Cross-references: ADR-007 (Identity), ADR-008 (Audit), 005-security-design.md (40 controls).

## 1. Authentication & Authorisation

- Every endpoint must declare auth posture explicitly: `[Authorize]` (default) or `[AllowAnonymous]` (with comment)
- Never do authorisation inside business logic — enforce at the endpoint boundary
- Always check resource ownership (BOLA): verify user is a ProjectMember before accessing project resources
- Check project-level role, not just platform role: a PlatformAdmin is NOT automatically an Owner of every project

## 2. Input Validation

- Validate at API boundary only (DataAnnotations on request DTOs)
- Trust validated data inside services — don't re-validate
- Never trust query string or route values for security decisions — use authenticated identity
- Sanitise all user input before injecting into agent prompts (AR-3: prompt injection defence)

## 3. SQL / EF Core Safety

- Always use parameterised queries (LINQ or `FromSql(FormattableString)`)
- Never `FromSqlRaw` with concatenated user input
- Never expose raw database errors to clients (GlobalExceptionHandler maps to `SYS_DATABASE_ERROR`)
- Dynamic identifiers (table names, column names) must come from an allow-list, never from user input

## 4. Secrets Management

- No secrets in source code — Key Vault (prod), `appsettings.Development.json` (dev)
- Gitignored: `appsettings.Local.json`, `*.pfx`, `*.pem`, `*.key`, `.env*`
- Pre-commit: `gitleaks protect --staged` blocks secrets
- Fail-fast: missing required config at startup throws `InvalidOperationException`
- API keys stored as SHA-256 hash — plaintext shown once at creation, never stored

## 5. Logging Security

Never log:
- Tokens (JWT, API keys, refresh tokens)
- Passwords or password hashes
- Full request/response bodies on sensitive endpoints
- Connection strings with credentials

Always log:
- Auth success (Debug level)
- Auth failure (Warning level)
- Authorisation denial (Warning level)
- Unhandled exceptions (Error level)
- User mutations — create, update, delete (Information level)
- Rate limit hits (Warning level)
- Agent execution start/complete (Information level)

PII masking is automatic via Serilog.Enrichers.Sensitive (emails, IBANs).

## 6. Dangerous Code Patterns — Forbidden

| Pattern | Risk | Alternative |
|---------|------|-------------|
| `Process.Start` with user input | OS command injection | Execute in Dynamic Sessions sandbox |
| `Type.GetType` with user input | Reflection injection | Use typed dispatch |
| `JsonSerializer.Deserialize` with `TypeNameHandling.All` | Remote code execution | Use typed deserialization |
| `XmlReader` with `DtdProcessing.Parse` | XXE | `DtdProcessing.Prohibit` |
| Disabling HTTPS validation | MitM | Never disable in any environment |
| `Regex` on user input without timeout | ReDoS | `new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1))` |
| `innerHTML` in React | XSS | Use JSX (auto-escaped) |

## 7. Agent-Specific Security

- All tools touching network/filesystem run in ACA Dynamic Sessions (Principle 22)
- API keys injected as session environment variables — never passed through LLM output
- Azure tokens acquired by Worker, forwarded as short-lived bearer tokens to sessions
- Agent-extracted learnings default to `Pending` — human gate before activation (ADR-014)
- Tool call rate limiting per execution with configurable ceiling (AR-8)
- Prompt injection defence: escape XML-like tags, injection detection middleware (AR-3)

## 8. HTTP Security Headers

Configured globally in `Program.cs` via `NetEscapades.AspNetCore.SecurityHeaders`:

- `Strict-Transport-Security: max-age=31536000; includeSubDomains`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Frame-Options: DENY`
- `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; form-action 'none'`
- Server header removed

## 9. Dependency Security

- `NuGetAudit` enabled in `Directory.Build.props` — NU1902+ warnings on vulnerable packages
- `packages.lock.json` + `--locked-mode` in CI prevents dependency tampering
- Monthly dependency updates
- Container base image: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (non-root, minimal)

## 10. Pre-Commit Enforcement

1. `gitleaks protect --staged` — secret scanning
2. `dotnet build` — Roslyn security analyzers (CA2100, CA3xxx, CA5xxx as errors)
3. `dotnet test` — all tests pass
4. CQI score ≥ 70 — code quality gate
