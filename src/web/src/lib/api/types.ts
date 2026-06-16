import type { components } from '@/lib/api/schema'

// Ergonomic aliases over the generated OpenAPI schema, so feature code imports
// `ProductSummary` rather than `components['schemas']['ProductSummaryDto']`.
type Schemas = components['schemas']

export type ProductSummary = Schemas['ProductSummaryDto']
export type ProductDetail = Schemas['ProductDetailDto']
export type ProductVariant = Schemas['ProductVariantDto']
export type Category = Schemas['CategoryDto']
export type ProductPage = Schemas['ProductSummaryDtoPagedResult']

export type CustomerProfile = Schemas['CustomerProfileDto']
export type Address = Schemas['AddressDto']

export type Cart = Schemas['CartDto']
export type CartItem = Schemas['CartItemDto']
