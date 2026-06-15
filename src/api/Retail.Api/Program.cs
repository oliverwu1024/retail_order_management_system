using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Constants;
using Retail.Api.Data;
using Retail.Api.Data.Interceptors;
using Retail.Api.Domain.Entities;
using Retail.Api.Identity;
using Retail.Api.Middlewares;
using Retail.Api.Repositories;
using Retail.Api.Services;
using Retail.Api.Storage;
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

    // ── Strongly-typed auth options ─────────────────────────────────────────
    // Bind the Jwt / Auth / Auth:DefaultAdmin config sections to typed options so
    // the token service, cookie writer, and seeder inject IOptions<T> instead of
    // indexing raw configuration strings.
    builder.Services
        .AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .Validate(
            o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
            "Jwt:Key must be configured with at least 32 characters (User Secrets / Key Vault).")
        .ValidateOnStart();
    // Dedicated CSRF signing key (key separation from Jwt:Key — see CsrfTokenService).
    builder.Services
        .AddOptions<CsrfOptions>()
        .Bind(builder.Configuration.GetSection(CsrfOptions.SectionName))
        .Validate(
            o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
            "Csrf:Key must be configured with at least 32 characters (User Secrets / Key Vault).")
        .ValidateOnStart();
    builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection(AuthSettings.SectionName));
    builder.Services.Configure<DefaultAdminOptions>(builder.Configuration.GetSection(DefaultAdminOptions.SectionName));

    // ── ASP.NET Identity (core) ──────────────────────────────────────────────
    // AddIdentityCore — NOT AddIdentity — because this is a token API, not a
    // cookie/Razor app. AddIdentity registers four unused authentication COOKIE
    // schemes (Identity.Application / External / two 2FA schemes) AND sets them as
    // the default authenticate/challenge schemes — which then fight our Bearer
    // default and 401 every [Authorize]. AddIdentityCore registers none of that,
    // so Bearer (configured below) is the sole, uncontested scheme. We re-add only
    // the pieces a JWT flow actually uses:
    //   • AddRoles                 → RoleManager + role store (seeder + AddToRole).
    //   • AddEntityFrameworkStores → user/role EF stores (MUST come after AddRoles,
    //                                or the role store is never registered).
    //   • AddSignInManager         → SignInManager.CheckPasswordSignInAsync (lockout-aware).
    //   • AddDefaultTokenProviders → password-reset / email-confirm tokens (Phase 1.4).
    //
    // Password policy → PRD §1.1: ≥12 chars containing at least one letter and one
    // digit. We enforce length + digit HERE and delegate the "must contain a
    // letter" rule to RegisterRequestValidator — Identity has no single "any
    // letter" switch (only lower/upper), and splitting it this way keeps Identity
    // from rejecting a password the validator already accepted.
    builder.Services
        .AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;

            options.User.RequireUniqueEmail = true;

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<RetailDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

    // ── JWT Bearer authentication ───────────────────────────────────────────
    // We use JWT for the API: the access token rides in an HttpOnly cookie
    // (ADR-0007) and is validated as a bearer token. All four validations are ON
    // (issuer, audience, lifetime, signing key) — disabling any is the classic JWT
    // misconfiguration that lands on a CVE list.
    builder.Services
        .AddAuthentication(options =>
        {
            // Bearer is the ONLY authentication scheme — AddIdentityCore (above)
            // registered no competing cookie schemes. We set all three defaults
            // explicitly so [Authorize], challenges, and an unnamed
            // AuthenticateAsync() all resolve to Bearer — unambiguous and self-documenting.
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer();

    // Configure the Bearer options from the bound JwtOptions — the SINGLE source of
    // truth shared with JwtService (which mints the tokens). Reading via injected
    // IOptions<JwtOptions> (instead of indexing builder.Configuration here) resolves
    // the values AFTER configuration is finalized, so minting and validation can
    // never diverge — including when an integration test overrides Jwt:* via
    // in-memory configuration that is layered on after startup begins.
    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<JwtOptions>>((bearer, jwtOptionsAccessor) =>
        {
            JwtOptions jwt = jwtOptionsAccessor.Value;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                // Default ClockSkew (5 min) absorbs small issuer/validator clock drift.
            };

            // The access token rides in an HttpOnly cookie, not the Authorization
            // header. Pull it from the cookie before validation; if absent, the
            // default header extraction still runs (used by Swagger's Authorize box).
            bearer.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Cookies.TryGetValue(AuthConstants.AccessTokenCookie, out string? cookieToken)
                        && !string.IsNullOrEmpty(cookieToken))
                    {
                        context.Token = cookieToken;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddAuthorization();

    // ── Auth services (ADR-0007) ────────────────────────────────────────────
    // JwtService + CsrfTokenService are immutable after construction → singletons.
    // AuthService + the refresh-token repository are request-scoped (they touch the
    // scoped DbContext). The seeder is scoped and resolved once at startup.
    builder.Services.AddSingleton<IJwtService, JwtService>();
    builder.Services.AddSingleton<ICsrfTokenService, CsrfTokenService>();
    builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IdentityDataSeeder>();

    // ── Catalog services (Story 1.2) ─────────────────────────────────────────
    builder.Services.AddScoped<IProductRepository, ProductRepository>();
    builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
    builder.Services.AddScoped<ICatalogService, CatalogService>();

    // ── Customer profile services (Story 1.4) ────────────────────────────────
    builder.Services.AddScoped<ICustomerProfileRepository, CustomerProfileRepository>();
    builder.Services.AddScoped<ICustomerProfileService, CustomerProfileService>();

    // Blob storage (product images → Azure Blob / Azurite). The client builds its
    // BlobServiceClient lazily, so a blank Storage:ConnectionString never breaks
    // catalogue reads — only an actual image upload.
    builder.Services.Configure<BlobStorageOptions>(builder.Configuration.GetSection(BlobStorageOptions.SectionName));
    builder.Services.AddSingleton<IBlobStorageClient, BlobStorageClient>();

    // ── CORS for the SPA ─────────────────────────────────────────────────────
    // Cookie auth is cross-ORIGIN in dev (SPA :5173 → API :7015) even though it is
    // same-SITE, so the browser requires an explicit credentialed CORS grant:
    // named origins (a wildcard is illegal with credentials) + AllowCredentials.
    string[] corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? Array.Empty<string>();
    if (corsOrigins.Length == 0)
    {
        // Fail-closed (WithOrigins([]) blocks all cross-origin SPA calls) — but unlike
        // the Jwt:Key fail-fast there is no hard error, so warn to shorten "CORS blocked"
        // debugging when an environment is missing Cors:AllowedOrigins.
        Log.Warning("CORS: no allowed origins configured (Cors:AllowedOrigins is empty) — cross-origin SPA calls will be blocked.");
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("spa", policy => policy
            .WithOrigins(corsOrigins)
            .AllowCredentials()
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"));
    });

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

    // FluentValidation (invoked explicitly in controllers) owns request validation
    // and returns our ApiResponse 422. Suppress [ApiController]'s automatic 400
    // ProblemDetails so the two don't compete; malformed JSON still 400s at the
    // input formatter.
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    {
        options.SuppressModelStateInvalidFilter = true;
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

    // 1b. Baseline security response headers (defense-in-depth). This is a JSON API
    //     + a separate SPA, so these are hardening rather than load-bearing, but their
    //     presence is the canonical baseline and they also cover Swagger/error/blob
    //     responses. Set right after ExceptionMiddleware so even error responses carry them.
    app.Use(async (context, next) =>
    {
        IHeaderDictionary headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        // Strict default for a pure JSON API — it renders no HTML of its own.
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        await next();
    });

    // 1c. HSTS outside Development. TLS is terminated at the APIM/Container Apps edge
    //     in prod (so UseHttpsRedirection would be a no-op behind the proxy and is
    //     intentionally omitted), but emitting Strict-Transport-Security instructs
    //     browsers to refuse plaintext on subsequent visits — the half that still has merit.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

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

    // 5. CORS — between routing and auth, so preflight + credentialed cross-origin
    //    calls from the SPA are honoured before authentication runs.
    app.UseCors("spa");

    // 6. AuthN then AuthZ. Both required and in this order.
    app.UseAuthentication();
    app.UseAuthorization();

    // 7. CSRF — signed double-submit check on state-changing requests (ADR-0007).
    //    Sits after auth, close to the endpoints it protects; safe methods pass through.
    app.UseMiddleware<CsrfMiddleware>();

    // 8. Endpoints: controllers + health probes.
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

    // ── Seed roles + default admin (idempotent, best-effort) ─────────────────
    // Runs in a DI scope after the host is built. A missing/unreachable DB logs an
    // error but does NOT abort startup — this keeps the boot smoke test and any
    // DB-less start working; real dev seeds once `dotnet ef database update` ran.
    using (IServiceScope scope = app.Services.CreateScope())
    {
        try
        {
            IdentityDataSeeder seeder = scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception seedException)
        {
            Log.Error(seedException, "Identity seeding failed; continuing startup.");
        }
    }

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

// Exposes the top-level-statement entry point as a public type so integration
// tests can drive it via WebApplicationFactory<Program>. (Without this, the
// generated Program class is internal and the test project cannot name it.)
public partial class Program { }
