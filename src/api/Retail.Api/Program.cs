using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retail.Api.Common.Abstractions;
using Retail.Api.Data;
using Retail.Api.Data.Interceptors;
using Retail.Api.Domain.Entities;
using Retail.Api.Middlewares;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
//  Program.cs — composition root for Retail.Api.
//
//  This file wires the entire process: logging, telemetry, EF Core, Identity,
//  JWT auth, MVC, validation, Swagger, health checks, and the middleware
//  pipeline. Long but linear by design — every block is annotated with WHY
//  it exists so the wiring stays interview-defensible.
//
//  WHY KEEP IT IN ONE FILE INSTEAD OF SPLITTING INTO IServiceCollection
//  EXTENSION METHODS?
//  --------------------------------------------------------------------
//  Extension-method splits (e.g. AddRetailAuth(), AddRetailObservability())
//  hide ordering and make it harder to teach the file in an interview. For
//  this codebase, one annotated Program.cs is the more defensible choice.
//  When the file exceeds ~300 lines we'll factor out by concern.
// ─────────────────────────────────────────────────────────────────────────────

// ── Serilog bootstrap logger ────────────────────────────────────────────────
// Why a bootstrap logger? Any exception thrown BEFORE the full Serilog config
// is applied (e.g., a missing connection string discovered during builder
// setup) would otherwise be eaten by the default ASP.NET logger and printed
// without our enrichment. The bootstrap logger guarantees startup errors
// land in the console with the same shape as production logs.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Retail.Api host");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog: full configuration ─────────────────────────────────────────
    // Replace the default ILogger pipeline with Serilog. ReadFrom.Configuration
    // means the levels and sinks are driven by appsettings.json's "Serilog"
    // section, so ops can change log verbosity without a redeploy. Enrich
    // with FromLogContext picks up properties pushed via LogContext.PushProperty
    // (e.g., we'll push UserId in an auth middleware later).
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ── HttpContext accessor + TimeProvider ─────────────────────────────────
    // AddHttpContextAccessor: required by AuditingInterceptor (and any other
    // non-request-scoped service that needs to know "who is the current user").
    // TimeProvider.System: the .NET 8+ replacement for DateTime.UtcNow as a DI
    // dependency. Lets us inject a FakeTimeProvider in tests. Singleton is fine
    // — TimeProvider has no per-request state.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton(TimeProvider.System);

    // Current-user accessor: the seam the AuditingInterceptor uses to stamp
    // CreatedBy/UpdatedBy without depending on HttpContext directly. Scoped
    // because the HTTP-backed implementation wraps the request-scoped
    // IHttpContextAccessor. Swap this single registration to change where the
    // "current user" comes from (e.g. a system principal for background jobs).
    builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

    // ── EF Core DbContext + interceptors ────────────────────────────────────
    // The interceptor is registered Scoped so it can capture the per-request
    // HttpContext.User. AddDbContext with the IServiceProvider-aware overload
    // lets us resolve the interceptor from DI and attach it via AddInterceptors.
    // Connection string lives in configuration; failing fast if it's missing
    // (the ?? throw) is intentional — silently running on a default connection
    // string is a security/data-corruption footgun.
    builder.Services.AddScoped<AuditingInterceptor>();
    builder.Services.AddDbContext<RetailDbContext>((sp, options) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' not found. Set ConnectionStrings:Default in appsettings or env vars.");

        options.UseSqlServer(connectionString, sql =>
        {
            // The migrations assembly explicitly names this project so EF
            // tooling (dotnet ef migrations add) knows where to put the
            // generated files. Defensible against future split where some
            // services live in a separate assembly.
            sql.MigrationsAssembly(typeof(RetailDbContext).Assembly.FullName);
        });

        // AddInterceptors hooks the AuditingInterceptor into the EF Core
        // SaveChanges pipeline. Adding more interceptors here (outbox,
        // optimistic concurrency, soft-delete) is a one-line change.
        options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
    });

    // ── ASP.NET Identity ────────────────────────────────────────────────────
    // AddIdentity wires the full Identity stack (UserManager, SignInManager,
    // RoleManager, token providers for password reset / email confirm). We
    // tighten the password policy from the defaults (which allow 6 chars and
    // require special chars) — 8 chars, no required special char, is the
    // current OWASP-aligned baseline for consumer apps.
    builder.Services
        .AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;

            options.User.RequireUniqueEmail = true;

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<RetailDbContext>()
        .AddDefaultTokenProviders();

    // ── JWT Bearer authentication ───────────────────────────────────────────
    // We use JWT for the API (mobile + SPA via httpOnly cookie wrapper — the
    // cookie carries the JWT, the API validates it as a bearer token). All
    // four validations are ON: issuer, audience, lifetime, signing key.
    // Disabling any of these is the classic JWT misconfiguration that ends up
    // on a CVE list.
    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException(
            "Jwt:Key not configured. Set Jwt:Key in appsettings (32+ chars) or User Secrets.");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                // Default ClockSkew is 5 minutes — generous enough to absorb
                // small clock drift between issuer and validator without
                // letting expired tokens linger. We keep the default.
            };
        });

    builder.Services.AddAuthorization();

    // ── MVC controllers ─────────────────────────────────────────────────────
    // CamelCase property naming matches what the React client expects.
    // WhenWritingNull keeps the wire bodies lean by omitting null fields
    // (e.g., a successful ApiResponse won't ship `errors: null`).
    builder.Services
        .AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    // ── FluentValidation ────────────────────────────────────────────────────
    // AddValidatorsFromAssemblyContaining scans this assembly for any class
    // implementing IValidator<T> and registers each as a transient service.
    // We invoke validators explicitly in controllers (see CODING_STANDARDS)
    // instead of auto-validating via FluentValidation.AspNetCore (which was
    // deprecated by the FluentValidation team in v11.3). Explicit validation
    // is more predictable and easier to test.
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // ── Swagger / OpenAPI ───────────────────────────────────────────────────
    // Dev-only UX surface. The bearer scheme is wired here so devs can paste
    // a JWT once and hit any [Authorize]'d endpoint without copying the token
    // into every request manually.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Retail OMS API",
            Version = "v1",
            Description = "Backend for the Retail Order Management System portfolio project.",
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT bearer token. Paste only the raw token (no 'Bearer ' prefix).",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        });

        // Note: We deliberately do NOT call AddSecurityRequirement here.
        // Microsoft.OpenApi 2.x reworked OpenApiReference and the requirement
        // builder no longer accepts the inline-reference pattern from v1.
        // Effect on UX: the "Authorize" button still appears in Swagger UI
        // (driven by AddSecurityDefinition above), the dev pastes their JWT
        // once, and it flows on every request. The only thing missing is
        // the lock icon on each endpoint — purely cosmetic.
    });

    // ── OpenTelemetry ───────────────────────────────────────────────────────
    // Traces: per-request distributed traces with AspNetCore + EF Core
    // instrumentation. The Activity / TraceId surfaced in ApiResponse.TraceId
    // comes from here — the AspNetCore instrumentation creates the root
    // Activity for every HTTP request.
    // Metrics: built-in ASP.NET Core meters (request rate, duration buckets).
    // Console exporter is the dev sink. Prod swaps in OTLP → Tempo / Grafana
    // — single config change.
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(r => r.AddService(serviceName: "Retail.Api"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddConsoleExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter());

    // ── Health checks ───────────────────────────────────────────────────────
    // DbContextCheck issues a lightweight `SELECT 1`-equivalent against the
    // configured DbContext. Tagged "ready" so /health/ready returns it and
    // /health/live ignores it (liveness should NOT depend on external systems
    // — that's why Kubernetes/Container Apps separates the two probes).
    builder.Services
        .AddHealthChecks()
        .AddDbContextCheck<RetailDbContext>(name: "database", tags: new[] { "ready" });

    // ── Polly / HTTP resilience ─────────────────────────────────────────────
    // No HttpClient registrations exist yet — Phase 0 doesn't make outbound
    // HTTP calls. When Phase 1 introduces them (Stripe API, external inventory
    // feeds, Anthropic LLM), each typed HttpClient gets wrapped with a Polly
    // resilience pipeline via .AddResilienceHandler(name, b => b.AddRetry(...)
    // .AddCircuitBreaker(...)). Until then, no Polly package is referenced —
    // pulling it in without a consumer would be dead weight.

    var app = builder.Build();

    // ─────────────────────────────────────────────────────────────────────────
    //  Middleware pipeline. Order is load-bearing — listed top-to-bottom in
    //  request flow. Reordering these breaks behavior in non-obvious ways.
    // ─────────────────────────────────────────────────────────────────────────

    // 1. ExceptionMiddleware first — its try wraps everything downstream.
    app.UseMiddleware<ExceptionMiddleware>();

    // 2. Serilog request logging — one structured log line per request with
    //    method, path, status, elapsed ms. Sits after ExceptionMiddleware so
    //    that even failed requests get a log line (the exception middleware
    //    doesn't short-circuit logging — it just shapes the response body).
    app.UseSerilogRequestLogging();

    // 3. Swagger in dev only. Production exposes no API browser surface.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Retail OMS API v1");
            c.RoutePrefix = "swagger"; // serve at /swagger, not at root
        });
    }

    // 4. Routing — explicit call so the ordering of auth-then-authz between
    //    routing and endpoints is unambiguous.
    app.UseRouting();

    // 5. AuthN then AuthZ. Both required and in this order.
    app.UseAuthentication();
    app.UseAuthorization();

    // 6. Endpoints: controllers + health probes.
    app.MapControllers();

    // Liveness: returns 200 if the process can respond — no dependency checks.
    // K8s/Container Apps uses this to decide "should I kill this pod?"
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false, // no individual checks; just process-up
    });

    // Readiness: returns 200 if the process AND its critical dependencies are
    // healthy. K8s/Container Apps uses this to decide "should I route traffic
    // to this pod?" If the DB is down we want traffic to skip this instance.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    });

    app.Run();
}
catch (Exception ex)
{
    // Any exception thrown during builder setup or before the host stabilizes
    // lands here. We log via Serilog (the bootstrap logger is still active)
    // so the failure is visible in the same format as runtime logs.
    Log.Fatal(ex, "Retail.Api host terminated unexpectedly during startup");
}
finally
{
    // Flush any buffered Serilog log events before the process exits — without
    // this, the final fatal log line can be lost.
    Log.CloseAndFlush();
}
