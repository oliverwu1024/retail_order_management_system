# ADR-0002: No MediatR — Direct Service Classes for In-Process Orchestration

**Status**: Accepted (2026-06-06)

**Deciders**: project owner

**Related**: PLAN.md §5 (Tech Stack), §6 (Architecture — three-tier) · CODING_STANDARDS § Three-Tier Discipline · ADR-0004 (MVC Controllers)

---

## Context

ASP.NET projects in the .NET ecosystem commonly use **MediatR** to introduce a mediator between controllers and business logic, enabling a CQRS-lite handler pattern (`IRequest<TResponse>` → `IRequestHandler<TRequest, TResponse>`). It also offers a pipeline-behavior abstraction for cross-cutting concerns (validation, logging, transactions) and `INotification` for in-process pub/sub.

Two facts shape this decision:

1. **MediatR went commercial in September 2025.** Production use by an organization with > USD 1M revenue requires a paid licence. This is moot for a portfolio project, but signals that future maintainers must check licensing before re-using patterns.
2. **The project locked a three-tier architecture** (ADR-0004 + `tech_decisions.md`): Controllers → Services → Repositories, single `Retail.Api` project, folder-based separation. MediatR's value is highest when handler graphs are wide and reused across entry points (HTTP + jobs + events). Here, the graph is narrow — most services are called from one controller, sometimes one background worker.

We also need three things that MediatR commonly provides:

- **Cross-cutting concerns** (validation, logging scope, transaction boundary).
- **In-process domain events** (e.g. "order placed" handlers that must run inside the same transaction).
- **Cross-service async work** (e.g. send confirmation email after order, recompute loyalty points).

Each is addressable without a mediator.

## Decision

Do **not** take a dependency on MediatR (or a homegrown equivalent). Use **direct service classes**:

- Controllers depend on **per-domain service interfaces** (`ICatalogService`, `IOrderService`, etc.) injected by ASP.NET DI.
- Services depend on **per-aggregate repository interfaces** (`IOrderRepository`, etc.) and other services.
- DI registration lives in **per-folder extension methods** (`AddCatalogServices`, `AddOrderServices`, …) in `ServiceCollectionExtensions.cs`, composed in `Program.cs`.

For the concerns MediatR would have covered:

- **Validation** → FluentValidation, registered globally; failures become 400 ProblemDetails via the MVC filter pipeline.
- **Logging scope** → Serilog `LogContext.PushProperty` per request (correlation ID, user ID, route) added by middleware.
- **Transaction boundary** → `IUnitOfWork` wrapping EF Core `SaveChangesAsync`, called from the service layer.
- **In-process domain events** (same transaction) → lightweight `IDomainEventDispatcher` invoked at the end of `SaveChangesAsync` (interceptor); handlers registered as `IDomainEventHandler<TEvent>`.
- **Cross-service async work** (different transaction, possibly cross-process) → Azure Service Bus + Azure Functions (locked in `tech_decisions.md`). Never an in-process `MediatR.Notification`.

## Consequences

**Positive**

- Stack traces are direct: controller → service → repository. No handler-discovery indirection to read past during debugging.
- Call graphs are visible in the IDE — "Find usages" on a service method gives the truth.
- One fewer third-party dependency to learn deeply enough to defend in interview; no NuGet-version churn.
- Resume / interview narrative: *"explicit three-tier with constructor DI"* is the dominant industry pattern, defensible without library appeals.
- Cross-cutting concerns sit in the right place: validation in MVC filters, logging in middleware, transactions in the service layer. Each is independently testable.
- No commercial-licence question if any portion of the project is later forked into a paid product.

**Negative / trade-offs**

- More verbose per endpoint than MediatR's `request → handler` pattern. Acceptable: services average 1–3 repository dependencies, not 10+.
- We lose MediatR's pipeline-behavior abstraction. Mitigation: each concern has a different idiomatic .NET home (filters, middleware, EF interceptors, source-generated decorators) — not one abstraction. Trade-off favours clarity over uniformity.
- No built-in `INotification` for in-process pub/sub. Mitigation: `IDomainEventDispatcher` covers same-transaction events; cross-process events go through Service Bus. Both fit the eventual Phase 8 event-driven story.
- Slightly more DI wiring per new service. Mitigation: per-folder extension methods keep `Program.cs` flat.

## Alternatives considered

1. **MediatR for CQRS-lite handler pattern**
   - Rejected: licence cost is moot for a portfolio project but the indirection cost is real — handler discovery obscures call graphs at a scale where the abstraction does not pay back. Adds a dependency we must defend in interviews against the question *"why MediatR at 20 endpoints?"*

2. **Wolverine or MassTransit for in-process mediation + transport**
   - Rejected: heavy frameworks that overlap with Service Bus + Functions (already in stack for transport). Their in-process mediator features come bundled with transport features we already have. Net adds learning and dependency surface without removing any.

3. **Homegrown `IRequestHandler<TRequest, TResponse>` pattern**
   - Rejected: same downsides as MediatR (indirection, discovery magic) with no community familiarity benefit. Classic NIH.

4. **CQRS proper (separate read/write models + write-side handlers)**
   - Rejected for this scale. The project does not have query/command divergence severe enough to justify two models per aggregate. Revisit when reads measurably constrain writes; not before.

## Implementation notes

- DI extension methods live in `Retail.Api/Extensions/ServiceCollectionExtensions.cs`; one extension per domain folder. Composition in `Program.cs` is `services.AddCatalogServices().AddOrderServices()...`.
- Service interfaces in `Retail.Api/Services/<Domain>/I<X>Service.cs`; implementations alongside.
- `IUnitOfWork` wraps `DbContext.SaveChangesAsync`; injected into services, called once per public service method.
- `IDomainEventDispatcher` collects events on entities during the request, drains them in an EF `SaveChangesInterceptor` after `SavingChanges` and before `SavedChanges`. Handlers resolved from DI as `IEnumerable<IDomainEventHandler<TEvent>>`.
- FluentValidation: validators in `Retail.Api/Validators/`, registered via `AddValidatorsFromAssemblyContaining<Program>()`. MVC pipeline auto-runs them; failures map to 400 ProblemDetails.
- Cross-service async work hands off via Service Bus message published inside the transaction (outbox pattern, Phase 8). In-process domain events never used for cross-aggregate transactional consistency.

## Revisit triggers

- **Service count grows beyond ~50 with significant cross-calls and wide handler graphs.** → Re-evaluate whether a mediator (MediatR or otherwise) would reduce wiring noise.
- **Need for a pipeline behavior that does not fit MVC filters, middleware, or EF interceptors.** → Evaluate a targeted decorator pattern via Scrutor or source generation before adopting a mediator.
- **Project pivots to Minimal APIs (overturns ADR-0004).** → Reassess; Minimal APIs + handler dispatch has different ergonomics that may shift the balance.
- **CQRS write/read divergence becomes measurable** (e.g. read model is denormalized enough to need its own projection pipeline). → Adopt CQRS pattern; mediator may follow.
