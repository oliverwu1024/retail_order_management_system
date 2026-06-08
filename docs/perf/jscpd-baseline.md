# jscpd Baseline — 2026-06-08

> Baseline copy/paste duplication scan for the Retail OMS repo at the end of
> **Phase 0 / Story 0.3**. Phase 10 (Task 10.3.2) re-runs the same scan with
> the same config and the result must show **≥45% reduction** in duplicated
> lines relative to the numbers below.

## How it was generated

```bash
cd src/web
pnpm jscpd-baseline
```

Config: [`src/web/.jscpd.json`](../../src/web/.jscpd.json)
Tool: `jscpd@5.x` (TypeScript + C# detector)

Auto-generated full report (HTML + markdown): [`jscpd-baseline-report/`](./jscpd-baseline-report/)

## Headline numbers

| Format     | Files | Total lines | Clones | Duplicated lines | % duplicated |
|------------|-------|-------------|--------|------------------|--------------|
| C#         | 11    | 1,955       | 3      | 286              | **14.63 %**  |
| TSX        | 11    | 580         | 0      | 0                | 0.00 %       |
| TypeScript | 5     | 402         | 0      | 0                | 0.00 %       |
| **Total**  | 27    | 2,937       | 3      | 286              | **9.74 %**   |

Token-level: 13.31 % duplicated tokens (1,412 / 10,610).

## Where the duplication lives

All three clones detected at baseline are in **EF Core auto-generated migration files**:

1. `Data/Migrations/20260608071305_0000_init.Designer.cs` ↔
   `Data/Migrations/RetailDbContextModelSnapshot.cs` — 267 lines.
   The model snapshot is by design a copy of each migration's recorded model
   state; EF Core regenerates it on every migration. **Not real duplication.**
2. Internal repeats inside `0000_init.Designer.cs` — 15 lines of Identity-table
   index boilerplate the EF scaffolder emits twice.
3. Internal repeats inside `0000_init.cs` — 7 lines of Identity-table
   `CreateTable` boilerplate.

**Hand-written application code at baseline: 0 % duplicated.** Both the
backend (controllers, services, middleware, interceptors, ApiResponse,
HealthController) and the frontend (components, store, router, apiClient)
register zero clones.

## Why we still publish a "noisy" baseline

The baseline is a contract for the Phase 10 comparison, not a clean-code
trophy. Two reasons we leave the migration noise in:

1. **The same config will run in Phase 10.** Excluding migrations now and
   then re-including them later would invalidate the comparison.
2. **The denominator grows.** As Phase 1–9 add tens of thousands of lines of
   real application code, the migration noise's share of the total naturally
   shrinks toward zero. That natural improvement is part of the 45 % story.

If at Phase 10 we want a per-domain breakdown (real code vs auto-generated),
we can add a `--silent` follow-up scan with `Migrations/` explicitly excluded
and compare those numbers separately.

## Phase 10 success criteria (Task 10.3.2)

Phase 10 re-runs `pnpm jscpd-baseline` and writes
`docs/perf/jscpd-final.md`. The headline gate is:

> **Total duplicated-line % ≤ 5.35 %** (a 45 % reduction from 9.74 %).

Soft signals to watch:
- C# real-code duplication should stay below ~3 %.
- TSX/TS duplication should stay below ~5 %.
- Any single clone > 50 lines outside `Migrations/` is a refactoring candidate.

## Versioning

This file is the durable contract. The folder `jscpd-baseline-report/`
contains the auto-generated artifacts and may be regenerated locally
without affecting the comparison — those numbers are reproduced in the
table above.
