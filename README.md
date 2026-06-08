# Retail Order Management System                                                                                                                                                            
                                                                                                                                                                                            
A full-stack e-commerce + order management platform built as a deep-dive portfolio project covering modern backend (ASP.NET 10 LTS), frontend (React + Tailwind + shadcn/ui), cloud (Azure),     
event-driven architecture (Service Bus + Event Grid), and applied ML/AI (recommendation, demand forecasting, fraud scoring, multi-provider LLM customer support).                           
                                                                                                                                                                                            
> **Status:** 🚧 In active development. Phase 0 (project scaffolding) in progress. See [`docs/PLAN.md`](docs/PLAN.md) for the full 11-phase roadmap.                                        



---                                                                                                                                                                                         
            
## Why this exists                                                                                                                                                                          
            
This repo is the technical evidence behind two specific role entries on my résumé — every architectural choice, library, and observability hook maps back to a defensible interview talking 
point. The goal is *depth over breadth*: real Azure deployment, real event-driven flows, real ML pipelines — not just CRUD with buzzwords.
                                                                                                                                                                                        
---             

## Tech stack                                                                                                                                                                               

**Backend**                                                                                                                                                                                 
- ASP.NET Core 10 (LTS), MVC Controllers, Entity Framework Core 10
- SQL Server (Azure SQL Serverless in production)                                                                                                                                           
- ASP.NET Identity + JWT in HTTP-only cookies + refresh-token rotation
- Stripe (test mode) for payments                                                                                                                                                           
- ML.NET for recommendations, demand forecasting, fraud scoring
- Multi-provider LLM (OpenAI + Anthropic) for customer-support chat                                                                                                                         
                                                                                                                                                                                        
**Frontend**
- React 19 + TypeScript 6 + Vite 8 *(see ADR-0010)*
- Tailwind CSS 3.4 + shadcn/ui (Radix-based, hand-written primitives)
- TanStack Query for server state + Zustand 5 for client state
- React Router v7 (data router)
- Playwright for E2E tests
                                                                                                                                                                                        
**Cloud (Azure)**
- Container Apps (backend) behind API Management                                                                                                                                            
- Static Web Apps (frontend)                                                                                                                                                                
- Azure SQL Serverless
- Service Bus + Event Grid + Azure Functions (event-driven order pipeline)                                                                                                                  
- Application Insights + Log Analytics (observability)                                                                                                                                      
- Bicep (IaC), deployed to `australiaeast`
                                                                                                                                                                                        
**Quality gates**
- Coverlet (code coverage), xUnit + Playwright (tests), k6 (load tests), jscpd (duplication)                                                                                                
- `dotnet format --verify-no-changes` + ESLint + Prettier in CI                                                                                                                             
                                                                                                                                                                                        
---                                                                                                                                                                                         
                                                                                                                                                                                        
## Quick start

> **Prereqs:** Docker Desktop (or Engine + Compose v2), .NET 10 SDK, Node 20+, Git.

Three commands to a running stack:

```bash
# 1. Copy the env template (.env lives next to the compose file so no --env-file flag is needed)
cp docker/.env.example docker/.env

# 2. Build images + start api + web + sqlserver + azurite
docker compose -f docker/docker-compose.yml up -d --build

# 3. Apply EF Core migrations against the containerized SQL Server
#    (first-time only; re-run after any new migration)
dotnet ef database update --project src/api/Retail.Api
```

After that, services are reachable at:

| URL                                       | What                                  |
|-------------------------------------------|----------------------------------------|
| <http://localhost:5173>                   | Web UI (Vite dev server, HMR-enabled)  |
| <http://localhost:5124/swagger>           | API + Swagger UI                       |
| <http://localhost:5124/api/health>        | App-envelope heartbeat                 |
| <http://localhost:5124/health/live>       | Liveness probe (no DB)                 |
| <http://localhost:5124/health/ready>      | Readiness probe (includes DB check)    |
| `localhost:1433`                          | SQL Server (sa / `$MSSQL_SA_PASSWORD`) |
| `localhost:10000-10002`                   | Azurite (blob / queue / table)         |

To stop the stack: `docker compose -f docker/docker-compose.yml down`.
To wipe persistent data too: add `-v` (drops SQL data and Azurite blobs).

### Running pieces directly (without containers)

The `web` service in compose is convenient for full-stack demos, but for actual frontend work, run Vite on the host — HMR is faster and you skip the anonymous-volume `node_modules` quirks:

```bash
# Start only the backing infra in containers; skip the web service
docker compose -f docker/docker-compose.yml up -d sqlserver azurite api

# Run Vite on the host
cd src/web && pnpm install && pnpm dev
```

For the API on the host (uses `dotnet user-secrets` for `Jwt:Key`, not the env file):

```bash
docker compose -f docker/docker-compose.yml up -d sqlserver azurite
dotnet run --project src/api/Retail.Api
```                                     
                                                                                                                                                                                        
---                                                                                                                                                                                         
            
## Architecture at a glance                                                                                                                                                                 

```                                                                                                                                                                                         
            ┌─────────────┐
            │   Browser   │
            └──────┬──────┘
                    │                                                                                                                                                                    
        ┌────────▼────────┐
        │ Static Web App  │  (React + Tailwind)                                                                                                                                       
        └────────┬────────┘                                                                                                                                                           
                    │
        ┌────────▼────────┐                                                                                                                                                           
        │ API Management  │  (rate limit, auth, routing)
        └────────┬────────┘                                                                                                                                                           
                    │
        ┌──────────▼──────────┐                                                                                                                                                         
        │   Container Apps    │  (ASP.NET 10 API)
        │   - Identity        │                                                                                                                                                         
        │   - Catalog         │
        │   - Orders          │                                                                                                                                                         
        │   - ML inference    │                                                                                                                                                         
        └──┬───────────────┬──┘
            │               │                                                                                                                                                            
    ┌───────▼──────┐  ┌─────▼──────┐
    │  Azure SQL   │  │ Service Bus│                                                                                                                                                     
    │  Serverless  │  │ + Event    │
    └──────────────┘  │ Grid       │                                                                                                                                                     
                    └─────┬──────┘
                            │                                                                                                                                                            
                    ┌───────▼────────┐                                                                                                                                                   
                    │ Azure Functions│  (async order processing)
                    └────────────────┘                                                                                                                                                   
```                                                                                                                                                                                         

Full architecture, ADRs, and per-phase design notes live in [`docs/`](docs/).                                                                                                               
            
---                                                                                                                                                                                         

## Documentation                                                                                                                                                                            
            
| Document | Purpose |
|---|---|
| [`docs/PLAN.md`](docs/PLAN.md) | 11-phase delivery roadmap (~31–40 weeks) |
| [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md) | Epic / Story / Task breakdown + acceptance criteria |                                                                                    
| [`docs/DATABASE_DESIGN.md`](docs/DATABASE_DESIGN.md) | Schema, indexes, migration strategy |                                                                                              
| [`docs/CODING_STANDARDS.md`](docs/CODING_STANDARDS.md) | Conventions, definition-of-done, review checklist |                                                                              
| [`docs/adr/`](docs/adr/) | Architecture Decision Records (ADRs 0001–0006, 0010) |                                                                                                               
                                                                                                                                                                                        
---                                                                                                                                                                                         
                                                                                                                                                                                        
## Roadmap snapshot

- **Phase 0** — project scaffolding, CI, baseline docs *(current)*                                                                                                                          
- **Phase 1** — Identity + JWT + role model (Customer / Staff / StoreManager / Administrator)
- **Phase 2** — Catalog + inventory                                                                                                                                                         
- **Phase 3** — Cart + checkout + Stripe (test mode)                                                                                                                                        
- **Phase 4** — Order management + admin console                                                                                                                                            
- **Phase 5** — ML recommendations + demand forecasting                                                                                                                                     
- **Phase 6** — Multi-provider LLM customer-support chat
- **Phase 7** — Promotions: vouchers + loyalty                                                                                                                                              
- **Phase 8** — Event-driven order pipeline (Service Bus + Event Grid + Functions)                                                                                                          
- **Phase 9** — Observability + runbooks (App Insights + Log Analytics)                                                                                                                     
- **Phase 10** — Load testing (k6) + performance hardening                                                                                                                                  
- **Phase 11** — Final polish + deploy                                                                                                                                                      
            
See [`docs/PLAN.md`](docs/PLAN.md) for the detailed breakdown.                                                                                                                              
            
---                                                                                                                                                                                         
            
## License

MIT (portfolio / self-learning project — no warranty, not intended for production use).