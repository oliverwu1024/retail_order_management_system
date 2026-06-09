// ─────────────────────────────────────────────────────────────────────────────
//  eventGrid module — placeholder for Phase 8 (Stripe webhook fan-out +
//  system events).
//
//  WHEN ACTIVATED, deploys:
//    - Event Grid System Topic for the storage account (blob-created events
//      fan out to the ML retrain Function)
//    - Event Grid Custom Topic `retail-events` for app-level events:
//        - order.placed
//        - order.cancelled
//        - inventory.low-threshold
//    - Event Subscriptions wiring each event type → Functions or Service Bus
//
//  WHY EVENT GRID AND NOT JUST MORE SERVICE BUS QUEUES?
//  ---------------------------------------------------
//  Service Bus is consumer-pull, durable, FIFO-able. Event Grid is push,
//  fire-and-forget, fan-out. Use the right tool: order-confirmation is
//  durable work → SB queue; "an order happened, anyone interested?" is
//  pub-sub → Event Grid. Splitting cleanly here makes the architecture
//  story credible at interview.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 8: Event Grid Custom Topic + System Topic, event subscriptions
// with Service Bus / Function endpoints, dead-letter destination to storage.
