// ─────────────────────────────────────────────────────────────────────────────
//  serviceBus module — placeholder for Phase 8 (event-driven order pipeline).
//
//  WHEN ACTIVATED, deploys:
//    - Service Bus namespace (Standard tier — Basic doesn't support topics
//      or sessions; we need both)
//    - Queues:
//        - order-confirmation        (api → email-confirmation Function)
//        - voucher-redemption        (api → voucher-usage Function)
//        - loyalty-accrual           (api → ledger Function)
//        - dead-letter-monitor-input (system DLQ tap for Phase 9 alerting)
//    - Topics:
//        - product-sentiment         (api → Phase 4 sentiment processor)
//
//  WHY STANDARD AND NOT PREMIUM?
//  -----------------------------
//  Premium is geo-replication, dedicated capacity, and VNet integration —
//  all overkill for portfolio traffic. Standard supports topics, sessions,
//  dead-letter, scheduled messages: every feature the order pipeline needs.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 8: Service Bus namespace Standard, 4 queues, 1 topic with
// subscriptions, AAD-only auth (RBAC roles for the api SP and Functions SP).
