// ─────────────────────────────────────────────────────────────────────────────
//  ai module — placeholder for Phase 4 (Azure AI Language for sentiment).
//
//  WHEN ACTIVATED, deploys:
//    - Cognitive Services account, kind=TextAnalytics, SKU S0
//    - Managed-identity access from the api Container App
//    - Diagnostic settings → monitoring module
//
//  WHY AZURE AI LANGUAGE AND NOT ANOMALY DETECTOR?
//  -----------------------------------------------
//  Anomaly Detector is being retired (see ADR-0003). For anomaly detection
//  we use a hand-rolled Z-score classifier in ML.NET — no Azure dependency.
//  This module is sentiment-analysis only: review-text → positive/negative/
//  neutral + confidence score, called from Phase 4 review processing.
//
//  Anthropic Claude usage (chatbot, copy generation) does NOT live here —
//  Anthropic is a SaaS API called directly via Anthropic.SDK; no Azure
//  resource. The Anthropic API key lives in keyVault.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 4: Microsoft.CognitiveServices/accounts kind=TextAnalytics,
// custom subdomain for AAD auth, public network access ON (no Private Link
// at this scale).
