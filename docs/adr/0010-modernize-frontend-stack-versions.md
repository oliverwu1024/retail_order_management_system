# ADR-0010: Modernize Frontend Stack Versions (React 19, Vite 8, TS 6, React Router v7)

**Status**: Accepted (2026-06-08)

**Deciders**: project owner

**Supersedes**: none (refines PLAN.md §5 Tech Stack — Frontend)

**Related**: PLAN.md §5 (Tech Stack) · `tech_decisions.md` · REQUIREMENTS.md Task 0.3.1 · ADR-0006 (the analogous backend-side acceptance of newer-than-planned versions)

---

## Context

PLAN.md §5 specified the frontend stack as:

> **React 18.3 + Vite 5 + TypeScript 5.6**
> **React Router v6.4+ data router**
> **Tailwind CSS 3.4** + **shadcn/ui**
> **TanStack Query v5** + **Zustand 4**

Those numbers reflect tooling state when PLAN.md was authored. When Story 0.3 / Task 0.3.1 ran `pnpm create vite@latest src/web --template react-ts`, the latest-default scaffold installed:

| Library         | PLAN.md spec | Actually installed (2026-06-08) |
|-----------------|--------------|----------------------------------|
| React           | 18.3         | **19.2.6**                       |
| Vite            | 5            | **8.0.16**                       |
| TypeScript      | 5.6          | **6.0.3**                        |
| React Router    | v6.4+        | **v7.17.0**                      |
| Tailwind CSS    | 3.4          | 3.4.19 (pinned by REQUIREMENTS)  |
| TanStack Query  | v5           | 5.101.0                          |
| Zustand         | 4            | **5.0.14**                       |

Six of the seven major libraries are newer than PLAN.md's target. This forced a real choice between two paths:

1. **Pin everything back to PLAN.md's numbers**: rewrite the scaffold to install React 18.3 / Vite 5 / TS 5.6 / RR v6.4 / Zustand 4. Fight create-vite's defaults on every future scaffolding step.
2. **Accept the modernized stack** and update PLAN.md.

The decision is structurally analogous to the .NET 8 → .NET 10 acceptance in **ADR-0006**: the tooling has moved forward past PLAN.md's snapshot, the newer versions are stable, and downgrading would mean fighting defaults forever for no measurable benefit.

The Tailwind pin (3.4 not 4) is **separate** from this drift and is preserved — REQUIREMENTS Task 0.3.2 explicitly names `Tailwind CSS 3.4`, and shadcn/ui's v3-vs-v4 migration is a real architectural difference (PostCSS plugin vs Vite plugin, theme tokens move from CSS variables to `@theme` directives). 3.4 stays.

## Decision

**Adopt the modernized frontend stack as installed**:

- **React 19.2** (was: 18.3)
- **Vite 8** (was: 5)
- **TypeScript 6.0** (was: 5.6)
- **React Router v7** (was: v6.4+)
- **Zustand 5** (was: 4)
- **TanStack Query 5.101** (was: v5 — minor bump, no real drift)
- **Tailwind CSS 3.4.19** ← **explicitly stays at 3.x per REQUIREMENTS Task 0.3.2**

**Update PLAN.md §5 Frontend bullets** to reflect these numbers. PLAN.md is a living document, not an ADR — version bumps belong in PLAN.md edits, not in the ADR text (which freezes at decision time).

**Update `tech_decisions.md` memory** with the frontend version pins so future sessions see the current target without re-reading PLAN.md.

**Do not pin specific patch versions in `package.json`.** Caret ranges (`^19.2.6`) match the project's "small team, no platform lock-in" posture. If the React or Vite teams ship a breaking patch, that's a separate Issue.

**Document the choice to write the four shadcn primitives by hand** (rather than via `pnpm dlx shadcn`) as part of this ADR's implementation notes, since the rationale couples to React 19 / Tailwind 3.4 / shadcn-CLI-defaults-to-Tailwind-4 reality.

## Consequences

**Positive**

- **No tooling fight on every scaffold**. `pnpm create vite`, `pnpm dlx` defaults, the latest `create-react-router` template — they all assume the current major. Pinning back would mean a custom-template fork or per-command flag soup.
- **React 19 wins are non-cosmetic**: `useTransition` for non-blocking form submits, `useOptimistic` for cart drawer responsiveness (Phase 2), Actions API for form submission flows (Phase 1–2 auth + checkout). All directly applicable to upcoming phases.
- **Vite 8 has measurably faster HMR and a smaller bundle** than Vite 5 in our `pnpm build` output (368 kB / 116 kB gzip for the skeleton — competitive with hand-tuned Vite-5 setups).
- **TS 6 deprecates `baseUrl`** in favor of `paths`-only resolution. We adopted the new shape during Story 0.3 setup — one less migration to remember later.
- **React Router v7 IS the data router architecturally**. v6.4 was the introduction; v7 is the consolidated API. Starting on v7 means no migration when v6 reaches EOL.
- **Zustand 5 collapsed some legacy types** and removed Function-form `set` quirks. Type inference is sharper on the auth-store skeleton we already wrote.
- **Aligns with ADR-0006's posture**: accept the host environment's reality, document the drift, move on. Consistency reduces cognitive load.

**Negative / trade-offs**

- **PLAN.md doc-edit blast radius**. The §5 Frontend block, the architecture diagram caption (if it mentions versions), and any tutorial-style README sections need version-string updates. Mitigation: handled in the same session as this ADR is filed.
- **Tutorial / StackOverflow lag.** React 19 material is one year old; React 18 material is four. For the auth flow + form patterns we're about to write in Phase 1, this is mild — almost everything still works with `useState` + React Hook Form. The friction lives in the Suspense / Actions territory.
- **Risk of React 19 quirks under StrictMode**. React 19's double-render in dev is more aggressive than 18's. We may catch latent useEffect-with-side-effect bugs sooner; that's mostly a feature, occasionally a sharp edge.
- **Zustand 5 breaking changes** (the `setState` API tightening) mean any pre-existing Zustand-4 tutorial snippets need light adaptation. We have zero Zustand-4 code to migrate (Story 0.3 wrote against 5 from the start), so this is hypothetical.
- **shadcn/ui CLI now defaults to Tailwind 4 components.** We deliberately wrote the four Story-0.3 primitives by hand to match Tailwind 3.4 conventions, not the CLI output. Future shadcn component additions (Select, Modal, Drawer, DataTable, etc.) require the same hand-write discipline — or a Tailwind 4 pivot, which would be a separate ADR.
- **`Microsoft.OpenApi 2.x` namespace flattening** caught us at backend build time. Unrelated to this ADR but a parallel example of the "newer is destination, not free upgrade" reality we're accepting.

## Alternatives considered

1. **Strict downgrade to PLAN.md's exact specs (React 18.3, Vite 5, TS 5.6, RR v6.4)**
   - Rejected. Requires deleting the Story 0.3 scaffold and re-creating it with `create-vite@5` (a specific old major), then manual downgrades for each library. Creates a permanent maintenance burden: every `pnpm create` invocation in this repo would need version flags, and every future scaffold (e.g. Storybook setup, Playwright init) would risk pulling in versions the rest of the stack can't tolerate. The original PLAN.md targets were a snapshot, not a contract.

2. **Modernize EVERYTHING including Tailwind to 4.x**
   - Rejected. REQUIREMENTS Task 0.3.2 explicitly names "Tailwind CSS 3.4". Tailwind 4 changes the theme-token model (`@theme` directive in CSS, no `tailwind.config.ts`), the PostCSS-vs-Vite-plugin architecture, and shadcn template assumptions. That's a separate decision that deserves its own ADR with the cost/benefit explicitly weighed.

3. **Adopt a meta-framework (Next.js, Remix, TanStack Start)**
   - Out of scope for this ADR. PLAN.md commits to Vite + React Router data router as the SPA architecture. A meta-framework pivot would touch the deployment model (SWA vs Container Apps for the web), routing model, data-loading model, and auth model. If revisited, that's its own ADR.

4. **Lock to `^` ranges with `engines.node` pinning and a renovate-bot policy**
   - Deferred. We use `^` ranges by default; a renovate / dependabot policy belongs in the CI ADR (forthcoming, Story 0.5). Out of scope here.

## Implementation notes

- **PLAN.md §5 Frontend bullets** updated in the same commit as this ADR:
  - "React 18.3 + Vite 5 + TypeScript 5.6" → "**React 19.2 + Vite 8 + TypeScript 6.0**"
  - "React Router v6.4+ data router" → "**React Router v7 data router**"
  - "Zustand 4" → "**Zustand 5**"
  - **Tailwind CSS 3.4 unchanged** (intentional pin, preserved)
- **`tech_decisions.md` memory** gains a brief "frontend version pins" sentence pointing here.
- **`src/web/package.json`** already reflects the new versions (set up during Story 0.3) — no change.
- **`src/web/tsconfig.app.json`** already removed deprecated `baseUrl` in favor of `paths`-only — no change.
- **Story-0.3 shadcn primitives** (Button, Input, Card, Toast/Toaster + use-toast) are documented as "hand-written, not CLI-generated" in this ADR so future component additions follow the same pattern instead of `pnpm dlx shadcn add`.
- **No code change required by this ADR.** The decision codifies what was already executed in Story 0.3 and updates PLAN.md to match reality.

## Revisit triggers

- **Tailwind 4 ships a feature that materially blocks us** (e.g., a Radix-required CSS feature only available via Tailwind 4's `@theme`). → New ADR proposing Tailwind 4 migration; would touch every component in `src/components/ui/`.
- **React 20 ships with a breaking change** within the project's build window (Phase 1–10). → Evaluate pin-to-19 vs migrate; default is to migrate.
- **`create-vite` defaults to Vite 9** during a future scaffold. → Bump in place; no ADR needed unless Vite 9 introduces a breaking config shape.
- **A Story-0.3 component pattern proves unviable in production usage** (e.g., the Toaster swipe gesture breaks on a specific browser). → Component-level fix, not ADR-worthy.
- **Meta-framework pivot is requested** (Next.js / TanStack Start). → New ADR; this one stands as the SPA architecture's last word.
