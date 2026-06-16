# Product Image Gallery — Scope (2026-06-16)

Adds a **multi-image gallery** to products, replacing the single `Product.PrimaryImageBlobKey`
upload. Decisions (confirmed by the user):

- **Per-variant images** — an image can be general (shown for the whole product) OR tied to a
  specific variant; the storefront swaps the gallery when a variant is selected.
- **Full admin manager** — upload multiple, drag-reorder, choose the primary, edit alt text
  (a11y), associate to a variant, delete.

## Data model

New entity **`ProductImage`** (`Domain/Entities/ProductImage.cs`, table `ProductImage`):

| Column | Type | Notes |
|---|---|---|
| `Id` | uniqueidentifier | PK, sequential GUID (matches `Product`) |
| `ProductId` | uniqueidentifier | FK → `Product`, **Cascade** |
| `ProductVariantId` | uniqueidentifier? | FK → `ProductVariant`, **NoAction** (null = general image) |
| `BlobKey` | nvarchar(260) | path in `product-images` container |
| `AltText` | nvarchar(200)? | for `<img alt>` / axe a11y |
| `SortOrder` | int | display order within the product |
| `IsPrimary` | bit | the product hero (card/cart image); exactly one per product |
| audit | — | `IAuditableEntity` (stamped by interceptor) |

- **Hard delete** (no `IsDeleted`): deleting an image removes the row + best-effort the blob.
- Indexes: `UX_ProductImage_Primary` = UNIQUE on `(ProductId)` WHERE `[IsPrimary] = 1`
  (one primary per product); `IX_ProductImage_ProductId_SortOrder`; `IX_ProductImage_ProductVariantId`.
- Cascade paths: `Product → ProductImage` Cascade; `ProductVariant → ProductImage` NoAction
  (avoids the multi-cascade-path error; variant delete handles its images in the service).
- Query filter `!pi.Product.IsDeleted` to stay consistent with `Product`'s soft-delete filter.

**`Product.PrimaryImageBlobKey` STAYS** as a denormalized cache of the primary image's blob key,
maintained by the service — so `ProductSummaryDto` / `CartItemDto` keep their cheap single-column
read and the list/cart cards don't need to join the gallery.

## Migration `0006_product_images`
Create the table + indexes + FKs, then **backfill**: every existing product with a non-null
`PrimaryImageBlobKey` gets one `ProductImage` row (general, `IsPrimary = 1`, `SortOrder = 0`).
(PLAN.md's "0006_promotions" label for Phase 7 shifts to the next free number.)

## API (admin = Administrator; reads public)
- `POST   /catalog/products/{id}/images` (multipart: file, `variantId?`, `altText?`) — append; first image becomes primary. (Replaces the old singular `POST .../image`.)
- `PUT    /catalog/products/{id}/images/order` (body: ordered `imageIds[]`) — reorder.
- `PUT    /catalog/products/{id}/images/{imageId}` (body: `altText?`, `variantId?`, `isPrimary?`) — edit / set-primary / (re)associate.
- `DELETE /catalog/products/{id}/images/{imageId}` — delete (promotes a new primary if needed).
- Product detail (`GET /catalog/products/{slug}` + admin get-by-id) now returns the full `images[]`.

Invariants enforced in the service inside a transaction (mirrors the Story-1.4 default-address
pattern): exactly one `IsPrimary` per product; `PrimaryImageBlobKey` kept in sync; magic-byte
sniff + 5 MB cap reused from `ImageFormat` (renamed from the old `ProductImage` helper to free the
entity name).

## Frontend
- **Admin** `ImageGalleryManager` (replaces `ImageUploadField`): upload, dnd-reorder, set-primary,
  alt-text, variant dropdown, delete.
- **Storefront**: `ProductDetailPage` gallery/carousel that swaps to the selected variant's images
  (falls back to general images); `ProductCard`/cart keep using the primary.

## Chunks
- **A** data model + migration (+ rename helper → `ImageFormat`).
- **B** backend service/repo/controller/DTOs + integration tests.
- **C** admin gallery manager.
- **D** storefront carousel + variant-driven swap.
- Then a review workflow + verify.
