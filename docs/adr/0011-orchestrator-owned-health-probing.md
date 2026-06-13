# ADR-0011: Orchestrator-Owned Health Probing (No Docker HEALTHCHECK)

**Status**: Accepted (2026-06-13)

**Deciders**: project owner

**Related**: `docker/api/Dockerfile` · `docker/docker-compose.yml` · `src/api/Retail.Api/Program.cs` (`/health/live`, `/health/ready`) · `infra/bicep/modules/containerApps.bicep` (Azure probes) · ADR-0006

---

## Context

Story 0.4 shipped a Docker `HEALTHCHECK` (and a mirrored compose `healthcheck`) that probe `/health/live` with `wget`. The Phase 0 docker end-to-end verification (2026-06-13) caught two problems:

1. **The probe can't run.** `mcr.microsoft.com/dotnet/aspnet:10.0` is a minimal runtime image that ships **neither `wget` nor `curl`** (`/bin/sh: 1: wget: not found`). The API served fine — only the probe was broken — but the container reported **`unhealthy`** forever, and the optional `web` service gates on `api: condition: service_healthy`, so it would never start.
2. **The probe is redundant in production anyway.** The same Dockerfile builds the prod image, whose target is **Azure Container Apps**. Container Apps — like Kubernetes — runs its own HTTP liveness/readiness probes and **ignores the Dockerfile `HEALTHCHECK`**. Anything we install to satisfy the Docker probe (e.g. `curl`) is dead weight and extra attack surface in the image where it is never used.

The naive fix (install `curl`/`wget` into the runtime image) is common but contradicts both the image's minimal-surface goal and the fact that the orchestrator owns probing.

## Decision

**Health is owned by the orchestrator, not the image.**

- **Remove the Docker `HEALTHCHECK`** from `docker/api/Dockerfile` and the mirrored `healthcheck` from the `api` compose service.
- **Do not install `curl`/`wget`** into the runtime image to power a probe the orchestrator ignores; keep the minimal runtime/attack surface.
- **Production:** Azure Container Apps HTTP probes (defined in Bicep) target the `/health/live` (liveness) and `/health/ready` (readiness) endpoints already built in Story 0.2.
- **Local:** gate `web` on `service_started` instead of `service_healthy`. The Vite dev server proxies API calls on demand and tolerates a brief API warm-up. The `/health/*` endpoints remain available for manual `curl` checks.

## Consequences

**Positive**

- Minimal runtime image stays minimal — no `curl` CVE surface added for a check prod never runs.
- No more false `unhealthy`; `docker compose up` no longer blocks `web` behind an unpassable probe.
- Health semantics live in exactly one place per environment: Bicep probes (prod), compose `depends_on` + `/health/*` endpoints (local).
- Clean, defensible story: orchestrated apps delegate liveness/readiness to the platform.

**Negative / trade-offs**

- `docker ps` no longer shows a green `(healthy)` for `api` locally — just `Up`. Liveness is still verifiable via `curl /health/live`.
- `service_started` is a weaker local gate than `service_healthy` — `web` may start a second before the API is ready. Negligible for a proxying dev server.
- A future non-orchestrated target (plain `docker run`, Swarm) would need an in-image probe — see revisit triggers.

## Alternatives considered

1. **Install `curl`/`wget` in the runtime stage, keep the `HEALTHCHECK`.** Rejected — bloats the prod image and adds attack surface for a check Container Apps ignores; contradicts the minimal-image goal.
2. **`dotnet`-based self-probe** (a `dotnet Retail.Api.dll health` early-args branch as the `HEALTHCHECK` CMD). Viable and adds no OS packages, but puts probe code in the composition root for a local-only convenience. Deferred unless a non-orchestrated deploy target appears.
3. **Keep `service_healthy`, fix only the compose check.** Rejected — the compose check runs inside the same toolless image, so it hits the identical `wget` problem, and it leaves the redundant prod `HEALTHCHECK` in place.

## Revisit triggers

- A deployment target **without** orchestrator HTTP probes (plain `docker run`, Docker Swarm) → add the `dotnet`-based self-probe (alternative 2).
- A base-image change that includes a probe tool, or a Microsoft-provided health utility for chiseled images → reassess.
