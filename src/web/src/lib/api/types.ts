import type { components } from '@/lib/api/schema'

// Ergonomic aliases over the generated OpenAPI schema, so feature code imports
// `ProductSummary` rather than `components['schemas']['ProductSummaryDto']`.
type Schemas = components['schemas']

export type ProductSummary = Schemas['ProductSummaryDto']
export type ProductDetail = Schemas['ProductDetailDto']
export type ProductVariant = Schemas['ProductVariantDto']
export type ProductImage = Schemas['ProductImageDto']
export type Category = Schemas['CategoryDto']
export type ProductPage = Schemas['ProductSummaryDtoPagedResult']

export type CustomerProfile = Schemas['CustomerProfileDto']
export type Address = Schemas['AddressDto']

export type Cart = Schemas['CartDto']
export type CartItem = Schemas['CartItemDto']

export type Review = Schemas['ReviewDto']
export type ReviewList = Schemas['ReviewListDto']
export type ReviewSummary = Schemas['ReviewSummaryDto']
export type ReviewPage = Schemas['ReviewDtoPagedResult']

export type OrderSummary = Schemas['OrderSummaryDto']
export type OrderDetail = Schemas['OrderDetailDto']
export type OrderLine = Schemas['OrderLineDto']
export type OrderPage = Schemas['OrderSummaryDtoPagedResult']

export type AdminUser = Schemas['AdminUserDto']
export type AdminUserPage = Schemas['AdminUserDtoPagedResult']

export type AdminOrderSummary = Schemas['AdminOrderSummaryDto']
export type AdminOrderDetail = Schemas['AdminOrderDetailDto']
export type AdminOrderPage = Schemas['AdminOrderSummaryDtoPagedResult']

export type AuditLog = Schemas['AuditLogDto']
export type AuditLogPage = Schemas['AuditLogDtoPagedResult']
export type SalesReport = Schemas['SalesReportDto']

export type SentimentSummary = Schemas['SentimentSummaryDto']
export type ProductSentiment = Schemas['ProductSentimentDto']

export type ChatTurn = Schemas['ChatTurnDto']
export type ChatWebhookRequest = Schemas['ChatWebhookRequest']
export type ChatProposedAction = Schemas['ChatProposedAction']
export type ChatSession = Schemas['ChatSessionDto']
export type ChatSessionDetail = Schemas['ChatSessionDetailDto']
export type ChatSessionPage = Schemas['ChatSessionDtoPagedResult']
