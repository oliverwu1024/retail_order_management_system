# ADR-0004: MVC Controllers Over Minimal APIs

**Status**: Accepted (2026-06-06)

**Deciders**: project owner

**Related**: PLAN.md §5 (Tech Stack), §6 (Architecture — three-tier) · CODING_STANDARDS § API Conventions · ADR-0002 (No MediatR — controllers as composition root)

---

## Context

ASP.NET Core 8 offers two HTTP API authoring styles:

- **MVC Controllers** — class-per-domain action containers, attribute routing (`[Route]`, `[HttpGet]`), rich filter pipeline (`IActionFilter`, `IAsyncResultFilter`, etc.), model binding, action conventions, and the `[ApiController]` attribute that adds automatic 400 model validation and `ProblemDetails` responses.
- **Minimal APIs** — endpoint-as-lambda registered against `WebApplication` (`app.MapPost("/orders", ...)`). Lower ceremony per endpoint, slightly better cold-start and throughput, AOT-friendly. Introduced in .NET 6.

The project's surface area is non-trivial:

- ~20+ endpoints at MVP across Auth, Catalog, Inventory, Cart, Orders, Promotions, Chatbot, ML, Admin.
- Many endpoints share cross-cutting concerns: authorization, model validation, anti-forgery, response shaping, structured logging.
- Three-tier architecture (ADR-0002) makes the controller the composition root for each action — DI'd services are the dependencies, controllers are thin orchestrators.
- The project owner's prior ASP.NET experience is tutorial-level on Controllers, not Minimal APIs.

## Decision

Use **MVC Controllers with `[ApiController]`** and attribute routing for **all** HTTP endpoints owned by `Retail.Api`. Single project, organized by domain folder (`Controllers/Catalog/CatalogController.cs`, `Controllers/Orders/OrdersController.cs`, etc.).

**Carve-outs** (Minimal APIs permitted, narrowly):

- Diagnostics endpoints registered directly in `Program.cs`: `GET /health`, `GET /health/ready`, `GET /version`. These are deliberately framework-free, return literals or `IHealthCheck` aggregations, and have no DI surface worth a controller.
- Webhook endpoints if a future provider strictly requires `Endpoint`-based routing to bypass anti-forgery (e.g. Stripe). Most providers tolerate `[ApiController]` + `[IgnoreAntiforgeryToken]`; default is still controller.

Azure Functions are out of scope of this decision — they have their own programming model (`[Function]`) and live in a separate project.

## Consequences

**Positive**

- **Filter pipeline does the cross-cutting work once.** `[Authorize]`, `[ValidateAntiForgeryToken]`, FluentValidation, model-state-to-`ProblemDetails`, response caching, and Serilog scope are pipeline-level rather than per-endpoint repetitions.
- **`[ApiController]` gives sensible defaults for free**: automatic 400 on invalid models, automatic binding-source inference (`[FromBody]` for complex types, `[FromRoute]` for path tokens), `ProblemDetails` errors with `traceId`.
- **Controllers are a natural composition root for three-tier.** A `OrdersController` lines up with `IOrderService` and stays thin (input shaping → service call → response shaping).
- **Convention support for OpenAPI / Swashbuckle is mature** — XML doc comments, `[ProducesResponseType]`, and `[FromBody]`/`[FromQuery]` flow into Swagger without bespoke filter writing.
- **Tutorial / StackOverflow / book corpus is dominated by Controllers.** For a learning project, debugging help is one search away. Resume narrative — *"ASP.NET 8 MVC with three-tier separation"* — is the industry-default phrasing.
- **Easier organization at 20+ endpoints.** A single `Program.cs` `app.MapPost(...)` chain becomes hard to scan and review; class-per-domain controllers stay structured.

**Negative / trade-offs**

- **Slightly higher ceremony per endpoint** than Minimal APIs (constructor DI, attribute-decorated method signatures, `ActionResult<T>` return types). Acceptable cost for the structure benefit at this scale.
- **Slightly worse cold-start and per-request overhead** than Minimal APIs. Negligible at our throughput and Container Apps warm-pool config. Measured difference is in microseconds-to-low-milliseconds, dominated by EF Core and downstream calls.
- **Action conventions can hide behaviour** — `[ApiController]` rewrites model-validation responses to 400 `ProblemDetails`; this needs awareness during debugging. Mitigation: a single section in CODING_STANDARDS documenting what `[ApiController]` adds.
- **Worse AOT story** than Minimal APIs. Not relevant: Container Apps is JIT, AOT is not a project goal.

## Alternatives considered

1. **Minimal APIs throughout**
   - Rejected: 20+ endpoints spread across a single `Program.cs` lambda chain (or partial-class `MapXyzEndpoints` extension methods) is harder to read, review, and grep than class-per-domain controllers. The benefits — perf, ceremony reduction, AOT — don't justify the loss of the filter pipeline and the structure that controllers enforce by default. Minimal APIs shine for tiny services (≤ 5 endpoints), not for our scope.

2. **Mixed: Minimal APIs for read endpoints, Controllers for mutations**
   - Rejected: cognitive cost of maintaining two styles in one project; inconsistent review and testing patterns; weakens the resume narrative (*"we used both, for reasons"* invites uncomfortable interview questions about consistency). The diagnostics carve-out is narrow enough to not count as a split style.

3. **FastEndpoints (community library, REPR pattern)**
   - Rejected: non-mainstream tooling for a learning project. Adds learning tax for a framework whose patterns are not transferrable. Defensibility in interviews is weaker (*"why FastEndpoints over Controllers?"* needs a longer answer than the saving is worth).

4. **gRPC for internal endpoints + HTTP for external**
   - Rejected: scope creep. No internal RPC story planned; all calls are HTTP from the SPA or Functions. gRPC would multiply tooling without removing any.

## Implementation notes

- Controllers live at `Retail.Api/Controllers/<Domain>/<Domain>Controller.cs`.
- Base route via `[Route("api/[controller]")]` on the class; verbs via `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]` on actions.
- Request and response DTOs in `Retail.Api/Contracts/<Domain>/` — never reuse EF entities at the HTTP boundary.
- Return type `Task<ActionResult<TResponse>>` for shaped responses; `Task<IActionResult>` when status varies materially across paths.
- `[Authorize(Roles = "Administrator")]` per action where appropriate; policy-based authorization (`[Authorize(Policy = "CanManageInventory")]`) when role checks aren't expressive enough.
- `[ApiController]` is applied via a base class `ApiControllerBase` to avoid attribute repetition; `ApiControllerBase` also sets `[Route("api/[controller]")]` and may provide shared helpers (`Problem(...)` factories).
- FluentValidation registered globally (`AddValidatorsFromAssemblyContaining<Program>()`) auto-runs against incoming models; failures map to `ValidationProblemDetails` automatically.
- `Program.cs` is minimal at the endpoint layer: `app.MapControllers()`, then the carve-out Minimal-API endpoints for `/health` and friends.
- Swagger / OpenAPI: Swashbuckle generates from XML doc comments + `[ProducesResponseType]`. The frontend's `openapi-typescript` step consumes the spec.

## Revisit triggers

- **HTTP/2 server-streaming or other transport feature lands that Minimal APIs supports first and Controllers do not.** → Reassess for the affected endpoint(s); a narrow Minimal-API carve-out (already permitted for diagnostics) can absorb it.
- **AOT becomes a project requirement** (e.g. a Container Apps SKU change that benefits dramatically from AOT cold-start, or a Functions migration). → Re-evaluate; Minimal APIs is the AOT-friendly path.
- **A future framework version makes `[ApiController]` behaviour materially diverge from Minimal APIs' defaults**, undermining the comparison. → New ADR documenting the new comparison.
- **The endpoint count shrinks to ≤ 5** (won't happen for this scope, but noted for forks). → Minimal APIs may be the simpler choice.
