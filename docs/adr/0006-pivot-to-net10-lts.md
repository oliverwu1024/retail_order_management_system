# ADR-0006: Pivot to .NET 10 LTS — Supersedes ADR-0001

**Status**: Accepted (2026-06-06)

**Deciders**: project owner

**Supersedes**: ADR-0001 (Target .NET 8 LTS for the Initial Build)

**Related**: `Directory.Build.props` · `global.json` (not added — single SDK on host) · ADR-0005 (Multi-Provider LLM Abstraction) · PLAN.md §5 (Tech Stack) · `tech_decisions.md`

---

## Context

ADR-0001 was authored on 2026-06-06 under the assumption that **.NET 10 LTS was scheduled for Nov 2026 GA and not yet available**. That assumption was incorrect. On the same date, while initializing the .NET solution skeleton (Story 0.2 / Task 0.2.1), the host machine reported:

```
$ dotnet --list-sdks
10.0.108 [/usr/lib/dotnet/sdk]
```

.NET 10 LTS in fact GA'd in **November 2025** (one release cycle earlier than ADR-0001 stated) and is supported through **November 2028**. SDK 10.0.108 reflects ~7 months of post-GA service-pack patches. .NET 8 SDK is not installed on the host.

`dotnet new` templates from the .NET 10 SDK default to `<TargetFramework>net10.0</TargetFramework>`, which overrode the `net8.0` value set in `Directory.Build.props`. Every project created in Task 0.2.1 targets `net10.0` as a result.

This forces a real choice between two paths:

1. **Install .NET 8 SDK side-by-side**, pin it with `global.json`, force-regenerate every `.csproj` to `net8.0`, and plan a future .NET 10 LTS migration as ADR-0001 described.
2. **Accept the reality on the host** — adopt .NET 10 LTS now, given it is the destination ADR-0001 was already planning a migration to.

The original ADR-0001 reasoning still tells us which to pick. Its core argument was: pick the runtime that is **currently supported, has a stable library ecosystem, and offers a clean upgrade path**. Today, **.NET 10 LTS satisfies all three** for the same reasons .NET 8 LTS did seven months ago:

- Currently supported (through Nov 2028 — three more years than .NET 8's Nov 2026 EOL).
- Library ecosystem has had ~7 months of post-GA stabilization; all our locked dependencies (EF Core 10, ML.NET, Stripe.net, `Anthropic.SDK`, `Microsoft.Extensions.Http.Resilience`, Serilog) ship `net10.0`-compatible targets.
- The clean upgrade path is **no migration at all** — we ship on the LTS the project would have migrated to anyway, avoiding doing the migration twice.

## Decision

**Adopt `.NET 10 LTS` as the target framework for every .NET project in the repo** (`Retail.Api`, `Retail.Ml`, `Retail.Ml.Trainer`, `Retail.Tests.Unit`, `Retail.Tests.Integration`). Lock the version centrally:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>latest</LangVersion>
</PropertyGroup>
```

**Mark ADR-0001 as `Superseded by ADR-0006 (2026-06-06)`.** Do not delete or edit its body — ADRs are immutable historical record. The status change is the only acceptable retroactive edit.

**Do not add `global.json` at this time.** Only one SDK is installed on the host (.NET 10), so SDK pinning is redundant. Add `global.json` only if `.NET 11` or a future SDK is later installed side-by-side and produces template-default surprises.

**No change to ADR-0005's decision** (multi-provider `ILlmClient` wrapper). ADR-0005 anticipated `Microsoft.Extensions.AI` (`IChatClient`) as the eventual migration target post-.NET 10. With .NET 10 now in scope, that migration **may** happen sooner — but only if `Microsoft.Extensions.AI` adapters for `Anthropic.SDK` are first-party and mature. The `ILlmClient` wrapper continues to insulate `CopyGenService` and `ChatService` from that decision.

## Consequences

**Positive**

- **No double migration.** Ship on the LTS that ADR-0001 was planning to migrate to anyway. Net engineering effort avoided: one .NET upgrade cycle.
- **Three more years of LTS runway.** .NET 10 LTS support extends to Nov 2028 vs .NET 8's Nov 2026 — comfortably past any realistic completion date for this project and well into its post-portfolio maintenance window.
- **C# 14 features** (e.g. `field` keyword, expanded `params` collections, extension members) are available immediately. Modest expressiveness gains; not load-bearing.
- **`Microsoft.Extensions.AI` (`IChatClient`) is a current TFM**, not a future one. If/when adapters mature, ADR-0005's migration becomes "swap the implementation" instead of "swap the runtime and then the implementation."
- **One SDK on host** means no `global.json` complexity and no risk of "works on my machine because I have SDK X."
- **The `Microsoft.CodeAnalysis.NetAnalyzers` reference and other version-specific dependencies move to their `10.x` line**, matching the runtime — fewer "out-of-band analyzer with the wrong language version" friction points.

**Negative / trade-offs**

- **ADR-0001 is now historical.** It documents reasoning that the project no longer follows. We keep it for audit trail but every reader has to navigate to ADR-0006 to learn the current decision. Mitigation: ADR-0001's `Status` line directs the reader here.
- **Doc-edit blast radius.** README, PLAN.md, `Directory.Build.props`, `tech_decisions.md` memory, and all `.csproj` files contain version mentions that must change in one sweep. Mitigation: handled in a single batch in this same session.
- **StackOverflow / book corpus lag.** Material specific to .NET 10 features is less mature than .NET 8 material. Mitigation: most ASP.NET MVC, EF Core, and Identity material remains version-agnostic; .NET-10-specific questions are rare for the patterns this project uses.
- **`Microsoft.Extensions.AI` GA-or-near-GA for our LLM use cases is now in scope.** This is a near-term temptation to adopt it directly in ADR-0005 — which we explicitly defer. The `ILlmClient` wrapper still pays for itself in testability, telemetry, and provider-swap clarity even if `IChatClient` becomes viable tomorrow.
- **`Microsoft.CodeAnalysis.NetAnalyzers` version may need a guess-and-verify bump** (8.0.0 → 10.0.x). If the chosen `10.x` version isn't on NuGet, restore fails fast and we correct.

## Alternatives considered

1. **Install .NET 8 SDK side-by-side and stay on `net8.0` per ADR-0001**
   - Rejected. Adds an SDK-install step on the dev machine, requires `global.json` to disambiguate, forces deletion and regeneration of every project just created in Task 0.2.1, and **commits to a .NET 10 LTS migration later this year anyway** — a migration that we have done zero engineering work to put off and have every reason to skip. The original argument for .NET 8 (".NET 10 not yet GA") no longer holds.

2. **Stay on `.NET 8` for the build window, defer the migration to post-Nov 2026 per ADR-0001**
   - Rejected. Same problem as (1) plus an extra cost: .NET 8 LTS support ends Nov 2026, which is **during** the build window. We would be running an out-of-support runtime for the tail of the project, or doing a rushed migration mid-build. Both are worse than pivoting now.

3. **.NET 10 preview / RC streams**
   - Not applicable. .NET 10 is GA, not preview. The "preview risk" concern in ADR-0001's alternatives list is moot.

4. **Skip LTS and use `.NET 11` STS when it ships**
   - Rejected for the same reason ADR-0001 rejected .NET 9 STS — STS support windows (18 months) do not cover this project's expected post-build maintenance horizon. LTS is the right cadence.

## Implementation notes

- `Directory.Build.props`: change `<TargetFramework>net8.0</TargetFramework>` → `<TargetFramework>net10.0</TargetFramework>`. Update the adjacent comment from "C# 12" to "C# 14". Bump `Microsoft.CodeAnalysis.NetAnalyzers` from `Version="8.0.0"` to a current `10.x` (verify on NuGet at apply time).
- `.csproj` files (auto-generated by .NET 10 templates) already specify `<TargetFramework>net10.0</TargetFramework>`. No edit needed; the `Directory.Build.props` change is for consistency / future-template-regeneration.
- `global.json` deliberately **not** added.
- README.md: 4 string substitutions (".NET 8 SDK" → ".NET 10 SDK"; "ASP.NET 8" → "ASP.NET 10"; "Entity Framework Core 8" → "Entity Framework Core 10"; "ASP.NET Core 8 (LTS)" → ".NET 10 LTS, ASP.NET Core MVC").
- `docs/PLAN.md`: 5 lines updated (Tech Stack row, architecture diagram, folder-layout comment, Backend bullet, locked-decision table row).
- `tech_decisions.md` memory: the locked Backend line flips to .NET 10 LTS with a flip-date and reference to this ADR.
- CI (`actions/setup-dotnet@v4`) pins to `10.0.x`; Container Apps image becomes `mcr.microsoft.com/dotnet/aspnet:10.0`; Azure Functions targets host `~10` once Microsoft enables it (verify at Phase 8).
- ADR-0001 receives **only** a `Status` line change; body stays intact as historical record.

## Revisit triggers

- **`Microsoft.Extensions.AI` ships a mature first-party `IChatClient` adapter for `Anthropic.SDK`.** → New ADR proposing migration of `ILlmClient` → `IChatClient`; possibly supersedes ADR-0005.
- **A required library drops `net10.0` support or has no `net10.0`-compatible build at the time of dependency add.** → Evaluate pinning to the last compatible major or substituting an alternative; do not regress to `net8.0`.
- **A CVE in the .NET 10 runtime requires a patch unavailable on the LTS branch.** → Out-of-band patch via newer image tag; no ADR change unless we're forced off LTS entirely.
- **.NET 11 STS ships and offers a feature the project measurably needs** (unlikely for our scope). → Re-evaluate LTS-vs-STS cadence; default remains LTS.
