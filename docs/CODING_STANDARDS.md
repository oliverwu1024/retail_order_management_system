# 🛒 Retail OMS 代码规范

> 配套文档：`docs/PLAN.md`、`docs/REQUIREMENTS.md`、`docs/DATABASE_DESIGN.md`

---

## 📁 项目结构

### 后端 ASP.NET Core - 三层架构 (Three-tier)

依赖方向：**Controller → Service → Repository → DbContext**。Controller 永远不直接访问 Repository 或 DbContext；Service 永远不直接访问 DbContext。

```
src/api/
├── Retail.Api/                       # 主 API 项目（单项目三层架构）
│   ├── Controllers/                  # 接口层 - ASP.NET Core MVC Controllers
│   │   ├── AuthController.cs
│   │   ├── CatalogController.cs
│   │   ├── CartController.cs
│   │   ├── OrdersController.cs
│   │   ├── InventoryController.cs
│   │   ├── PaymentsWebhookController.cs
│   │   ├── ChatWebhookController.cs
│   │   ├── ReviewsController.cs
│   │   └── AnalyticsController.cs
│   ├── Services/                     # 业务逻辑层
│   │   ├── IOrderService.cs / OrderService.cs
│   │   ├── ICartService.cs / CartService.cs
│   │   ├── ICatalogService.cs / CatalogService.cs
│   │   ├── IInventoryService.cs / InventoryService.cs
│   │   ├── IPaymentService.cs / PaymentService.cs   # 调用 Ai/Payments 模块
│   │   ├── IChatService.cs / ChatService.cs
│   │   ├── ICopyGenService.cs / CopyGenService.cs
│   │   └── IAuditService.cs / AuditService.cs
│   ├── Repositories/                 # 数据访问层
│   │   ├── IProductRepository.cs / ProductRepository.cs
│   │   ├── IOrderRepository.cs / OrderRepository.cs
│   │   ├── ICartRepository.cs / CartRepository.cs
│   │   ├── IInventoryRepository.cs / InventoryRepository.cs
│   │   ├── IReviewRepository.cs / ReviewRepository.cs
│   │   ├── IChatRepository.cs / ChatRepository.cs
│   │   ├── IAuditRepository.cs / AuditRepository.cs
│   │   └── BaseRepository.cs         # 泛型基类（可选）
│   ├── Domain/                       # 领域模型层
│   │   ├── Entities/                 # 业务实体（POCO，配合 EF Core）
│   │   │   ├── Product.cs
│   │   │   ├── ProductVariant.cs
│   │   │   ├── InventoryItem.cs
│   │   │   ├── Cart.cs / CartItem.cs
│   │   │   ├── Order.cs / OrderLine.cs
│   │   │   ├── Payment.cs / Shipment.cs
│   │   │   ├── Review.cs / AuditLog.cs
│   │   │   ├── ChatSession.cs / ChatMessage.cs
│   │   │   └── DemandForecast.cs / ReorderHint.cs / OrderAnomaly.cs
│   │   └── ValueObjects/             # 值对象（Money, AddressSnapshot）
│   ├── DTOs/                         # Request / Response 模型
│   │   ├── Requests/
│   │   │   ├── CreateProductRequest.cs
│   │   │   ├── PlaceOrderRequest.cs
│   │   │   └── ...
│   │   └── Responses/
│   │       ├── ProductDto.cs
│   │       ├── OrderDto.cs
│   │       └── ...
│   ├── Data/                         # 数据上下文层
│   │   ├── RetailDbContext.cs
│   │   ├── Configurations/           # IEntityTypeConfiguration<T> 实体配置
│   │   │   ├── ProductConfiguration.cs
│   │   │   └── ...
│   │   ├── Interceptors/             # EF Core interceptor
│   │   │   └── AuditingInterceptor.cs
│   │   ├── Migrations/               # EF Core 迁移文件
│   │   └── Seeders/                  # Role / Admin / Demo 数据 seeder
│   ├── Middlewares/                  # 全局中间件
│   │   ├── ExceptionMiddleware.cs
│   │   └── IdempotencyMiddleware.cs
│   ├── Validators/                   # FluentValidation 验证器
│   │   ├── CreateProductRequestValidator.cs
│   │   └── ...
│   ├── Mappers/                      # 实体 ↔ DTO 显式映射（不用 AutoMapper）
│   │   ├── ProductMapper.cs
│   │   ├── OrderMapper.cs
│   │   └── ...
│   ├── Ai/                           # 外部 AI 客户端封装
│   │   ├── ILlmClient.cs             # provider-agnostic LLM 抽象（见 ADR-0005）
│   │   ├── Contracts/                # LlmRequest / LlmCompletion / LlmTool 等 provider 无关 DTO
│   │   │   ├── LlmRequest.cs
│   │   │   ├── LlmCompletion.cs
│   │   │   ├── LlmMessage.cs
│   │   │   ├── LlmTool.cs / LlmToolUse.cs / LlmToolResult.cs / LlmToolChoice.cs
│   │   │   └── LlmUsage.cs
│   │   ├── Providers/
│   │   │   ├── AnthropicLlmClient.cs # 主要实现（Claude Sonnet）— Phase 4/5
│   │   │   ├── StubLlmClient.cs      # Ai:Mode=stub，演示降级用
│   │   │   └── OpenAiLlmClient.cs    # Phase 6/7 stretch
│   │   ├── AiSettings.cs             # IOptions<AiSettings>：Provider、Mode、模型别名映射
│   │   └── ITextAnalyticsAdapter.cs / TextAnalyticsAdapter.cs
│   ├── Payments/                     # Stripe 集成封装
│   │   └── IStripeClient.cs / StripeClient.cs
│   ├── Storage/                      # Azure Blob 封装
│   │   └── IBlobStorageClient.cs / BlobStorageClient.cs
│   ├── Identity/                     # JWT 服务
│   │   ├── IJwtService.cs / JwtService.cs
│   │   └── RoleSeeder.cs
│   ├── HostedServices/               # BackgroundService 后台任务
│   │   ├── CartExpirySweeper.cs
│   │   ├── ReviewSentimentHostedService.cs
│   │   ├── ForecastRefreshHostedService.cs
│   │   └── OrderAnomalyHostedService.cs
│   ├── Exceptions/                   # 自定义业务异常
│   │   ├── NotFoundException.cs
│   │   ├── ConcurrencyException.cs
│   │   ├── OutOfStockException.cs
│   │   └── UnauthorizedException.cs
│   ├── Common/                       # 全局工具
│   │   ├── Constants/                # 常量（角色名、配置 key 等）
│   │   │   └── Roles.cs
│   │   ├── Enums/                    # 业务枚举（OrderStatus、PaymentStatus 等）
│   │   ├── Extensions/               # 扩展方法（HttpContext、IServiceCollection 等）
│   │   ├── Helpers/                  # 静态工具（DateHelper、CurrencyHelper 等）
│   │   └── Models/                   # 通用模型（ApiResponse<T>、PagedResult<T>）
│   │       ├── ApiResponse.cs
│   │       └── PagedResult.cs
│   ├── Program.cs                    # 应用入口 + DI 注册
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── Dockerfile
│
├── Retail.Ml/                        # 独立项目，ML.NET 模型
│   ├── Forecasting/                  # SSA 需求预测
│   │   ├── ISsaForecastService.cs / SsaForecastService.cs
│   │   ├── ModelStore.cs
│   │   └── Trainer.cs
│   └── Anomaly/                      # Z-score 异常检测
│       └── OrderAnomalyService.cs    # 注：调用方在 Retail.Api/HostedServices
│
├── Retail.Ml.Trainer/                # 独立 CLI 项目（GitHub Actions 调用）
│   └── Program.cs
│
├── Retail.Tests.Unit/                # xUnit 单元测试（无 DB）
│   ├── Services/                     # OrderServiceTests 等
│   ├── Validators/
│   ├── Mappers/
│   └── Builders/                     # 测试数据构造器（ProductBuilder 等）
│
├── Retail.Tests.Integration/         # xUnit 集成测试（Testcontainers SQL Server）
│   ├── Controllers/                  # 整合测试 Controller 行为
│   └── WebApplicationFactoryFixture.cs
│
└── Retail.sln
```

**为什么是单项目而不是分项目（Clean Arch）**：portfolio 项目优先简洁。三层在文件夹层面分离，依赖方向通过 code review 强制。`Retail.Ml` 单独项目是因为 ML.NET 训练 CLI 需要独立 entrypoint。

### 前端 React 结构

```
src/web/
├── src/
│   ├── app/                          # 应用根
│   │   ├── router.tsx
│   │   ├── providers.tsx             # QueryClient、Theme、Auth context
│   │   └── App.tsx
│   ├── components/                   # 跨 feature 复用 — 2026-06-06 新增
│   │   ├── ui/                       # shadcn-style primitives（12+ 個 reusable）
│   │   │   ├── Button.tsx            # button + variants（primary、secondary、ghost、destructive）
│   │   │   ├── Input.tsx
│   │   │   ├── Select.tsx
│   │   │   ├── Checkbox.tsx
│   │   │   ├── Modal.tsx             # 基于 Radix Dialog
│   │   │   ├── Drawer.tsx            # Radix Dialog 变体（侧滑）
│   │   │   ├── DataTable.tsx         # 含 sort/filter/pagination
│   │   │   ├── FilterPanel.tsx
│   │   │   ├── Pagination.tsx
│   │   │   ├── Toast.tsx + use-toast.ts
│   │   │   ├── Tabs.tsx              # 基于 Radix Tabs
│   │   │   ├── Card.tsx
│   │   │   └── EmptyState.tsx
│   │   └── layouts/
│   │       ├── AdminShell.tsx        # admin 总壳（sidebar + topbar + 内容区）
│   │       ├── SidebarNav.tsx        # 按 role claim 动态渲染菜单
│   │       └── StorefrontShell.tsx
│   ├── features/                     # 按功能拆分
│   │   ├── storefront/
│   │   │   ├── CatalogPage.tsx
│   │   │   ├── ProductDetailPage.tsx
│   │   │   ├── CartDrawer.tsx        # 用 components/ui/Drawer
│   │   │   ├── hooks/
│   │   │   │   ├── useProductsQuery.ts
│   │   │   │   └── useCart.ts
│   │   │   └── messages.ts           # i18n 字符串集中
│   │   ├── admin/
│   │   │   ├── AdminApp.tsx          # 入口路由
│   │   │   ├── orders/OrdersListPage.tsx     # 用 DataTable + FilterPanel 拼装
│   │   │   ├── products/ProductsListPage.tsx
│   │   │   ├── vouchers/VouchersListPage.tsx # Phase 7
│   │   │   ├── loyalty/LoyaltyAdminPage.tsx  # Phase 7
│   │   │   ├── observability/ObservabilityPage.tsx # Phase 9（嵌入 workbook iframe）
│   │   │   └── ForecastTile.tsx
│   │   ├── auth/                     # login / signup / refresh-on-401 interceptor
│   │   ├── loyalty/                  # 顾客端「我的积分」页 — Phase 7
│   │   └── chat/
│   │       └── ChatDrawer.tsx        # 用 components/ui/Drawer
│   ├── lib/
│   │   ├── api/                      # 自动生成的 OpenAPI client
│   │   ├── apiClient.ts              # 客户端工厂（cookie 自动携带；附 CSRF token header）
│   │   ├── csrf.ts                   # 读非 HTTP-only csrf cookie，写入 X-CSRF-Token header
│   │   ├── stores/                   # Zustand stores
│   │   └── notify.ts                 # useNotify() Toast hook
│   └── main.tsx
├── e2e/                              # Playwright 测试（含 @axe-core/playwright a11y 断言）
│   ├── checkout.spec.ts
│   ├── admin-fulfilment.spec.ts
│   └── voucher-loyalty.spec.ts       # Phase 7
├── public/
├── index.html
├── tailwind.config.ts
├── postcss.config.js
├── vite.config.ts
├── tsconfig.json
└── package.json
```

---

## 🎨 后端代码规范

### 统一响应格式

所有 API 返回 `ApiResponse<T>` 包装：

```csharp
public class ApiResponse<T>
{
    public bool Succeed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static ApiResponse<T> Success(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Succeed = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> Fail(string errorMessage, string? errorCode = null)
    {
        return new ApiResponse<T>
        {
            Succeed = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }
}
```

**错误码（ErrorCode）约定**：

| Code | 含义 | HTTP Status |
|---|---|---|
| `VALIDATION_ERROR` | FluentValidation 验证失败 | 400 |
| `UNAUTHORIZED` | JWT 缺失 / 无效 | 401 |
| `FORBIDDEN` | 已认证但角色不足 | 403 |
| `NOT_FOUND` | 资源不存在 | 404 |
| `CONFLICT` | 并发冲突 / 业务冲突（如库存不足） | 409 |
| `IDEMPOTENCY_CONFLICT` | 同一 Idempotency-Key 不同请求体 | 409 |
| `EXTERNAL_SERVICE_UNAVAILABLE` | Anthropic / Stripe / Azure AI 暂不可用 | 503 |
| `INTERNAL_ERROR` | 未捕获异常 | 500 |

### 全局异常处理中间件

所有未捕获异常必须经此中间件处理：

```csharp
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = exception switch
        {
            ValidationException validationEx => ApiResponse<object>.Fail(
                validationEx.Message, "VALIDATION_ERROR"),
            NotFoundException notFoundEx => ApiResponse<object>.Fail(
                notFoundEx.Message, "NOT_FOUND"),
            UnauthorizedException unauthorizedEx => ApiResponse<object>.Fail(
                unauthorizedEx.Message, "UNAUTHORIZED"),
            ConcurrencyException conflictEx => ApiResponse<object>.Fail(
                conflictEx.Message, "CONFLICT"),
            OutOfStockException oosEx => ApiResponse<object>.Fail(
                oosEx.Message, "CONFLICT"),
            HttpRequestException httpEx => ApiResponse<object>.Fail(
                "An upstream service is unavailable", "EXTERNAL_SERVICE_UNAVAILABLE"),
            _ => ApiResponse<object>.Fail(
                "An error occurred while processing your request", "INTERNAL_ERROR")
        };

        context.Response.StatusCode = GetStatusCode(exception);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    private static int GetStatusCode(Exception exception) => exception switch
    {
        ValidationException => 400,
        UnauthorizedException => 401,
        NotFoundException => 404,
        ConcurrencyException => 409,
        OutOfStockException => 409,
        HttpRequestException => 503,
        _ => 500
    };
}
```

**自定义业务异常定义在 `Retail.Api/Exceptions/`**：

```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class OutOfStockException : Exception
{
    public Guid ProductVariantId { get; }
    public int Requested { get; }
    public int Available { get; }

    public OutOfStockException(Guid variantId, int requested, int available)
        : base($"Variant {variantId} only has {available} available, requested {requested}.")
    {
        ProductVariantId = variantId;
        Requested = requested;
        Available = available;
    }
}
```

### Controller 层

使用 ASP.NET Core MVC Controllers（属性路由 + `[ApiController]`）。Controller **只做**：参数绑定、调用 Service、把结果包成 `ApiResponse<T>` 返回。**禁止** 在 Controller 中写业务逻辑、访问 Repository 或 DbContext。

```csharp
[ApiController]
[Route("api/v1/catalog")]
[Produces("application/json")]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly ICopyGenService _copyGenService;

    public CatalogController(ICatalogService catalogService, ICopyGenService copyGenService)
    {
        _catalogService = catalogService;
        _copyGenService = copyGenService;
    }

    /// <summary>
    /// 获取商品列表（分页 + 筛选）。
    /// </summary>
    [HttpGet("products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProductDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProducts(
        [FromQuery] ProductListRequest request,
        CancellationToken ct)
    {
        var result = await _catalogService.ListProductsAsync(request, ct);
        return Ok(ApiResponse<PagedResult<ProductDto>>.Success(result));
    }

    [HttpGet("products/{slug}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken ct)
    {
        var product = await _catalogService.GetProductBySlugAsync(slug, ct);
        return Ok(ApiResponse<ProductDto>.Success(product));
    }

    [HttpPost("products")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductRequest request,
        CancellationToken ct)
    {
        var product = await _catalogService.CreateProductAsync(request, ct);
        return CreatedAtAction(
            nameof(GetProductBySlug),
            new { slug = product.Slug },
            ApiResponse<ProductDto>.Success(product));
    }

    [HttpPost("products/{id:guid}/generate-copy")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ProductCopyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateCopy(
        Guid id,
        [FromBody] GenerateCopyRequest request,
        CancellationToken ct)
    {
        var copy = await _copyGenService.GenerateAsync(id, request, ct);
        return Ok(ApiResponse<ProductCopyDto>.Success(copy));
    }
}
```

**Program.cs 注册**：

```csharp
var builder = WebApplication.CreateBuilder(args);

// MVC Controllers
builder.Services.AddControllers();

// 数据上下文
builder.Services.AddDbContext<RetailDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Repositories
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
// ...

// Services
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ICartService, CartService>();
// ...

// 中间件、Identity、JWT、FluentValidation、Polly、Swagger 等
// ...

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();
```

### Service 层（业务逻辑层）

业务逻辑放在 `Services/`，**每个 Service 一个接口 + 实现**。Service 调用 Repository，不直接访问 DbContext。

```csharp
public interface IOrderService
{
    Task<CheckoutSessionDto> CreateCheckoutSessionAsync(Guid cartId, CancellationToken ct);
    Task<OrderDto> GetOrderAsync(Guid orderId, CancellationToken ct);
    Task CancelOrderAsync(Guid orderId, CancellationToken ct);
    Task MarkShippedAsync(Guid orderId, MarkShippedRequest request, CancellationToken ct);
    Task RefundOrderAsync(Guid orderId, CancellationToken ct);
}

public class OrderService : IOrderService
{
    private readonly ICartRepository _cartRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IStripeClient _stripeClient;
    private readonly RetailDbContext _db;           // 仅用于多表事务
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        ICartRepository cartRepository,
        IOrderRepository orderRepository,
        IInventoryRepository inventoryRepository,
        IStripeClient stripeClient,
        RetailDbContext db,
        ILogger<OrderService> logger)
    {
        _cartRepository = cartRepository;
        _orderRepository = orderRepository;
        _inventoryRepository = inventoryRepository;
        _stripeClient = stripeClient;
        _db = db;
        _logger = logger;
    }

    public async Task<CheckoutSessionDto> CreateCheckoutSessionAsync(Guid cartId, CancellationToken ct)
    {
        // 1. 加载 cart（带 items）
        var cart = await _cartRepository.GetByIdWithItemsAsync(cartId, ct)
            ?? throw new NotFoundException($"Cart {cartId} not found.");

        // 2. 验证库存
        foreach (var item in cart.Items)
        {
            var available = await _inventoryRepository.GetAvailableAsync(item.ProductVariantId, ct);
            if (available < item.Quantity)
            {
                throw new OutOfStockException(item.ProductVariantId, item.Quantity, available);
            }
        }

        // 3. 多表写入用事务
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 3a. 创建 reservation
            await _inventoryRepository.CreateReservationsAsync(cart, expiry: TimeSpan.FromMinutes(15), ct);

            // 3b. 创建 Payment row（Created 状态）
            var payment = await _orderRepository.CreatePendingPaymentAsync(cart, ct);

            // 3c. 调用 Stripe 创建 Checkout Session
            var session = await _stripeClient.CreateCheckoutSessionAsync(cart, payment.Id, ct);

            // 3d. 写入 stripeSessionId 到 Payment
            await _orderRepository.UpdatePaymentSessionIdAsync(payment.Id, session.Id, ct);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Checkout session {SessionId} created for cart {CartId}",
                session.Id, cartId);

            return new CheckoutSessionDto(session.Id, session.Url);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    // ... 其他方法
}
```

### Repository 层（数据访问层）

Repository **只负责数据访问**，不写业务逻辑。一个聚合根一个 Repository。

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Order?> GetByIdWithLinesAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Order>> ListByCustomerAsync(Guid customerProfileId, int page, int pageSize, CancellationToken ct);
    Task<Payment> CreatePendingPaymentAsync(Cart cart, CancellationToken ct);
    Task UpdatePaymentSessionIdAsync(Guid paymentId, string sessionId, CancellationToken ct);
    Task<bool> HasProcessedStripeEventAsync(string stripeEventId, CancellationToken ct);
    Task MarkStripeEventProcessedAsync(string stripeEventId, string eventType, CancellationToken ct);
}

public class OrderRepository : IOrderRepository
{
    private readonly RetailDbContext _db;

    public OrderRepository(RetailDbContext db)
    {
        _db = db;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<Order?> GetByIdWithLinesAsync(Guid id, CancellationToken ct)
    {
        return await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public async Task<PagedResult<Order>> ListByCustomerAsync(
        Guid customerProfileId, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Orders
            .Where(o => o.CustomerProfileId == customerProfileId)
            .OrderByDescending(o => o.PlacedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Order>(items, total, page, pageSize);
    }

    public async Task<bool> HasProcessedStripeEventAsync(string stripeEventId, CancellationToken ct)
    {
        return await _db.ProcessedStripeEvents
            .AnyAsync(e => e.StripeEventId == stripeEventId, ct);
    }

    // ... 其他方法
}
```

### 三层依赖规则总结

```
Controller -> Service -> Repository -> DbContext (EF Core)
                  \                       /
                   \--> external clients-/
                        (Stripe, Claude, AzureAI, Blob)
```

- ✅ Controller 调 Service
- ✅ Service 调 Repository（数据） + external client（外部服务）
- ✅ Service 在多表写入时直接拿 `RetailDbContext` 起事务（这是三层架构 + EF Core 的常规做法）
- ❌ Controller **禁止** 调 Repository 或 DbContext
- ❌ Repository **禁止** 调 Service（无循环依赖）
- ❌ Repository **禁止** 写业务逻辑（如「if 库存不够则抛异常」） — 这是 Service 的责任

### 依赖注入约定

- **生命周期**：
  - `Singleton` — 无状态、线程安全（如 `IMemoryCache`、`ModelStore`）
  - `Scoped` — 每个 HTTP 请求 / BackgroundService scope 一个实例（DbContext、Service、Repository）
  - `Transient` — 短生命周期 stateless（mapper、validator — 但 FluentValidation 通常 scoped）
- **禁止** 在 Singleton 中注入 Scoped 服务（会导致 captive dependency bug）
- BackgroundService 中使用 `IServiceScopeFactory.CreateScope()` 创建 scope

### EF Core 使用约定

- DbContext 只在 Application / Infrastructure 层访问，**端点中绝不直接使用**
- 查询语法：使用 LINQ method syntax（`.Where()...ToListAsync()`），不用 query syntax
- 投影（projection）尽早使用 `.Select(...)` 减少返回字段
- `Include` 链最深 2 层，超过则改用显式 join 或独立查询
- 写操作：
  - 单实体：`Update()` + `SaveChangesAsync()`
  - 批量：使用 EF Core 8 的 `ExecuteUpdateAsync()` / `ExecuteDeleteAsync()`
- 事务：跨多张表的写操作必须使用 `db.Database.BeginTransactionAsync()`

```csharp
// ✅ 正确：使用事务确保一致性
public async Task CommitOrderAsync(Guid orderId, CancellationToken ct)
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    try
    {
        // 1. Update Order.Status
        // 2. Insert OrderLine[]
        // 3. Decrement InventoryItem.OnHand
        // 4. Insert AuditLog
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

### FluentValidation

每个 Request DTO 都必须有对应 validator，自动注册：

```csharp
// Contracts/CreateProductRequest.cs
public record CreateProductRequest(
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    int PriceCents);

// Validators/CreateProductRequestValidator.cs
public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Z0-9-]+$").WithMessage("SKU must be uppercase alphanumeric or dash.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.PriceCents)
            .GreaterThan(0)
            .LessThan(10_000_000); // 上限 100,000.00
    }
}
```

**Program.cs**：

```csharp
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateProductRequestValidator>();
```

### 日志（Serilog）

**必须使用结构化日志**，禁止字符串插值：

```csharp
// ❌ 错误：日志参数无法被 App Insights 索引
_logger.LogInformation($"Order {orderId} placed by {customerId}");

// ✅ 正确：结构化参数
_logger.LogInformation("Order {OrderId} placed by {CustomerId}", orderId, customerId);
```

**敏感字段不可入日志**：
- ❌ Email、phone、address、credit card
- ✅ User ID（Guid）、order ID、SKU、金额（cents）

### Resilience（Polly）

所有外部 HTTP 客户端必须配置 resilience：

```csharp
builder.Services.AddHttpClient<AnthropicLlmClient>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(); // Polly 默认：retry + circuit breaker + timeout
```

### AI Client 抽象（`ILlmClient`）

两个 AI 功能（聊天机器人 §8a、文案生成 §8b）都需调用 LLM。我们**不让** `ChatService` / `CopyGenService` 直接依赖 `Anthropic.SDK`，而是统一走 `ILlmClient` 抽象。详见 `docs/adr/0005-multi-provider-llm.md`。

**接口定义**（`Retail.Api/Ai/ILlmClient.cs`）：

```csharp
public interface ILlmClient
{
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct);
}
```

**DTO 定义**（`Retail.Api/Ai/Contracts/`，全部 provider-agnostic — 不引用任何 SDK 类型）：

```csharp
public record LlmRequest(
    string Model,                              // 逻辑名："chat" / "copy"；provider 映射到真实 model ID
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null,
    LlmToolChoice? ToolChoice = null,
    int? MaxTokens = null,
    double? Temperature = null,
    bool EnableCaching = false);               // 启用 system prompt 缓存，由 provider 自行决定如何实现

public enum LlmRole { User, Assistant }

public record LlmMessage(
    LlmRole Role,
    string? Text = null,
    IReadOnlyList<LlmToolUse>? ToolUses = null,
    IReadOnlyList<LlmToolResult>? ToolResults = null);

public record LlmTool(string Name, string Description, JsonElement InputSchema);
public record LlmToolUse(string Id, string Name, JsonElement Input);
public record LlmToolResult(string ToolUseId, string Content);

public record LlmToolChoice(string Kind, string? RequiredToolName = null)
{
    public static LlmToolChoice Auto => new("auto");
    public static LlmToolChoice RequiredTool(string name) => new("required", name);
}

public record LlmCompletion(
    string? Text,
    IReadOnlyList<LlmToolUse> ToolUses,
    LlmUsage Usage,
    string StopReason);

public record LlmUsage(
    int InputTokens,
    int OutputTokens,
    int? CacheCreationTokens = null,
    int? CacheReadTokens = null);
```

**DI 注册**（`Program.cs`，Phase 4–5 仅注册 Anthropic）：

```csharp
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));

builder.Services.AddHttpClient<AnthropicLlmClient>(client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler();

// 单一 binding：业务层只见 ILlmClient，看不到具体 provider
builder.Services.AddScoped<ILlmClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
    return settings.Mode switch
    {
        "stub" => sp.GetRequiredService<StubLlmClient>(),
        _      => sp.GetRequiredService<AnthropicLlmClient>()
    };
});

// Phase 6/7 stretch — 接入 OpenAI 后切换为：
// builder.Services.AddScoped<ILlmClient>(sp =>
// {
//     var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
//     return (settings.Mode, settings.Provider) switch
//     {
//         ("stub", _)            => sp.GetRequiredService<StubLlmClient>(),
//         (_, "openai")          => sp.GetRequiredService<OpenAiLlmClient>(),
//         _                      => sp.GetRequiredService<AnthropicLlmClient>()
//     };
// });
```

**Service 层使用方式**（以 `CopyGenService` 为例）：

```csharp
public class CopyGenService : ICopyGenService
{
    private readonly ILlmClient _llm;
    private readonly IProductRepository _productRepo;
    private readonly ILogger<CopyGenService> _logger;

    public CopyGenService(
        ILlmClient llm,
        IProductRepository productRepo,
        ILogger<CopyGenService> logger)
    {
        _llm = llm;
        _productRepo = productRepo;
        _logger = logger;
    }

    public async Task<ProductCopyDto> GenerateAsync(
        Guid productId, GenerateCopyRequest request, CancellationToken ct)
    {
        var product = await _productRepo.GetByIdAsync(productId, ct)
            ?? throw new NotFoundException($"Product {productId} not found.");

        var emitTool = new LlmTool(
            Name: "emit_product_copy",
            Description: "Emit the generated product description and SEO copy.",
            InputSchema: ProductCopyJsonSchema.Value);

        var llmRequest = new LlmRequest(
            Model: "copy",                                                  // 逻辑名，provider 解析为真实 model id
            SystemPrompt: BuildSystemPrompt(request.Tone),
            Messages: new[]
            {
                new LlmMessage(LlmRole.User, Text: BuildUserPrompt(product, request))
            },
            Tools: new[] { emitTool },
            ToolChoice: LlmToolChoice.RequiredTool("emit_product_copy"),    // 强制结构化输出
            MaxTokens: 1024);

        var completion = await _llm.CompleteAsync(llmRequest, ct);

        var toolUse = completion.ToolUses.FirstOrDefault()
            ?? throw new ExternalServiceException(
                "LLM did not return expected tool use.",
                "EXTERNAL_SERVICE_UNAVAILABLE");

        _logger.LogInformation(
            "Copy gen used {InputTokens} input + {OutputTokens} output tokens (model={Model})",
            completion.Usage.InputTokens, completion.Usage.OutputTokens, llmRequest.Model);

        return JsonSerializer.Deserialize<ProductCopyDto>(toolUse.Input.GetRawText())!;
    }
}
```

**关键约定**：

- ❌ `ChatService` / `CopyGenService` **禁止** `using Anthropic.SDK`；通过 `Directory.Build.targets` 限制此命名空间仅可在 `Retail.Api/Ai/Providers/` 下出现。
- ✅ 所有 prompt 模板、tool 定义、JSON schema 留在调用方 Service 内；provider 实现只做传输层翻译（≈ 100 行映射代码）。
- ✅ 缓存、模型选择等 provider 专属优化封装在 `AnthropicLlmClient.CompleteAsync` 内部，调用方只声明意图（`EnableCaching = true`），不指定实现方式。
- ✅ Provider 实现必须输出结构化日志 `llm.provider` / `llm.model` / `llm.input_tokens` / `llm.output_tokens` / `llm.cache_read_tokens`（App Insights 成本仪表板用）。
- ✅ 单元测试用 `Mock<ILlmClient>` 替代，断言 service 行为；HTTP 层的 mock（如 `HttpMessageHandler` mock）仅出现在 `AnthropicLlmClient` 的针对性测试中。

---

## ⚛️ 前端代码规范

### TypeScript / React 风格

- **严格 TypeScript**：`strict: true`，`noUncheckedIndexedAccess: true`
- **禁止 `any`**：用 `unknown` 加类型缩窄
- 2 空格缩进
- 单引号字符串（JSX 属性除外）
- 行末分号
- 多行尾随逗号
- Arrow function 用于组件和回调；`function` 仅用于顶层工具函数

### 组件示例

```tsx
import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Button } from '@/components/ui/Button';
import { Select } from '@/components/ui/Select';
import { useNotify } from '@/lib/notify';
import { useApiClient } from '@/lib/apiClient';

type Props = {
  productId: string;
  onSuccess: (description: string) => void;
};

export function GenerateCopyButton({ productId, onSuccess }: Props) {
  const [tone, setTone] = useState<'playful' | 'professional' | 'luxury'>('professional');
  const api = useApiClient();
  const notify = useNotify();

  const mutation = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.POST(
        '/api/v1/catalog/products/{id}/generate-copy',
        { params: { path: { id: productId } }, body: { tone } },
      );
      if (error) throw error;
      return data;
    },
    onSuccess: (response) => {
      if (response.succeed && response.data) {
        onSuccess(response.data.description);
      }
    },
    onError: () => notify.error('Failed to generate copy. Try again.'),
  });

  return (
    <div className="flex flex-col gap-3">
      <Select
        label="Tone"
        value={tone}
        onChange={(value) => setTone(value as typeof tone)}
        options={[
          { value: 'playful', label: 'Playful' },
          { value: 'professional', label: 'Professional' },
          { value: 'luxury', label: 'Luxury' },
        ]}
      />
      <Button
        variant="primary"
        onClick={() => mutation.mutate()}
        disabled={mutation.isPending}
      >
        {mutation.isPending ? 'Generating…' : 'Suggest Description'}
      </Button>
    </div>
  );
}
```

### Hook 约定

- 自定义 hook 必须以 `use` 开头
- 把 API 调用封装为 hook（组件不直接调 `apiClient`）
- 命名格式：`use<Resource><Action>` → `useProductsQuery`、`useCreateProductMutation`

```tsx
// features/storefront/hooks/useProductsQuery.ts
import { useQuery } from '@tanstack/react-query';
import { useApiClient } from '@/lib/apiClient';

type ProductListParams = {
  page?: number;
  pageSize?: number;
  categoryId?: string;
  search?: string;
};

export function useProductsQuery(params: ProductListParams) {
  const api = useApiClient();
  return useQuery({
    queryKey: ['products', params],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/v1/catalog/products', {
        params: { query: params },
      });
      if (error) throw error;
      return data;
    },
  });
}
```

### 状态管理

- **服务端状态** → TanStack Query
- **跨组件 UI 状态**（购物车 drawer 开关、主题等）→ Zustand
- **本地组件状态** → `useState`

```tsx
// lib/stores/cartStore.ts
import { create } from 'zustand';

type CartStore = {
  isDrawerOpen: boolean;
  toggleDrawer: () => void;
  closeDrawer: () => void;
};

export const useCartStore = create<CartStore>((set) => ({
  isDrawerOpen: false,
  toggleDrawer: () => set((s) => ({ isDrawerOpen: !s.isDrawerOpen })),
  closeDrawer: () => set({ isDrawerOpen: false }),
}));
```

### 表单（React Hook Form + Zod）

```tsx
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';

const schema = z.object({
  sku: z.string().regex(/^[A-Z0-9-]+$/, 'Uppercase alphanumeric or dash'),
  name: z.string().min(1).max(200),
  priceCents: z.number().int().positive().lt(10_000_000),
});

type FormValues = z.infer<typeof schema>;

export function ProductForm() {
  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
  });

  return (
    <form onSubmit={handleSubmit((v) => console.log(v))}>
      {/* ... */}
    </form>
  );
}
```

### Tailwind + shadcn/ui 约定（2026-06-06 新增）

**Class 顺序**（强制 — 由 `prettier-plugin-tailwindcss` 自动排序）：layout → flex/grid → spacing → sizing → typography → colors → effects → transitions → state variants（`hover:`、`focus:`、`disabled:`、`data-*:`、`aria-*:`）。不要手写顺序，跑 `pnpm format` 即可。

**避免 `className` 字符串拼接的反模式** — 用 `cn()` 工具（`clsx` + `tailwind-merge`）：

```tsx
// ❌ 错误：拼接难调试 + Tailwind 冲突类无法 merge
<div className={`p-4 ${isActive ? 'bg-blue-500' : 'bg-gray-100'} ${className ?? ''}`} />

// ✅ 正确：cn() 合并 + tailwind-merge 处理冲突
import { cn } from '@/lib/cn';
<div className={cn('p-4', isActive ? 'bg-blue-500' : 'bg-gray-100', className)} />
```

**Variant 用 `class-variance-authority`（cva）** — 所有 `components/ui/` primitives 通过 cva 表达 variants：

```tsx
// components/ui/Button.tsx
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/cn';

const buttonVariants = cva(
  'inline-flex items-center justify-center rounded-md font-medium transition-colors ' +
    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 ' +
    'disabled:pointer-events-none disabled:opacity-50',
  {
    variants: {
      variant: {
        primary: 'bg-brand-600 text-white hover:bg-brand-700',
        secondary: 'bg-gray-100 text-gray-900 hover:bg-gray-200',
        ghost: 'hover:bg-gray-100 hover:text-gray-900',
        destructive: 'bg-red-600 text-white hover:bg-red-700',
      },
      size: {
        sm: 'h-8 px-3 text-sm',
        md: 'h-10 px-4 text-sm',
        lg: 'h-12 px-6 text-base',
      },
    },
    defaultVariants: { variant: 'primary', size: 'md' },
  },
);

type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & VariantProps<typeof buttonVariants>;

export function Button({ className, variant, size, ...props }: ButtonProps) {
  return <button className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}
```

**Component library 規則**（對應 Job B-1 履歷條目「12+ reusable components ... reducing code duplication by 45%」）：

- ❌ 在 `features/` 內手寫 `<button className="bg-blue-500 ...">` — 必須走 `components/ui/Button`
- ❌ 重複定義同樣的 modal / drawer / data table 結構 — 提取為 primitive
- ✅ 一個 primitive 必須能被至少 2 個 feature 復用才算 valid（否則就只是普通組件，放在 feature 內）
- ✅ 每個 primitive 有 `<Name>.stories.tsx` 範例（Storybook 可選但建議 Phase 3 引入）
- ✅ Accessibility 默认对：用 Radix primitives 包裝（Dialog / Tabs / Select 等）— 鍵盤導航、ARIA、焦點管理 Radix 已處理

**jscpd 阈值**：CI 跑 `pnpm jscpd src/ --threshold 5`（最多 5% 重複），超過 PR comment 警告（不 block，因為自動 dedup 不總是合理）。Phase 0 baseline + Phase 10 final 兩份報告 commit 到 `docs/perf/`，差距即 B-1 的「45% reduction」數字來源。

### 認證 cookie 約定（2026-06-06 新增）

- **登入回應永不在 body 返回 token** — 一律 `Set-Cookie`
- 前端從不存取 access token 字串（HTTP-only 也讀不到）
- 每個狀態變更請求自動加 `X-CSRF-Token` header（來源：非 HTTP-only `csrf` cookie），由 `apiClient` interceptor 處理
- `apiClient` 401 響應自動觸發 `/api/v1/auth/refresh` 一次重試；失敗則跳轉登入頁

```tsx
// lib/csrf.ts
export function readCsrfToken(): string | null {
  const match = document.cookie.match(/(?:^|;\s*)csrf=([^;]+)/);
  return match?.[1] ?? null;
}

// lib/apiClient.ts（摘要）
export function createApiClient() {
  const client = createOpenApiFetch({ baseUrl: '/api' });
  client.use({
    onRequest({ request }) {
      const csrf = readCsrfToken();
      if (csrf && request.method !== 'GET') {
        request.headers.set('X-CSRF-Token', csrf);
      }
      return request;
    },
    onResponse: async ({ response, request }) => {
      if (response.status === 401 && !request.url.endsWith('/auth/refresh')) {
        await fetch('/api/v1/auth/refresh', { method: 'POST', credentials: 'include' });
        return fetch(request);
      }
      return response;
    },
  });
  return client;
}
```

---

## 📝 命名规范

### 后端 C#
| 元素 | 风格 | 示例 |
|---|---|---|
| 类 | PascalCase | `ProductService`、`OrderController` |
| 方法 | PascalCase | `GetUserById`、`CreateOrderAsync` |
| 异步方法 | 以 `Async` 结尾 | `GetOrderAsync` |
| 变量 / 参数 | camelCase | `userId`、`orderItems` |
| 私有字段 | _camelCase（primary constructor 中可省） | `_logger` |
| 接口 | PascalCase 以 `I` 开头 | `IProductRepository` |
| 枚举 | PascalCase | `OrderStatus.Pending` |
| 常量 | PascalCase | `MaxRetries` |

### 前端 TS/React
| 元素 | 风格 | 示例 |
|---|---|---|
| 组件 | PascalCase 文件 + 导出 | `ProductCard.tsx` 导出 `ProductCard` |
| Hook | useCamelCase | `useCart()` |
| 类型 / 接口 | PascalCase（优先 `type`） | `type OrderDto = {...}` |
| 常量 | UPPER_SNAKE_CASE | `MAX_CART_ITEMS` |
| 文件夹 | kebab-case | `chat-drawer/` |
| 函数 / 变量 | camelCase | `handleSubmit` |

### SQL / EF Core
| 元素 | 风格 | 示例 |
|---|---|---|
| 表名 | PascalCase 单数 | `Product`、`Order` |
| 列名 | PascalCase | `OrderNumber`、`CreatedAt` |
| 主键 | `Id` | — |
| 外键 | `{Entity}Id` | `CustomerProfileId` |
| 索引 | `IX_` / `UX_` 前缀 | `IX_Order_CustomerProfileId_PlacedAt` |
| 序列 | `Seq_{Purpose}` | `Seq_OrderNumber` |

---

## 🔀 Git 提交规范

后端提交代码前必须确保 `dotnet build` 通过 + `dotnet test` 通过。
前端提交代码前必须确保 `pnpm typecheck && pnpm lint && pnpm test` 通过。

### Commit Message 格式

```
<type>(<scope>): <subject>
```

**示例**：

```
feat(api): add stripe checkout session endpoint
fix(web): prevent double submit on checkout button
refactor(api): extract auditing interceptor from dbcontext
docs(adr): add adr-0003 for zscore anomaly approach
test(api): cover concurrent inventory decrement
chore(infra): bump bicep template to v1.2
```

### 类型说明

| Type | 说明 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修复 bug |
| `refactor` | 重构，不改变功能 |
| `perf` | 性能优化 |
| `test` | 添加 / 修改测试 |
| `docs` | 文档更新 |
| `style` | 代码格式调整（不改逻辑） |
| `build` | 构建系统 / 依赖 |
| `ci` | CI / CD 配置 |
| `chore` | 其他维护工作 |
| `revert` | 回滚 |

### Scope 约定

- `api` — `Retail.Api` 及相关
- `web` — `src/web/`
- `infra` — Bicep / IaC
- `db` — 数据库迁移
- `ci` — GitHub Actions
- `ml` — `Retail.Ml/`
- `docs` — `docs/` 或 README
- `adr` — `docs/adr/`

### Branch 约定

- `main` — 受保护，只能通过 PR 合入
- `feat/<topic>` — 新功能
- `fix/<topic>` — bug 修复
- `infra/<topic>` — 基础设施
- `docs/<topic>` — 文档
- `chore/<topic>` — 杂项

### PR 模板

```markdown
## 简介 (What)
- 一句话说明本 PR 做了什么

## 动机 (Why)
- 解决了什么问题 / 满足了哪个 FR

## 实现 (How)
- 非显而易见的技术决策（最多一段）

## 测试 (Test Plan)
- [ ] 单元测试已添加 / 更新
- [ ] 集成测试已添加 / 更新（如适用）
- [ ] 手动 smoke test 通过
- [ ] CI 全绿

## 截图（如有 UI 改动）

## 文档更新
- [ ] 如改变了 requirements，已更新 `docs/REQUIREMENTS.md`
- [ ] 如改变了数据模型，已更新 `docs/DATABASE_DESIGN.md`
- [ ] 如有架构决策，已添加 ADR
```

---

## ✅ 代码质量要求

### 缩进
- 后端 4 空格
- 前端 2 空格
- 由 `.editorconfig` 强制

### 后端质量标准
- ✅ 所有未捕获异常必须经 `ExceptionMiddleware` 处理并返回统一 `ApiResponse<T>` 格式
- ✅ 必须使用 Dependency Injection 管理服务生命周期
- ✅ 多表写操作必须使用事务（`BeginTransactionAsync`）
- ✅ 所有请求 DTO 必须有 FluentValidation 验证器
- ✅ 所有外部 HTTP 客户端必须配置 Polly resilience handler
- ✅ 接口 / 公共方法使用 XML 文档注释（`<summary>`）
- ✅ Async 方法必须以 `Async` 结尾 + 接受 `CancellationToken`
- ❌ 禁止 `.Result` / `.Wait()` / `GetAwaiter().GetResult()`
- ❌ 禁止字符串插值的日志参数
- ❌ 禁止 `using System.Net.WebClient` / 老旧 API
- ❌ 禁止在 Singleton 中注入 Scoped 服务

### 前端质量标准
- ✅ TypeScript `strict: true` 编译无错误
- ✅ ESLint `--max-warnings 0` 通过
- ✅ Prettier 格式化
- ✅ 服务端状态用 TanStack Query，不复制到 local state
- ✅ Loading / Error 状态在每个页面都是 first-class
- ✅ API 错误经 `useNotify()` 显示 Toast
- ❌ 禁止 `any`
- ❌ 禁止 `data-testid` 除非无语义替代
- ❌ 禁止在组件中直接调用生成的 API 客户端（必须经 hook 封装）

### 测试要求
- 单元测试覆盖：所有 use case handler 的核心逻辑、所有 validator、所有领域规则
- 集成测试覆盖：所有写操作端点（创建 / 更新）、Stripe webhook、并发库存
- E2E 覆盖：注册 → 加购 → 结账 → 查看订单 golden path
- 测试命名：`MethodName_StateUnderTest_ExpectedBehavior`
  - 例：`Reserve_WhenStockInsufficient_Throws409`
- AAA 结构：Arrange / Act / Assert 用空行分隔

```csharp
// 单元测试示例
public class OrderServiceTests
{
    [Fact]
    public async Task CreateCheckoutSession_WhenInventoryInsufficient_ThrowsOutOfStockException()
    {
        // Arrange
        var cart = CartBuilder.New().WithItem(variantId, quantity: 5).Build();
        var cartRepoMock = new Mock<ICartRepository>();
        cartRepoMock
            .Setup(r => r.GetByIdWithItemsAsync(cart.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var inventoryRepoMock = new Mock<IInventoryRepository>();
        inventoryRepoMock
            .Setup(r => r.GetAvailableAsync(variantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);  // 库存只有 2 件，cart 要 5 件

        var sut = new OrderService(
            cartRepoMock.Object,
            Mock.Of<IOrderRepository>(),
            inventoryRepoMock.Object,
            Mock.Of<IStripeClient>(),
            TestDbContextFactory.CreateInMemory(),
            NullLogger<OrderService>.Instance);

        // Act
        var act = () => sut.CreateCheckoutSessionAsync(cart.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<OutOfStockException>();
    }
}
```

---

## ✨ Definition of Done

代码视为完成需满足：
1. ✅ 代码已合入 `main`（通过 PR）
2. ✅ CI 全绿
3. ✅ 相关单元测试 / 集成测试 / E2E 已通过
4. ✅ 对应 FR 的验收标准（Acceptance Criteria）本地演示通过
5. ✅ `docs/` 已同步更新（如适用）
6. ✅ 无遗留无主 `// TODO`（必须有 owner + issue 链接）
7. ✅ 对应 Phase 的 manual smoke test 通过（见 `docs/PLAN.md`）
