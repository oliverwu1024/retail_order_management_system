# 🛒 Retail Order Management System 需求文档

> 配套文档：`docs/PLAN.md`（实施计划）、`docs/DATABASE_DESIGN.md`（数据模型）、`docs/CODING_STANDARDS.md`（代码规范）
>
> **2026-06-06 更新**：项目重新定位为「两份履历角色条目的证据材料」（详见 `project_resume_targets.md` memory）。新增 Epic 7（促銷系統）、Epic 8（事件驅動架構）、Epic 9（可觀測性）、Epic 10（效能與負載測試）。Admin UI 技術棧从 Refine.dev + MUI 改为 **Tailwind CSS + shadcn/ui**。認證升級为 **HTTP-only cookie + refresh-token rotation + CSRF**。

Figma UI 链接：*待补充 — 项目自主设计；admin 採用自建 Tailwind + shadcn/ui 組件庫*

技术栈：React 18 + Vite + TypeScript + **Tailwind CSS + shadcn/ui (Radix)**（前端）/ ASP.NET Core 10 LTS + EF Core 10 + SQL Server 2022（后端）/ Azure Container Apps + **APIM** + **Service Bus + Event Grid + Functions** + Static Web Apps（部署）

---

## 👥 用户角色说明

> **2026-06-06 更新**：StaffOps 拆分为 **StoreManager（店长）** + **Staff（店员）** 兩個明確角色，对齐 Job B-2 履历条目「store managers, staff, and administrators」用語。共四個角色。

| 角色 | 代号 | 权限范围 |
|---|---|---|
| **Customer（顾客）** | `Customer` | 浏览商品 / 加入购物车 / Stripe 结账 / 应用优惠券 / 兑换积分 / 查看自己的订单和积分明细 / 提交评论 / 与 AI 聊天助手互动 |
| **Staff（店员）** | `Staff` | 查看所有订单 / 标记发货 / 调整库存 / 处理订单异常 / 查看预测和补货建议 / 只读审计与报表 |
| **StoreManager（店长）** | `StoreManager` | Staff 全部权限 + 退款 / 创建/停用 Staff 账号 / 完整报表（销售、情感、异常、优惠券使用、积分汇总、Tier 分布）/ 完整审计搜索 |
| **Administrator（管理员）** | `Administrator` | StoreManager 全部权限 + 完整商品目录 CRUD / 触发 AI 文案生成 / 优惠券 CRUD / 积分 Tier 门槛管理 / 手动调整积分 / 用户和角色全部管理 / 应用配置 |

---

## 🧩 核心模块一览

| 模块名称 | Customer | Staff | StoreManager | Administrator |
|---|:-:|:-:|:-:|:-:|
| 用户注册 / 登录 | ✅ 自助注册 | ❌ 预置 | ❌ 预置 | ❌ 预置 |
| 商品目录浏览 | ✅ 浏览、筛选、搜索 | ✅ 查看 | ✅ 查看 | ✅ 查看 + 完整 CRUD |
| 购物车与结账 | ✅ 完整流程（含优惠券、积分兑换） | ❌ | ❌ | ❌ |
| 订单管理 | ✅ 查看 / 取消 Pending | ✅ 查看全部 / 标发货 / 处理异常 | ✅ + 退款 | ✅ 完整 |
| 库存管理 | ❌ | ✅ 调整 / 关闭补货提示 | ✅ | ✅ 完整 |
| 评论系统 | ✅ 已购买后提交 | ❌ | ✅ 情感汇总 | ✅ 情感汇总 |
| AI 聊天助手 | ✅ 客服对话 | ❌ | ✅ 查看会话历史 | ✅ 查看会话历史 |
| AI 商品文案生成 | ❌ | ❌ | ❌ | ✅ 触发 / 审核 / 保存 |
| AI 需求预测 | ❌ | ✅ 查看预测 + 补货建议 | ✅ | ✅ |
| AI 订单异常检测 | ❌ | ✅ 处理风险队列 | ✅ | ✅ |
| **优惠券** *(Phase 7)* | ✅ 应用优惠券 | ❌ | ✅ 查看使用统计 | ✅ 完整 CRUD |
| **积分系统** *(Phase 7)* | ✅ 查看余额 + 兑换 + 明细 | ❌ | ✅ 查看汇总报表 | ✅ 手动调整 / Tier 门槛管理 |
| 审计日志 | ❌ | ✅ 只读 | ✅ 完整搜索 | ✅ 完整搜索 |
| 报表 | ❌ | ✅ 只读 | ✅ 完整查看 | ✅ 完整查看 |
| **可观测性 dashboard** *(Phase 9)* | ❌ | ❌ | ✅ 只读 SLA 仪表板 | ✅ 完整 + 告警配置 |

---

## 📦 模块需求详解

### 1. 用户认证 / 账户管理

#### 1.1 Customer - 注册 / 登录
- **操作入口**：导航栏「Login」/「Sign Up」按钮
- **注册表单字段**：
  - Email（必填，全局唯一，做服务端验证）
  - Password（最少 12 字符 / 至少包含 1 个字母和 1 个数字）
  - Display Name（必填）
- **登录返回（2026-06-06 升級）**：
  - **JWT access token 寫入 HTTP-only / Secure / SameSite=Strict cookie（15 分钟有效）** — 防 XSS 盗取
  - **Refresh token 寫入獨立 HTTP-only cookie（14 天有效，每次刷新轮换 / rotation）**
  - **CSRF token 寫入非 HTTP-only cookie + 必须在每个状态变更请求头 `X-CSRF-Token` 中回传**（double-submit pattern）
  - 響應 body 不再回傳 token 字串 — 全部由 cookie 自动携带
- **Token 刷新**：调用 `/api/v1/auth/refresh`（携带 refresh cookie + CSRF）→ 旧 refresh token 立即失效，发新对 token 写入 cookie
- **登出**：调用 `/api/v1/auth/logout` → 服务端撤销 refresh token + Set-Cookie 清空全部 auth cookies
- **设计说明**：此模式相比 localStorage 储存 JWT 的优势 — XSS 不能直接读取 token；refresh rotation 检测被盗 token 重用；CSRF token 防御跨站请求。對應 Job B-2 履歷條目「HTTP-only cookies and refresh-token rotation on top of JWT authentication」。詳見 `docs/adr/0007-jwt-cookie-vs-localstorage.md`。

#### 1.2 Customer - 个人资料
- 编辑 Display Name、Phone
- Email 在 MVP 阶段不可修改
- 多个收货 / 账单地址（CRUD），可设置默认地址

#### 1.3 Staff / StoreManager / Administrator - 预置與管理
- 应用启动时自动生成 1 个 Administrator 账号（用户名 / 密码来自 `appsettings`）
- StoreManager 账号由 Administrator 在用户管理界面手动创建
- Staff 账号由 StoreManager 或 Administrator 手动创建

---

### 2. 商品目录

#### 2.1 Customer / 访客 - 浏览商品
- **入口**：首页、类别页、搜索栏
- 支持分页（`?page=&pageSize=`）
- 支持按类别过滤
- 支持商品名称 / 描述的全文搜索（大小写不敏感）
- 商品详情页展示：
  - 商品名 / 描述
  - 多张图片（轮播）
  - 多个变体（Size / Color 选择器）
  - 价格 / 划线价（如有）
  - 库存指示器：In Stock / Low Stock (< 10) / Out of Stock
  - 评论列表 + 平均评分

#### 2.2 Admin - 创建 / 编辑商品
- **操作入口**：「Create Product」按钮（Admin Dashboard）
- **表单字段**：
  - 商品名（必填）
  - SKU（必填，全局唯一）
  - Slug（自动从名称生成，可编辑）
  - 描述（富文本 / Markdown）
  - SEO Title / Meta Description
  - 类别（下拉，最多 3 级）
  - 品牌
  - 是否发布（IsPublished）
- **变体管理**：可添加 / 编辑 / 删除多个变体
  - 每个变体有 SKU、Options JSON（size / color）、价格（cents）
- **图片上传**：
  - 支持文件：jpg、png、webp
  - 可设置主图（Primary Image）
  - 存储到 Azure Blob 容器 `product-images`
- **AI 文案生成**：见模块 8

#### 2.3 Admin - 商品列表
- 完整商品列表（默认排除已软删除商品）
- 「Show Deleted」开关查看已删除商品（可恢复）
- 每行展示：图片缩略图、SKU、名称、类别、发布状态、库存状态
- 操作按钮：编辑 / 软删除 / 恢复

---

### 3. 购物车与结账

#### 3.1 购物车
- 访客（未登录）：基于 `X-Anon-Cart-Key` Cookie 维护购物车
- 登录用户：账户级购物车
- 登录后，匿名购物车自动合并到账户购物车
- 操作：加入 / 修改数量 / 移除
- 价格快照机制：加入时记录单价；结账时重新计算，若有变化提示用户

#### 3.2 结账（Stripe Checkout）
1. 用户点击「Checkout」
2. 后端创建 `InventoryReservation`（15 分钟有效）
3. 创建 Stripe Checkout Session，跳转到 Stripe 托管支付页
4. 用户使用测试卡 `4242 4242 4242 4242` 支付
5. Stripe Webhook（`POST /api/v1/payments/stripe/webhook`）触发：
   - 签名验证（`EventUtility.ConstructEvent`）
   - 幂等性检查（通过 `ProcessedStripeEvent` 表去重）
   - `checkout.session.completed`：提交 reservation → 创建 Order + OrderLines → 标记 cart 为 Converted
   - `charge.refunded`：标记 Order Refunded + 回滚库存
6. 跳转回 `/checkout/success?orderId=...`

#### 3.3 购物车过期
- 30 分钟未更新的 Cart 由 `CartExpirySweeper` 自动清理
- Reservation 释放，库存可用

---

### 4. 订单管理

#### 4.1 Customer - 我的订单
- 列表展示：订单号、下单日期、状态、金额、商品数
- 详情页展示：
  - 订单项明细（图片、名称、SKU、单价、数量、小计）
  - 物流信息（如已发货）：carrier、tracking number
  - Stripe 支付状态
- 取消 Pending 订单：
  - 若已支付，触发 Stripe 全额退款
  - 库存回滚

#### 4.2 Staff / StoreManager - 订单工作台
- 全部订单列表，支持筛选：
  - 状态（Pending / Paid / Fulfilled / Cancelled / Refunded）
  - 日期范围
  - 客户邮箱
  - 是否有异常标记
- 操作：
  - 「Mark as Shipped」：输入 carrier + tracking number，创建 `Shipment`
  - 「Mark as Delivered」
  - 查看订单异常（见模块 10）

#### 4.3 Admin - 退款
- 全额退款：
  - 调用 Stripe Refund API
  - `Order.Status = Refunded`
  - 库存回滚
  - 写入 AuditLog
- 部分退款（MVP 范围外，可未来扩展）

---

### 5. 库存管理

#### 5.1 库存模型
- 每个 `ProductVariant` 对应 1 个 `InventoryItem`
- 字段：`OnHand`（实际在库）、`Reserved`（被预留）、`RowVersion`（乐观锁）
- 计算字段：`Available = OnHand - Reserved`

#### 5.2 Staff / StoreManager / Administrator - 调整库存
- 操作入口：库存页面 → 选中变体 → 「Adjust」
- 表单：变化量（可正可负）、原因（必填，如「Warehouse count correction」）
- 写入 AuditLog

#### 5.3 库存乐观锁
- 任何更新使用 `Where(i => i.RowVersion == original).ExecuteUpdateAsync(...)`
- 0 行受影响 → 抛出 `ConcurrencyException` → API 返回 HTTP 409

#### 5.4 补货提示（AI 模块 9 提供数据）
- 「Reorder Hints」面板展示自动生成的补货建议
- 每条建议：商品变体、推荐订购数量、推理说明（「预测未来 14 天 120 件 / 在手 30 件 / 交付周期 7 天 → 补 90 件」）
- 操作：「Dismiss」（已处理）

---

### 6. 评论系统 + AI 情感分析（Phase 4）

#### 6.1 Customer - 提交评论
- 仅在购买并完成订单后可对该商品评论
- 表单：星级 1-5、评论正文（最多 4000 字符）
- 每个用户对每个商品只能评论 1 次

#### 6.2 商品页 - 展示评论
- 分页显示
- 平均评分 + 评分分布柱状图

#### 6.3 后端 - 情感分析（异步）
- 评论入库 → 抛出 `ReviewCreated` 域事件 → 写入 `Channel<Guid>` 队列
- `ReviewSentimentHostedService` 消费队列：
  - 调用 Azure AI Language Sentiment Analysis API（F0 免费层，5k/月）
  - 持久化 `SentimentScore`（-1 到 1）+ `SentimentLabel`（Positive / Neutral / Negative / Mixed）
- 失败处理：Polly 重试；持续失败则保留 `ProcessedAt=null`，由慢周期 sweeper 重试

#### 6.4 Admin - 情感汇总
- 商品详情页展示该商品所有评论的平均 SentimentScore + label 分布
- 「Products Needing Attention」面板列出平均 Sentiment < -0.2 的商品

---

### 7. AI 客服聊天助手（Phase 5）

#### 7.1 Customer - 聊天体验
- 操作入口：登录后任意页面右下角浮动按钮
- 点击打开 Tailwind + Radix Drawer 聊天面板
- 输入消息 → POST 到 `/api/v1/chat/webhook`（JWT 认证）
- 后端流程：
  1. 验证 JWT 提取 CustomerProfileId
  2. 加载或新建 `ChatSession`（按 conversationId）
  3. 拉取该顾客最近 5 个订单 + 10 行订单明细（RAG-lite）
  4. 调用 Anthropic Claude API（Sonnet）
  5. 使用 **Tool Use** 让 Claude 主动调用工具：
     - `get_order(orderNumber)`：查特定订单
     - `list_my_recent_orders()`：列最近订单
     - `get_shipping_status(orderNumber)`：查物流
     - `start_return(orderNumber, lineId, reason)`：发起退货
  6. 持久化 `ChatMessage`（包括工具调用记录）
  7. 返回助手消息
- 失败处理：Anthropic 5xx / timeout → HTTP 200 + 友好消息「I'm having trouble right now, try again in a moment」

#### 7.2 Stretch（Phase 6） - Copilot Studio 集成
- 在 Microsoft tenant 中创建 Copilot Studio 机器人
- 机器人通过 HTTP Action 调用同一个 `/api/v1/chat/webhook`
- 仅认证方式不同（HMAC 签名而非 JWT）
- 同一份 Claude / tool 逻辑，无需重新开发

#### 7.3 Admin - 会话诊断
- 管理后台「Chat Sessions」页面
- 可查看任意会话的完整消息历史（用户、助手、工具调用）

---

### 8. AI 商品文案生成（Phase 4）

#### 8.1 Admin - 操作流程
- 操作入口：商品编辑页「Suggest Description」按钮
- 弹窗选择：
  - 语调（tone）：playful / professional / luxury
  - 目标长度：short / medium / long
- 后端流程：
  1. 拉取商品属性（名称、类别、品牌、变体 options）
  2. 选 2 个相似商品的描述作为 in-context 示例
  3. 单次 Claude API 调用，定义 `emit_product_copy` 工具确保结构化输出
  4. 返回 JSON：
     ```json
     {
       "description": "...",
       "seoTitle": "...",
       "seoMetaDescription": "...",
       "bulletPoints": ["...", "..."]
     }
     ```
- 前端预览：差异化显示，提供「Accept」/「Reject」按钮
- **AI 输出永不自动保存**，必须 Admin 手动确认

---

### 9. AI 需求预测 + 补货建议（Phase 5）

#### 9.1 模型训练
- 每日 UTC 07:00 通过 GitHub Actions（`ml-train.yml`）触发
- 训练算法：ML.NET SSA（`ForecastBySsa`）
- 参数：`windowSize=14, seriesLength=90, trainSize=180, horizon=14`
- 数据：每个变体最近 180 天的日销量（缺失日补 0）
- 输出：`model-{variantId}.zip` → Azure Blob `ml-models/` 容器

#### 9.2 预测使用
- API 启动时加载 `ModelStore`（按需 lazy load + IMemoryCache）
- 每日凌晨 `ForecastRefreshHostedService` 跑预测：
  - 写入 `DemandForecast`（每个变体 1 条最新）
  - 计算 `ReorderHint`：
    - 安全库存 = `1.65 × stdev(daily_demand) × sqrt(7)`（交付周期 7 天）
    - 推荐订购量 = `max(0, forecast14d + safetyStock - OnHand)`

#### 9.3 数据不足处理
- 历史 < 30 天 → 跳过该变体，`Confidence=0`
- UI 显示「Forecast warming up」

> **As-built (Phase 5B 实作偏差，见 `PHASE_5B_FORECAST_SCOPE.md` + ADR-0012):** 模型改用纯 C#
> **Holt-Winters**（非 ML.NET SSA — SSA 的 Intel MKL/`libiomp5` 原生依赖在 Linux 上缺失）；预测以
> **DB 行**写入（无 Azure Blob / ModelStore — Phase 8 延后）；冷启动变体**跳过不写行**（以"行不存在"
> 表示 warming up，而非写 `Confidence=0` 哨兵行）；`ml-train.yml` 为 build-only 脚手架，真正的每日刷新
> 是进程内 `ForecastRefreshHostedService`。

---

### 10. AI 订单异常检测（Phase 5）

> 评论情感分析 见模块 6（Phase 4），不属于本模块。

#### 10.1 检测算法
- `OrderAnomalyHostedService` 每 15 分钟扫描最近订单
- 三个判定规则（任一命中即标记为异常）：
  1. **Z-score 异常**：`|order.Total - customerMean| / customerStdev > 3`
     - 使用该顾客最近 50 个订单（若 < 5 个则用全局均值）
  2. **新地理位置**：本次收货国家从未在该顾客之前的订单出现
  3. **数量异常**：单订单中某单品 > 5 件
- 写入 `OrderAnomaly`：score、reason、detected_at

#### 10.2 风险队列（Staff / StoreManager）
- 「Risk Queue」面板列出未处理的异常
- 标记异常的订单 **不能** 进入「Mark as Shipped」流程，须先 acknowledge
- 操作：「Acknowledge」（Staff 或 StoreManager 已查看）

#### 10.3 为什么不用 Azure Anomaly Detector
- Microsoft 已宣布 Azure Anomaly Detector 退役，不再推荐
- Z-score 算法透明、可解释、对 portfolio MVP 足够好
- 详见 `docs/adr/0003-zscore-not-anomaly-detector.md`

---

### 11. 审计与报表

#### 11.1 审计日志
- `AuditingInterceptor` 拦截所有 `SaveChangesAsync`
- 监控的实体：`Product`、`InventoryItem`、`Order`、`Payment`、`Shipment`
- 每条记录：Actor（用户 ID 或 'system'）、Action（Insert / Update / Delete / 业务动作）、EntityType、EntityId、BeforeJson、AfterJson、OccurredAt
- Staff（只读）/ StoreManager / Administrator 可搜索：按 actor、entity type、entity id、日期范围（Staff 仅可查看，不可导出）

#### 11.2 报表
- 销售按日：日期、订单数、总销售额、按类别拆分
- 情感汇总：商品、平均评分、平均 sentiment、评论数
- 异常汇总：按周 / 月聚合，命中规则分布

---

### 12. 系统配置

#### 12.1 Admin - 用户管理
- 列表：所有 AppUser，可按角色筛选
- 操作：
  - 邀请 Staff 或 StoreManager（生成 invitation token，邮件发送由 future scope 处理；MVP 中直接控制台输出）；仅 Administrator 可邀请 StoreManager
  - 启用 / 禁用账号
  - 重置密码（生成临时密码）

#### 12.2 应用配置（来自 Key Vault）
- Anthropic API Key
- Stripe Secret Key / Webhook Secret
- JWT 签名密钥
- Azure AI Language endpoint / key
- HMAC chat webhook secret

---

## 🔐 权限逻辑简表（4 角色）

| 功能 | Customer | Staff | StoreManager | Administrator |
|---|:-:|:-:|:-:|:-:|
| 注册账号 | ✅ | ❌ | ❌ | ❌ |
| 浏览商品 | ✅ | ✅ | ✅ | ✅ |
| 加入购物车 / 结账 | ✅ | ❌ | ❌ | ❌ |
| 应用优惠券 / 兑换积分 *(Phase 7)* | ✅ | ❌ | ❌ | ❌ |
| 查看自己的订单 / 积分明细 | ✅ | — | — | — |
| 取消 Pending 订单 | ✅ | — | — | — |
| 提交评论 | ✅ | ❌ | ❌ | ❌ |
| AI 聊天 | ✅ | ❌ | 查看历史 | 查看历史 |
| 查看全部订单 | ❌ | ✅ | ✅ | ✅ |
| 标记发货 / 到货 | ❌ | ✅ | ✅ | ✅ |
| 退款 | ❌ | ❌ | ✅ | ✅ |
| 调整库存 | ❌ | ✅ | ✅ | ✅ |
| 处理订单异常 | ❌ | ✅ | ✅ | ✅ |
| 关闭补货提示 | ❌ | ✅ | ✅ | ✅ |
| CRUD 商品 / 类别 | ❌ | ❌ | ❌ | ✅ |
| CRUD 优惠券 *(Phase 7)* | ❌ | ❌ | ❌ | ✅ |
| 手动调整积分 *(Phase 7)* | ❌ | ❌ | ❌ | ✅ |
| Tier 门槛管理 *(Phase 7)* | ❌ | ❌ | ❌ | ✅ |
| 触发 AI 文案生成 | ❌ | ❌ | ❌ | ✅ |
| 查看审计日志 | ❌ | 只读 | ✅ 完整搜索 | ✅ 完整搜索 |
| 查看报表（销售 / 情感 / 异常 / 优惠券 / 积分） | ❌ | 只读基础 | ✅ 全部 | ✅ 全部 |
| 创建 / 停用 Staff 账号 | ❌ | ❌ | ✅ | ✅ |
| 创建 / 停用 StoreManager 账号 | ❌ | ❌ | ❌ | ✅ |
| 角色管理 / 应用配置 | ❌ | ❌ | ❌ | ✅ |
| 查看可观测性 dashboard *(Phase 9)* | ❌ | ❌ | 只读 | ✅ 完整 + 告警 |

---

## 🛠️ Epic / Story / Task 分解

按照 `docs/PLAN.md` 的 7 个 Phase 组织。每个 Task 完成后必须满足 `docs/CODING_STANDARDS.md` 中的「Definition of Done」。

### 📌 Epic 0：项目初始化（Phase 0）

#### Story 0.1：仓库与文档
- Task 0.1.1 初始化 Git 仓库，创建 `.gitignore`、`.editorconfig`、`Directory.Build.props`
- Task 0.1.2 完成 4 份文档（PLAN / REQUIREMENTS / CODING_STANDARDS / DATABASE_DESIGN）✅ 已完成
- Task 0.1.3 编写 README（项目简介、3 步启动、文档链接）
- Task 0.1.4 创建 ADR 文件夹 + 5 个初始 ADR（0001 net8-vs-net9、0002 no-mediatr、0003 zscore-not-anomaly-detector、0004 mvc-controllers-over-minimal-apis、0005 multi-provider-llm）

#### Story 0.2：后端骨架（三层架构）
- Task 0.2.1 创建 `Retail.sln` 与项目：`Retail.Api`（单项目三层架构）、`Retail.Ml`、`Retail.Ml.Trainer`、`Retail.Tests.Unit`、`Retail.Tests.Integration`
- Task 0.2.2 在 `Retail.Api` 中创建三层文件夹：`Controllers/`、`Services/`、`Repositories/`、`Domain/Entities/`、`DTOs/Requests/`、`DTOs/Responses/`、`Data/Configurations/`、`Middlewares/`、`Validators/`、`Mappers/`、`Ai/`、`Payments/`、`Storage/`、`Identity/`、`HostedServices/`、`Exceptions/`、`Common/{Constants,Enums,Extensions,Helpers,Models}/`
- Task 0.2.3 配置 `Program.cs`：MVC Controllers (`AddControllers`)、Serilog、OpenTelemetry、健康检查、Swagger、Identity、JWT、FluentValidation、Polly
- Task 0.2.4 创建初始 `RetailDbContext`（仅 Identity 表）+ 迁移 `0000_init`
- Task 0.2.5 实现统一 `ApiResponse<T>` + `ExceptionMiddleware`（见 CODING_STANDARDS）
- Task 0.2.6 实现 `AuditingInterceptor` 骨架（`Data/Interceptors/`）
- Task 0.2.7 创建首个空 `HealthController`（验证三层骨架可启动）

#### Story 0.3：前端骨架（Tailwind + shadcn/ui）
- Task 0.3.1 Vite + React + TS + ESLint + Prettier 初始化
- Task 0.3.2 **Tailwind CSS 3.4 安装 + 配置 `tailwind.config.ts`（主题 token、自定义 color palette、字体）**
- Task 0.3.3 **shadcn/ui 初始化 + 复制首批 4 个 UI primitives 到 `src/components/ui/`**：`Button`、`Input`、`Card`、`Toast`（含 use-toast hook）
- Task 0.3.4 TanStack Query Provider + Zustand store 骨架
- Task 0.3.5 React Router 路由：`/`（首页占位）、`/admin`（占位，受 role guard 保护）
- Task 0.3.6 OpenAPI client 生成脚本（`pnpm gen:api`）+ `apiClient` 包装（自动携带 CSRF token）
- Task 0.3.7 **jscpd 基线報告生成**（`pnpm jscpd-baseline`）→ commit 到 `docs/perf/jscpd-baseline.md`，Phase 10 复测时对比 45% 减少目标

#### Story 0.4：本地开发
- Task 0.4.1 编写 `docker/docker-compose.yml`（api、web、sqlserver、azurite）
- Task 0.4.2 编写 `Retail.Api/Dockerfile`（multi-stage build）
- Task 0.4.3 `.env.example` 列出所有所需环境变量
- Task 0.4.4 README 3 命令启动指南

#### Story 0.5：CI/CD 与 IaC
- Task 0.5.1 GitHub Actions `ci.yml`（api 构建测试、web typecheck lint test、bicep lint）
- Task 0.5.2 Bicep 骨架：`main.bicep` + 12 个 module 占位（apim、containerApps、sql、keyVault、storage、ai、monitoring、registry、staticWebApp、serviceBus、eventGrid、functions — 与 PLAN.md §4 保持一致；2026-06-08 修正：原写 8 个，未反映事件驱动架构 pivot 新增的 4 个模块）
- Task 0.5.3 `iac.yml` 工作流（`bicep build` + `bicep what-if`）

---

### 📌 Epic 1：商品目录与认证（Phase 1）

#### Story 1.1：认证系统
- Task 1.1.1 配置 ASP.NET Core Identity（`IdentityUser<Guid>` + 角色）
- Task 1.1.2 角色与 Admin 账号 seeder（应用启动时）
- Task 1.1.3 注册 / 登录 / 刷新 / 登出端点（`/api/v1/auth/*`）
- Task 1.1.4 FluentValidation 验证器（注册表单：密码强度、邮箱唯一）
- Task 1.1.5 JWT 配置（密钥来自 user-secrets / Key Vault）
- Task 1.1.6 单元测试：密码 hash、JWT 生成 / 验证
- Task 1.1.7 集成测试：注册 → 登录 → 调受保护端点

#### Story 1.2：商品 / 类别 / 变体
- Task 1.2.1 `Domain/Entities/` 添加 Product、ProductVariant、Category、InventoryItem 实体
- Task 1.2.2 `Data/Configurations/` 添加 EF Core 配置类（`ProductConfiguration` 等）
- Task 1.2.3 迁移 `0001_catalog`（Product、ProductVariant、Category、InventoryItem、Address、CustomerProfile、Seq_OrderNumber）
- Task 1.2.4 `Repositories/` 实现 `IProductRepository` / `ProductRepository`、`ICategoryRepository` / `CategoryRepository`、`IInventoryRepository` / `InventoryRepository`
- Task 1.2.5 `Services/` 实现 `ICatalogService` / `CatalogService`（含分页、筛选、搜索逻辑）
- Task 1.2.6 `Controllers/CatalogController.cs`：公共读取端点（`GET /products`、`GET /products/{slug}`、`GET /categories`）
- Task 1.2.7 `Controllers/CatalogController.cs`：Admin 写入端点（POST / PUT / DELETE Product / Variant），加 `[Authorize(Roles = Roles.Admin)]`
- Task 1.2.8 商品图片上传端点（multipart → `IBlobStorageClient` → Azure Blob / Azurite）
- Task 1.2.9 `RetailDbContext.OnModelCreating` 软删除全局过滤器

#### Story 1.3：前端 - 商品浏览
- Task 1.3.1 商品列表页（分页、类别筛选、搜索框）
- Task 1.3.2 商品详情页（图片轮播、变体选择器、库存指示器）
- Task 1.3.3 Admin 商品列表（pre-component-library 简版，Phase 3 重构为 DataTable 复用）
- Task 1.3.4 Admin 商品创建 / 编辑表单（含图片上传）

#### Story 1.4：用户资料
- Task 1.4.1 `/api/v1/auth/me` + Profile / Address CRUD 端点
- Task 1.4.2 前端「My Account」页面

---

### 📌 Epic 2：购物车与订单（Phase 2）

#### Story 2.1：购物车
- Task 2.1.1 迁移 `0002_orders`（Cart、CartItem、InventoryReservation、Order、OrderLine、Payment、ProcessedStripeEvent、Shipment）
- Task 2.1.2 Cart 端点（`GET`、`POST items`、`PATCH items/{id}`、`DELETE items/{id}`）
- Task 2.1.3 匿名 Cart 支持（基于 `X-Anon-Cart-Key` Cookie）
- Task 2.1.4 登录后 Cart 合并逻辑
- Task 2.1.5 前端 Cart Drawer

#### Story 2.2：Stripe 结账
- Task 2.2.1 `POST /api/v1/checkout/session` 端点（创建 reservation + Stripe Session）
- Task 2.2.2 Stripe Webhook 端点（签名验证 + 幂等处理）
- Task 2.2.3 `checkout.session.completed` 处理逻辑（提交 reservation、创建 Order + OrderLine、关闭 cart）
- Task 2.2.4 `charge.refunded` 处理逻辑（库存回滚、Order Refunded）
- Task 2.2.5 集成测试：完整结账流程（Testcontainers + Stripe CLI fixture）
- Task 2.2.6 集成测试：重复 webhook 事件幂等性
- Task 2.2.7 集成测试：并发购买最后一件 → 仅一个成功

#### Story 2.3：库存与并发
- Task 2.3.1 `InventoryItem.RowVersion` 配置 + `ExecuteUpdateAsync` 并发更新模式
- Task 2.3.2 `CartExpirySweeper` BackgroundService（30 分钟超时释放）
- Task 2.3.3 库存调整端点（带 reason + idempotency key）

#### Story 2.4：订单查看
- Task 2.4.1 Customer：`GET /api/v1/orders` / `GET /orders/{id}` / `POST /orders/{id}/cancel`
- Task 2.4.2 前端 Customer「My Orders」页面 + 详情
- Task 2.4.3 取消 Pending 订单 → 触发 Stripe Refund + 回滚库存

---

### 📌 Epic 3：管理后台与审计（Phase 3）

#### Story 3.1：Tailwind 自建管理后台 Shell（替代原 Refine + MUI 方案，2026-06-06 改）
- Task 3.1.1 `AdminShell` layout 组件（Tailwind grid：左侧 sidebar nav + 顶部 topbar + 主内容区）
- Task 3.1.2 `SidebarNav` 组件 — 基于当前用户 role claim 动态显示菜单项（Staff 只见订单/库存；StoreManager 加报表 + 退款；Administrator 加目录/优惠券/积分/Tier/用户管理）
- Task 3.1.3 `RoleGuard` 路由守卫组件 — 检查 JWT cookie 中的 role claim 后才渲染 children
- Task 3.1.4 **扩展 `components/ui/` 组件库至 12+ 個**：`Select`、`Checkbox`、`Modal`（Radix Dialog）、`Drawer`（Radix Dialog 變體）、`DataTable`（含排序 / 分页 / 筛选）、`FilterPanel`、`Pagination`、`Tabs`、`EmptyState` — 合計超過 12 個 reusable primitives，對應 Job B-1 履歷條目
- Task 3.1.5 Admin 资源页面骨架：Products list、Orders list、Inventory list、Categories list — 完全用 `DataTable` + `FilterPanel` 拼装

#### Story 3.2：订单管理后台
- Task 3.2.1 订单列表（筛选状态、日期、客户、异常）
- Task 3.2.2 订单详情页（包含物流和支付信息）
- Task 3.2.3 「Mark as Shipped」流程 → 创建 Shipment
- Task 3.2.4 「Mark as Delivered」流程
- Task 3.2.5 退款流程（仅 Admin）

#### Story 3.3：审计日志
- Task 3.3.1 完善 `AuditingInterceptor`（捕获 Before / After JSON）
- Task 3.3.2 `GET /api/v1/audit-logs` 端点（搜索 / 分页）
- Task 3.3.3 前端审计日志查看页

#### Story 3.4：角色权限强化
- Task 3.4.1 后端策略：`[Authorize(Roles = "Administrator,StoreManager,Staff")]`（视端点而定）；常數定義於 `Common/Constants/Roles.cs`
- Task 3.4.2 前端路由守卫（基于 JWT role claim）
- Task 3.4.3 集成测试：伪造 Customer JWT 调 admin 端点 → 403

#### Story 3.5：基础报表
- Task 3.5.1 「Sales by Day」端点（按日期范围聚合）
- Task 3.5.2 前端报表页面（折线图，使用 **Recharts**）

#### Story 3.6：E2E 测试
- Task 3.6.1 Playwright golden path：注册 → 加购 → 结账 → 查看订单
- Task 3.6.2 Playwright admin flow：登录 → 创建商品 → 标发货

---

### 📌 Epic 4：AI - 文案生成 + 评论情感（Phase 4）

#### Story 4.1：评论功能
- Task 4.1.1 迁移 `0004_reviews_sentiment`（Review 表）
- Task 4.1.2 `POST /api/v1/products/{id}/reviews` 端点（验证已购买）
- Task 4.1.3 `GET /api/v1/products/{id}/reviews` 端点（分页）
- Task 4.1.4 前端商品详情页评论区 + 评论表单

#### Story 4.2：商品文案生成
- Task 4.2.1 `ClaudeClient`（基于 Anthropic.SDK 或 Refit）+ Resilience handler
- Task 4.2.2 `CopyGenService`：构造 prompt + tool definition `emit_product_copy`
- Task 4.2.3 `POST /api/v1/catalog/products/{id}/generate-copy` 端点
- Task 4.2.4 前端 Admin 商品编辑页「Suggest Description」按钮 + 弹窗（tone / length 选择）
- Task 4.2.5 前端 diff 视图 + Accept / Reject 按钮
- Task 4.2.6 `Ai:Mode = stub` 实现（用 fixtures 替代实际调用）

#### Story 4.3：评论情感分析
- Task 4.3.1 `TextAnalyticsClientAdapter`（基于 Azure.AI.TextAnalytics）
- Task 4.3.2 `ReviewSentimentHostedService`（Channel<Guid> 消费队列）
- Task 4.3.3 域事件 `ReviewCreated` 入队
- Task 4.3.4 后台处理：调用 Azure AI Language → 持久化 SentimentScore
- Task 4.3.5 前端 Admin 商品详情页情感汇总图表
- Task 4.3.6 「Products Needing Attention」面板（avg sentiment < -0.2）

---

### 📌 Epic 5：AI - 聊天 / 预测 / 异常（Phase 5）

#### Story 5.1：聊天后端
- Task 5.1.1 迁移 `0005_chat_forecast_anomaly`（ChatSession、ChatMessage、DemandForecast、ReorderHint、OrderAnomaly）
- Task 5.1.2 `ChatService`：构造 system prompt + tool definitions
- Task 5.1.3 Claude tool 实现：`get_order`、`list_my_recent_orders`、`get_shipping_status`、`start_return`
- Task 5.1.4 `POST /api/v1/chat/webhook` 端点（JWT 模式）
- Task 5.1.5 prompt caching 应用（system prompt 静态部分）
- Task 5.1.6 集成测试：模拟用户多轮对话

#### Story 5.2：聊天前端
- Task 5.2.1 `ChatDrawer` 组件（Radix Dialog 变体 + Tailwind 样式 + 消息列表 + 输入框）
- Task 5.2.2 浮动按钮触发 Drawer（仅登录 Customer 可见）
- Task 5.2.3 消息流（用户消息、助手消息、工具调用气泡）
- Task 5.2.4 加载状态 + 错误提示
- Task 5.2.5 `Ai:Mode = stub` 时使用本地 fixtures

#### Story 5.3：聊天诊断后台
- Task 5.3.1 `GET /api/v1/chat/sessions/{id}/history`（Admin only）
- Task 5.3.2 Admin「Chat Sessions」页面

#### Story 5.4：需求预测训练
- Task 5.4.1 `Retail.Ml.Trainer` CLI 项目
- Task 5.4.2 `SsaForecastService`（数据加载、补 0、训练、保存模型）
- Task 5.4.3 `ml-train.yml` GitHub Actions 工作流（cron 每日运行）
- Task 5.4.4 模型存储到 Azure Blob `ml-models/`
- Task 5.4.5 `manifest.json` 记录模型版本和准确度

#### Story 5.5：预测使用 + 补货建议
- Task 5.5.1 `ModelStore`：lazy load + IMemoryCache
- Task 5.5.2 `ForecastRefreshHostedService`：每日跑预测 + 写入 DemandForecast
- Task 5.5.3 `ReorderHint` 生成逻辑（包含 safety stock 算法）
- Task 5.5.4 `GET /api/v1/analytics/forecast` 端点
- Task 5.5.5 前端 Admin「Inventory」面板预测 tile
- Task 5.5.6 Reorder Hint 列表 + Dismiss 操作

#### Story 5.6：订单异常检测
- Task 5.6.1 `OrderAnomalyService`（Z-score、新地理、数量异常 3 个规则）
- Task 5.6.2 `OrderAnomalyHostedService`（每 15 分钟扫描）
- Task 5.6.3 Risk Queue 面板 + Acknowledge 操作
- Task 5.6.4 标记异常的订单阻止 Mark Shipped（除非已 ack）

#### Story 5.7：演示数据
- Task 5.7.1 Seeder：6 个月合成订单数据（含周期性 + 趋势）
- Task 5.7.2 演示 README 说明（演示客户、演示订单异常）

---

### 📌 Epic 6：Azure 部署与可观测性（Phase 6）

#### Story 6.1：Bicep 完整实现
- Task 6.1.1 `monitoring.bicep`（Log Analytics + App Insights）
- Task 6.1.2 `keyVault.bicep`（Vault + access policies for MI）
- Task 6.1.3 `storage.bicep`（Storage Account + blob containers）
- Task 6.1.4 `sql.bicep`（SQL Server + Serverless DB + MI auth）
- Task 6.1.5 `ai.bicep`（Azure AI Language F0）
- Task 6.1.6 `registry.bicep`（ACR Basic）
- Task 6.1.7 `containerApps.bicep`（ACA env + app + secretref）
- Task 6.1.8 `staticWebApp.bicep`（SWA linked to ACA）
- Task 6.1.9 `dev.bicepparam` / `prod.bicepparam`

#### Story 6.2：CI/CD 完整流水线
- Task 6.2.1 配置 Entra App Registration + OIDC federated credential
- Task 6.2.2 `cd-staging.yml`（push to main → 构建镜像 → 推 ACR → 更新 ACA → smoke test）
- Task 6.2.3 `cd-prod.yml`（手动 dispatch + 环境审批 → 重 tag + 部署）
- Task 6.2.4 `iac.yml` 完善（PR what-if comment、merge deploy）

#### Story 6.3：可观测性
- Task 6.3.1 OpenTelemetry 完整配置（HTTP / EF Core / 自定义 ActivitySource）
- Task 6.3.2 自定义 metrics（orders.placed、ai.copy.generations 等）
- Task 6.3.3 App Insights workbook：API health、AI calls、ML jobs

#### Story 6.4：最终打磨
- Task 6.4.1 完整 README（含架构图、screenshot、demo URL）
- Task 6.4.2 在 staging 中 seed 演示数据
- Task 6.4.3 cost 监控（Azure Budget alert $25/mo）+ shutdown checklist
- Task 6.4.4 录制 30 秒演示视频（可选）

#### Story 6.5：Stretch - Copilot Studio（如有 tenant 访问）
- Task 6.5.1 在 Copilot Studio 中创建机器人
- Task 6.5.2 配置 HTTP Action 调用 `/chat/webhook`（HMAC mode）
- Task 6.5.3 后端 webhook 端点支持双模式（cookie JWT or HMAC）
- Task 6.5.4 Feature flag 切换 React widget 与 Copilot Studio iframe

---

### 📌 Epic 7：促銷系統 — 優惠券 + 積分 + 統一定價管線（Phase 7，~5–6 週）

> **對應履歷條目**：Job A-2（voucher 實體 + pagination/filter/sort）、Job A-3（voucher redemption Event Grid 事件）。loyalty 雖然不在原始履歷條目中，但與 voucher 共用 pricing pipeline，作為整套促銷子系統一同交付。

#### Story 7.1：統一定價管線（Pricing Pipeline）
- Task 7.1.1 定義 `IPriceModifier` 介面（`Apply(PriceContext ctx)` → 返回新 `PriceContext`）
- Task 7.1.2 實現 5 個 modifier：`SubtotalModifier`、`VoucherModifier`、`LoyaltyRedeemModifier`、`ShippingModifier`、`TaxModifier`
- Task 7.1.3 `PricingService` 編排器：固定順序 subtotal → voucher → loyalty → shipping → tax；返回不可變 `PriceBreakdown` 快照
- Task 7.1.4 `OrderPriceBreakdown` 實體 + 遷移 `0006a_order_breakdown`（持久化每筆訂單的 breakdown 快照）
- Task 7.1.5 單元測試覆蓋：純 subtotal、含 voucher、含 loyalty、voucher+loyalty 堆疊、free-shipping voucher、min spend 不達標

#### Story 7.2：優惠券（Voucher）
- Task 7.2.1 遷移 `0006b_vouchers`：`Voucher`、`VoucherRedemption` 表
- Task 7.2.2 `IVoucherRepository` + `VoucherRepository`（含 `UsesRemaining` 樂觀並發 `Decrement` 方法）
- Task 7.2.3 `VoucherService`：策略模式分派 `PercentOff` / `AmountOff` / `FreeShipping` 三種 discount type
- Task 7.2.4 `POST /api/v1/vouchers/validate`（cart 內呼叫）— 驗證 code 存在、未過期、`UsesRemaining > 0`、`MinSpendCents` 滿足、未超出 per-customer 上限
- Task 7.2.5 `POST /api/v1/cart/apply-voucher` / `DELETE /api/v1/cart/apply-voucher`（cart 內加掛 voucherId 欄位）
- Task 7.2.6 Administrator CRUD 端點 `/api/v1/vouchers`（含 pagination + filter + sort，對應 A-2）
- Task 7.2.7 `GET /api/v1/vouchers/{code}/usage`（管理員看用量統計）
- Task 7.2.8 前端 storefront：cart drawer 內 promo code 輸入框 + 應用後 breakdown 即時更新
- Task 7.2.9 前端 admin：voucher 列表 + 創建/編輯表單（type 切換動態欄位）+ 用量統計頁
- Task 7.2.10 整合測試：並發兌換最後 1 個名額 → 只有 1 個成功

#### Story 7.3：積分系統（Loyalty）
- Task 7.3.1 遷移 `0006c_loyalty`：`LoyaltyAccount`、`LoyaltyTransaction` 表（含 `IdempotencyKey` unique 約束）
- Task 7.3.2 `ILoyaltyAccountRepository` / `ILoyaltyLedgerRepository` — ledger 只 append，**永不直接修改 balance**
- Task 7.3.3 `LoyaltyService.GetBalanceAsync`（聚合 ledger）/ `EarnAsync` / `RedeemAsync` / `ExpireAsync` / `AdjustAsync`
- Task 7.3.4 `POST /api/v1/cart/apply-loyalty`（兌換點數抵扣 cart）— 100 pt = $1，cap 為 voucher 後剩餘 subtotal
- Task 7.3.5 `GET /api/v1/loyalty/me`（顧客查餘額 + 最近 20 條 transaction）
- Task 7.3.6 Administrator `GET /api/v1/loyalty/accounts`、`GET /{customerId}/ledger`、`POST /{customerId}/adjust`（手動調整含原因）
- Task 7.3.7 Administrator `GET /api/v1/loyalty/tiers` / `PUT /tiers`（管理 Bronze/Silver/Gold 門檻）
- Task 7.3.8 前端顧客：「我的積分」頁面（餘額、Tier、明細表）
- Task 7.3.9 前端 admin：account 列表 + 單一顧客 ledger 視圖 + 手動調整 modal + Tier 門檻設定
- Task 7.3.10 整合測試：相同 `IdempotencyKey` 重複 `EarnAsync` → 只寫一筆（防 Service Bus 重試重複加分）

#### Story 7.4：訂單與促銷整合
- Task 7.4.1 `POST /api/v1/checkout/session` 改為先跑 `PricingService.Calculate` → 把 `PriceBreakdown` 入 Stripe Checkout 的 `line_items` + 寫入 `OrderPriceBreakdown`
- Task 7.4.2 訂單完成（webhook 路徑）→ 寫 `VoucherRedemption` 行 + `Voucher.UsesRemaining--`
- Task 7.4.3 訂單發貨流程觸發 `OrderShipped` 事件（先 in-process publish；Phase 8 接 Service Bus）
- Task 7.4.4 `EarnLoyaltyPointsHandler`（Phase 7 in-process；Phase 8 改 Function）— 接收 `OrderShipped` → 寫 `LoyaltyTransaction(Kind=Earn)` 含 `IdempotencyKey=OrderId+":earn"`
- Task 7.4.5 全額退款處理：寫 `Adjust` 行倒扣對應 earn points（如果已 Earn）+ 釋放 voucher uses（如 voucher 條款允許）
- Task 7.4.6 `GET /api/v1/orders/{id}/breakdown` 端點（顧客查看自己訂單的完整 breakdown）

#### Story 7.5：報表與儀表板
- Task 7.5.1 `GET /api/v1/analytics/vouchers-usage`（按日期/voucher 聚合的使用次數、抵扣金額）
- Task 7.5.2 `GET /api/v1/analytics/loyalty-summary`（總發放點數、總兌換、淨負債、Tier 分布）
- Task 7.5.3 前端 admin 報表頁：兩個圖表 + 1 個 Tier 分布餅圖

---

### 📌 Epic 8：事件驅動架構 — Service Bus + Event Grid + Functions（Phase 8，~3–4 週）

> **對應履歷條目**：Job A-3「event-driven workflows using Azure Service Bus and Event Grid with Azure Functions consumers」。

#### Story 8.1：Service Bus + Event Grid 基礎設施
- Task 8.1.1 Bicep `serviceBus.bicep`：namespace + queues（`orders-confirmation`、`vouchers-redemption`、`loyalty-earn`、`loyalty-expiry-trigger`）+ 死信佇列
- Task 8.1.2 Bicep `eventGrid.bicep`：topic + subscriptions
- Task 8.1.3 Bicep `functions.bicep`：Function App Consumption (Y1)
- Task 8.1.4 Bicep `apim.bicep` 加 Stripe webhook 路由 → Event Grid 入口
- Task 8.1.5 本地 `docker-compose` 加入 Service Bus emulator（或 in-process stub）

#### Story 8.2：Functions 專案
- Task 8.2.1 創建 `Retail.Functions` 專案（.NET 10 isolated worker）
- Task 8.2.2 `StripeWebhookHandlerFn`（HTTP trigger）— 簽名驗證 + 冪等檢查 → publish 到 Event Grid
- Task 8.2.3 `OrderConfirmationFn`（Service Bus trigger，queue=`orders-confirmation`）— 提交 reservation、創建 Order
- Task 8.2.4 `VoucherRedemptionFn`（Service Bus trigger，queue=`vouchers-redemption`）— 寫 `VoucherRedemption`、扣 `UsesRemaining`
- Task 8.2.5 `EarnLoyaltyPointsFn`（Service Bus trigger，queue=`loyalty-earn`）— 取代 Phase 7 in-process handler
- Task 8.2.6 `OrderAnomalyScanFn`（Timer trigger 每 15 分鐘）— 取代 Phase 5 `BackgroundService`
- Task 8.2.7 `ForecastRefreshFn`（Timer trigger 每日）— 取代 Phase 5 `BackgroundService`
- Task 8.2.8 `PointsExpiryScheduledFn`（Timer trigger 每日）— 掃描 12 個月不活躍帳戶 → 寫 `Expire` ledger 行
- Task 8.2.9 `TierRecalcScheduledFn`（Timer trigger 每日）— 重算每帳戶滾動 12 個月消費總額 → 更新 `Tier`

#### Story 8.3：API → Service Bus / Event Grid publish
- Task 8.3.1 `IEventPublisher` 抽象 + `ServiceBusEventPublisher` + `EventGridEventPublisher` 實作
- Task 8.3.2 `OrderService` 完成訂單 → publish `OrderConfirmed` 到 Service Bus（取代直接呼叫處理邏輯）
- Task 8.3.3 `OrderService.MarkShipped` → publish `OrderShipped` 到 Service Bus（取代 Phase 7 in-process）
- Task 8.3.4 `VoucherService` 應用成功 → publish `VoucherApplied`（cart 期）；commit 時 publish `VoucherRedeemed`
- Task 8.3.5 Outbox 模式（簡化版）— 確保 DB 寫入與事件 publish 原子性

#### Story 8.4：可靠性 + 觀測
- Task 8.4.1 全部 Functions 啟用 App Insights 自動關聯
- Task 8.4.2 死信佇列消費者：簡單告警郵件（在 Phase 9 完善），先確保 DLQ 不靜默
- Task 8.4.3 `docs/runbooks/service-bus-dead-letter.md` runbook

#### Story 8.5：解耦量化測試（為 A-3 數字定錨）
- Task 8.5.1 `tests/load/event-throughput.js`（k6）連續 24h 產生 10K+ 事件
- Task 8.5.2 基線報告 `docs/perf/event-driven-coupling-baseline.md`（Phase 7 in-process 階段同步 HTTP 呼叫計數）
- Task 8.5.3 事件驅動後報告 `docs/perf/event-driven-coupling-after.md`（同流程 inter-service 同步 HTTP 呼叫計數）
- Task 8.5.4 比較結論寫入 README — 70% 減少的具體計算

---

### 📌 Epic 9：可觀測性 + SLA + Runbooks（Phase 9，~2–3 週）

> **對應履歷條目**：Job A-5「Application Insights and Azure Monitor logs, metrics, and alerts ... resolving 95%+ of alerts within SLA ... 10+ runbooks」。

#### Story 9.1：Telemetry 補完
- Task 9.1.1 OpenTelemetry 自定義 `ActivitySource` 覆盖 pricing pipeline、loyalty earn/redeem、voucher validate
- Task 9.1.2 自定義 metrics：`orders.placed`、`orders.shipped`、`vouchers.redeemed`、`loyalty.points.earned`、`loyalty.points.redeemed`、`ai.copy.generations`、`chat.tool_calls`
- Task 9.1.3 全部 Functions 設定 `WEBSITE_HTTPSCALEV2_ENABLED` + App Insights sampling
- Task 9.1.4 APIM diagnostic settings → Log Analytics workspace
- Task 9.1.5 Container Apps 日志 + Service Bus metrics → 同一 workspace

#### Story 9.2：SLA 定義
- Task 9.2.1 `docs/sla.md`：每個服務的 SLO 數字
  - API endpoints：p95 < 250ms、p99 < 500ms、5xx 比率 < 0.1%
  - Function processing：cold start excluded p95 < 5s、失敗率 < 1%
  - Stripe webhook：APIM 接收到 Function ack < 2s
  - Service Bus：訊息積壓 < 100 持續 5min 即告警
- Task 9.2.2 Azure Monitor alert rules per SLO（email + webhook 通道）

#### Story 9.3：App Insights Workbooks
- Task 9.3.1 「API Health」workbook：5xx 趨勢、p95/p99 折線、最慢端點 top 10
- Task 9.3.2 「Function Pipeline」workbook：每 queue 訊息進出、DLQ 計數、處理延遲
- Task 9.3.3 「AI Usage」workbook：每日 LLM tokens / cost、tool call 分布、stub 模式比例
- Task 9.3.4 「Promotions」workbook：voucher 兌換、loyalty 淨流動、Tier 分布
- Task 9.3.5 前端 `/admin/observability` 頁面 — 嵌入 workbook iframe（受 StoreManager+ 限制）

#### Story 9.4：Runbooks（10+ 篇）
- Task 9.4.1 `docs/runbooks/api-5xx-spike.md`
- Task 9.4.2 `docs/runbooks/stripe-webhook-failure.md`
- Task 9.4.3 `docs/runbooks/service-bus-dead-letter.md`（如 Phase 8 已寫，這裡補完）
- Task 9.4.4 `docs/runbooks/sql-throttling.md`
- Task 9.4.5 `docs/runbooks/container-apps-cold-start.md`
- Task 9.4.6 `docs/runbooks/apim-rate-limit-misfire.md`
- Task 9.4.7 `docs/runbooks/anthropic-outage-degraded-chat.md`
- Task 9.4.8 `docs/runbooks/forecast-function-failure.md`
- Task 9.4.9 `docs/runbooks/loyalty-points-discrepancy.md`
- Task 9.4.10 `docs/runbooks/voucher-uses-race-recovery.md`
- 每篇格式：「症狀 → 立即動作 → 診斷步驟 → 修復 → 防再發 → 相關 alert id」

#### Story 9.5：SLA 95% 內回應證明
- Task 9.5.1 在 staging 注入合成告警（artificially trigger 每個 alert rule 至少 1 次）
- Task 9.5.2 追蹤 acknowledge timestamp → 計算 95% 內回應比例
- Task 9.5.3 `docs/sla-evidence.md` 記錄方法與結果（為履歷數字定錨）

---

### 📌 Epic 10：效能與負載測試（Phase 10，~2 週）

> **對應履歷條目**：Job A-1（500+ daily txn、<250ms p95）、Job A-2（55% query perf）、Job B-3（1.1s → 380ms search）、Job A-3（10K+ events/day）、Job A-4（85% coverage）、Job B-1（45% dedup）、Job B-4（99% deploy success）。

#### Story 10.1：k6 腳本套件
- Task 10.1.1 `tests/load/catalog-browse.js`：分頁 + 篩選 + 搜尋（B-3 目標：p95 ≤ 380ms）
- Task 10.1.2 `tests/load/checkout-flow.js`：完整下單流程（A-1 目標：500/day @ <250ms p95）
- Task 10.1.3 `tests/load/voucher-redemption.js`：高並發兌換相同 voucher（驗證 `UsesRemaining` 競態保護）
- Task 10.1.4 `tests/load/event-throughput.js`：產生 10K+ Service Bus 訊息 / 24h（A-3）
- Task 10.1.5 `tests/load/k6-thresholds.js`：共用門檻設定（fail build if p95 > 目標）

#### Story 10.2：基線測量 → 優化 → 復測
- Task 10.2.1 基線運行 catalog-browse k6 vs Phase 6 staging（無索引調整、無 `.AsNoTracking()`）→ `docs/perf/baseline-catalog.md`（含 grafana/k6 dashboard 截圖）
- Task 10.2.2 優化批次：
  - 加 `IX_Product_CategoryId_IsPublished_Name`（複合 + covering）
  - 列表查詢改 `.AsNoTracking()` + `.Select(p => new ProductListItemDto(...))`（projection）
  - APIM 加響應 cache（30s）on `/catalog/products`
- Task 10.2.3 復測 → `docs/perf/post-optim-catalog.md`，目標 ≥55% 改善（1.1s → 380ms）
- Task 10.2.4 checkout-flow 同樣三步循環，但目標是 <250ms p95
- Task 10.2.5 README 「Performance & Reliability」section 鏈接全部報告

#### Story 10.3：代碼質量度量
- Task 10.3.1 Coverlet 報告納入 CI artifact，`ci.yml` 加 `--threshold 85` gate（失敗則 PR 紅）
- Task 10.3.2 `pnpm jscpd` 復測 → `docs/perf/jscpd-final.md`，與 Phase 0 基線比對 ≥45% 減少
- Task 10.3.3 jscpd 失敗（增加 dedup）→ CI warning（不 block，但 PR comment）

#### Story 10.4：部署成功率追蹤
- Task 10.4.1 `tests/load/deploy-stats.ts` 腳本 — 透過 GH API 拉取 `cd-staging.yml` 與 `cd-prod.yml` 最近 100 次運行結果
- Task 10.4.2 計算 success / total ratio → `docs/deployment-stats.md`（自動更新）
- Task 10.4.3 README 引用該檔案數字（B-4 99%+ 證據）

---

## 🚫 范围外 / Out of Scope（MVP，2026-06-06 更新）

- **多租户**（单店铺、非 SaaS 平台）
- **多币种 / 多仓库**（单 AUD 商店、单虚拟仓库）
- **促销 Phase 7 外的功能**：BOGO、推荐奖励（referral）、生日积分、积分倍率活动、Tier 福利（除兑换比例对等之外）、礼品卡、店内信用 — 全部 Out of Scope
- **愿望清单 / 已保存的购物车**
- **邮件 / 短信通知**（仅 UI badge 提示；事务邮件作为已知缺口在 README 标注）
- **真实物流集成**（无 AusPost API；Staff 手动输入 tracking number）
- **逐辖区税表**（统一 10% GST）
- **客服工单系统**（AI 聊天是唯一客服入口）
- **原生移动 App**（响应式 web）
- **灾备 / 地理冗余**
- **PCI 范围**（Stripe 持有卡数据，应用从不接触）

> **已移除**：原本「折扣 / 优惠券 / 促销」与「会员 / 积分体系」皆已纳入 Phase 7 Epic 7。

---

## 📖 词汇表 / Glossary

| 术语 | 含义 |
|---|---|
| **Customer** | 注册顾客（`AppUser` 角色 `Customer` + `CustomerProfile`） |
| **Staff** | 一线店员（履约、库存调整、风险队列处理）。读全部业务數據，写域内 |
| **StoreManager** | 店长（Staff 全部 + 退款 + 完整报表 + 创建 Staff + 完整审计）|
| **Administrator** | 系统管理员（StoreManager 全部 + 目录 CRUD + 优惠券/积分配置 + 角色和用户管理 + 应用配置 + AI 文案触发）|
| **Admin** | 完整权限用户。管理目录、财务、用户、配置 |
| **Variant** | 商品具体 SKU（例如「Red Shoe, Size 10」）。库存按变体记录 |
| **Reservation** | 结账时创建的临时库存预留，15 分钟过期 |
| **Commit**（inventory） | Stripe 支付确认时 `OnHand` 减扣 |
| **Idempotency key** | 客户端提供的 UUID，防止 POST 重复处理 |
| **Anomaly** | `OrderAnomalyService` 标记的可疑订单（异常金额、新地理、异常数量） |
| **Reorder hint** | 系统自动生成的补货建议 |
| **Sentiment label** | Azure AI Language 分类：Positive / Neutral / Negative / Mixed |
| **ADR** | Architecture Decision Record（`docs/adr/`），记录重大决策的「为什么」 |
| **RAG-lite** | 简化版 RAG：将顾客最近订单上下文注入 system prompt，不依赖向量数据库 |
