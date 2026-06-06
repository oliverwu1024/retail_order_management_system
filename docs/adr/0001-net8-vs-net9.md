# ADR-0001: Target .NET 8 LTS for the Initial Build

**Status**: Accepted (2026-06-06)

**Deciders**: project owner

**Related**: PLAN.md §5 (Tech Stack) · `Directory.Build.props` · ADR-0005 (multi-provider LLM — TFM coupling)

---

## Context

We are building a long-lived portfolio project (~31–40 weeks scope) targeting an Azure-hosted .NET backend, beginning June 2026. As of project start, the available .NET runtimes are:

| Version  | Type | GA                                | End of Support              |
|----------|------|-----------------------------------|-----------------------------|
| .NET 6   | LTS  | Nov 2021                          | Nov 2024 (expired)          |
| .NET 7   | STS  | Nov 2022                          | May 2024 (expired)          |
| .NET 8   | LTS  | Nov 2023                          | Nov 2026                    |
| .NET 9   | STS  | Nov 2024                          | May 2026 (expired)          |
| .NET 10  | LTS  | Nov 2026 (scheduled, not yet GA)  | Nov 2028                    |

The build window runs roughly June 2026 → Mar 2027. .NET 10 LTS will GA partway through, but only the second half of the window could target it, and Azure Container Apps + Azure Functions runtime support typically lags .NET GA by weeks. Targeting a pre-release runtime against production-grade Azure services is operationally risky.

The chosen runtime must:

1. Be **currently supported** so deployment is not blocked.
2. Have **stable library ecosystem support** for our key dependencies (EF Core 8, ML.NET 4.x, Stripe.net, `Anthropic.SDK`, `Microsoft.Extensions.Http.Resilience`, Serilog 4.x).
3. Offer a **clean upgrade path** to the next LTS, since the project will outlive its initial runtime's support window.

## Decision

Target **`net8.0`** for every .NET project in the repo (API, ML, ML.Trainer, test projects). Lock the version centrally in the root `Directory.Build.props`:

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <LangVersion>latest</LangVersion>
</PropertyGroup>
```

Plan a clean bump to **.NET 10 LTS** post Nov 2026, once Azure Container Apps and Azure Functions runtimes both support it.

## Consequences

**Positive**

- All required libraries ship `net8.0`-compatible builds — no NuGet conflicts during the build window.
- Azure Container Apps and Azure Functions support `net8.0` `dotnet-isolated` natively (no preview-runtime risk).
- C# 12 features (collection expressions, primary constructors, alias-any-type) are available and sufficient for project expressiveness needs.
- Stable LTS support window covers the bulk of the build phase; deployment is unblocked.

**Negative / trade-offs**

- C# 13 features (`params` collections, `lock` object, `field` keyword) are unavailable until the .NET 10 bump.
- `Microsoft.Extensions.AI` (`IChatClient`) primarily targets newer TFMs. Mitigation: ADR-0005 ships our own `ILlmClient` whose DTOs deliberately mirror `IChatClient`'s shape, so the eventual migration is a mechanical swap rather than a rewrite.
- Runtime EOL Nov 2026 — slightly precedes the planned project completion. Mitigation: TFM lives in one place (`Directory.Build.props`); the migration to `net10.0` is a single-line repo-wide change plus CI / Container Apps image update.
- Holding back from early-adopting `Microsoft.Extensions.AI` itself means we don't yet benefit from its emerging tool-use and structured-output abstractions. Acceptable in exchange for dependency-surface stability across the migration.

## Alternatives considered

1. **.NET 9 (STS)**
   - Rejected: support ended May 2026, before the build start date. Targeting an out-of-support runtime is non-defensible — fails security review, precludes patches, and signals poor judgment to any reviewer.

2. **.NET 10 preview / RC builds**
   - Rejected: pre-release runtimes against Azure-hosted services are operationally risky. Container Apps and Functions typically lag .NET GA by weeks; targeting a preview pulls in an extra Azure-side compatibility coordination window during the build phase. The TFM gain does not justify the operational tax.

3. **Wait for .NET 10 GA (Nov 2026) before starting**
   - Rejected: delays project start by 5 months for no compensating engineering gain. The runtime bump is mechanical (one TFM line); the project's interview-defensible value is in architecture and code, not runtime currency. Timeline cost dominates.

4. **.NET 6 LTS**
   - Rejected: EOL Nov 2024 (already expired). Same defensibility issue as .NET 9, plus loss of C# 11 and 12 features. Strictly dominated.

## Implementation notes

- TFM is set **only** in root `Directory.Build.props` — never in individual `.csproj` files. The migration is one line.
- `LangVersion` is `latest` (not `12.0`), so the SDK picks the highest C# version it knows about. Bumping the SDK during the build window automatically picks up C# 12 service-pack improvements without an ADR.
- CI uses `actions/setup-dotnet@v4` pinned to `8.0.x` (latest patch); never floats to `9.0.x`.
- Azure Container Apps `containerImage` derives from `mcr.microsoft.com/dotnet/aspnet:8.0`. Azure Functions targets `dotnet-isolated` on host `~8`.
- All third-party `PackageReference` entries pin to versions that ship `net8.0` targets. Floating `*` versions are forbidden; Central Package Management (`Directory.Packages.props`, Story 0.2) locks majors.
- No compile-time guard is necessary here (unlike ADR-0005's `using Anthropic.SDK` rule) — MSBuild itself fails the build if a project disagrees with the parent `TargetFramework`.

## Revisit triggers

- **.NET 10 GA + Azure Container Apps and Azure Functions both confirm `net10.0` support.** → New ADR proposing the bump; supersedes ADR-0001. Coordinate with the ADR-0005 migration of `ILlmClient` → `IChatClient`.
- **A required library drops `net8.0` support during the build window** (e.g. a future ML.NET release, a `Microsoft.Extensions.AI` first-party Anthropic adapter we want). → Evaluate pinning to the last `net8.0`-supporting version vs. fast-tracking the .NET 10 bump.
- **A CVE in the .NET 8 runtime requires a patch unavailable on the LTS branch.** → Out-of-band patch via newer image tag; no ADR change unless we're forced off LTS entirely.
