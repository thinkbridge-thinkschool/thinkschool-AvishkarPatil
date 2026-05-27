# Avishkar Patil — ThinkBridge ThinkSchool Repository

A structured learning repo built day by day. Each day introduces one or two new concepts on top of the previous day's state. The first five days build a production-grade ASP.NET Core Quotes API incrementally — authentication, authorization, testing, observability, cloud deployment. Days 7–9 shift to SQL Server — indexing, query tuning, concurrency, and deadlock diagnosis.

---

## Structure

```
Day-1/   Tooling, Hello World, first Minimal API
Day-2/   EF Core, domain model, JWT auth, refresh tokens
Day-3/   Authorization policies, integration tests, Testcontainers
Day-4/   Test coverage, Serilog, OpenTelemetry, App Insights, IOptions
Day-5/   N+1 fix, Docker, azd → Azure Container Apps, Polly resilience
Day-7/   SQL CTEs, window functions, set operations
Day-8/   Clustered / non-clustered indexes, covering indexes
Day-9/   Isolation levels, read anomalies, deadlocks
```

> Day 6 was a consolidation / catch-up session with no new committed piece.

---

## Day 1 — Foundation

**Stack:** .NET 10, ASP.NET Core Minimal APIs, Node.js 24 + TypeScript

| Piece | What it covers |
|-------|---------------|
| [piece-1](Day-1/piece-1-tools-check/) | Environment verification — .NET, Node, Git |
| [piece-2](Day-1/piece-2-hello-two-languages/) | Hello World in C# and TypeScript side by side |
| [piece-3](Day-1/piece-3-aspnet-core-10-minimal-api/) | First ASP.NET Core 10 Minimal API — Quotes CRUD |
| [piece-4](Day-1/piece-4-node24-typescript-strict-api/) | Same Quotes API in Node 24 + TypeScript strict mode |
| [piece-5](Day-1/piece-5-refactor-godmethod-controller/) | God-method refactor — extract `OrderService`, add unit + integration tests |
| [piece-7](Day-1/piece-7-quotes-collections-api/) | Quotes + Collections API — first pass at nested resource design |

---

## Day 2 — EF Core, Domain Model, Auth

**Stack:** EF Core, SQLite, JWT (HMAC + Entra ID), BCrypt

| Piece | What it covers |
|-------|---------------|
| [piece-1](Day-2/piece-1/) | Collections aggregate, `FakeClock`, rich domain model |
| [piece-2](Day-2/piece-2/) | Cancellation tokens, EF Core repositories, SQLite migrations |
| [piece-3](Day-2/piece-3/) | Domain exception handling, `ExceptionMiddleware` |
| [piece-4](Day-2/piece-4/) | Repository layer hardening |
| [piece-6](Day-2/piece-6/) | Multi-scheme JWT — internal HMAC + Entra ID policy selector |
| [piece-7](Day-2/piece-7/) | Refresh token rotation — family-based revocation, BCrypt password hashing |

---

## Day 3 — Authorization + Testing

**Stack:** ASP.NET Core Authorization, xUnit, WebApplicationFactory, Testcontainers

| Piece | What it covers |
|-------|---------------|
| [piece-1](Day-3/piece-1/) | Layered JWT — full refresh token flow (rotate-on-use, revoke-family-on-reuse, logout) |
| [piece-2](Day-3/piece-2/) | Authorization policies — `can-edit-quotes` (claim-based) + `can-delete-own-quote` (resource-based `IAuthorizationRequirement`) |
| [piece-5](Day-3/piece-5/) | Unit test expansion — token service, user, collection domain tests |
| [piece-6](Day-3/piece-6/) | Integration tests with `WebApplicationFactory` + in-memory SQLite — 23 tests, all green |
| [piece-7](Day-3/piece-7/) | Real SQL Server in CI via `Testcontainers.MsSql` — one container per run, GUID-named DB per test |

---

## Day 4 — Observability + Configuration

**Stack:** Serilog, OpenTelemetry, Azure App Insights, Azure Key Vault, `IOptions<T>`

| Piece | What it covers |
|-------|---------------|
| [piece-2](Day-4/piece-2/) | Coverage drive — deleted dead methods, added boundary tests; 89% → 96% line coverage |
| [piece-4](Day-4/piece-4/) | Serilog structured logging + per-request `TraceId` correlation middleware |
| [piece-5](Day-4/piece-5/) | OpenTelemetry tracing — ASP.NET Core + EF Core + HttpClient + custom `ActivitySource` → Jaeger |
| [piece-6](Day-4/piece-6/) | Azure App Insights via `UseAzureMonitor`, Key Vault config, latency alert wired to email |
| [piece-7](Day-4/piece-7/) | Typed `IOptions<T>` — `JwtOptions` + `EntraIdOptions` with `ValidateDataAnnotations().ValidateOnStart()` |

---

## Day 5 — Docker, Cloud, Resilience

**Stack:** Docker (no Dockerfile), Azure Container Apps, azd, KQL, Polly

| Piece | What it covers |
|-------|---------------|
| [piece-1](Day-5/piece-1/) | N+1 query diagnosis via OTel/Jaeger traces → fix with `WHERE Id IN (...)` |
| [piece-2](Day-5/piece-2/) | Container image from `dotnet publish` (`PublishContainer`) — no Dockerfile needed |
| [piece-3](Day-5/piece-3/) | Azure Container Registry push + manual Container App deploy |
| [piece-4](Day-5/piece-4/) | `azd up` end-to-end — `azure.yaml` + Bicep provisions ACR, MI, AcrPull role, Container App |
| [piece-5](Day-5/piece-5/) | KQL against App Insights — `requests \| summarize p50, p99 by name`, saved as workspace function |
| [piece-6](Day-5/piece-6/) | HTTP resilience with Polly — retry + circuit breaker on Entra ID metadata client |
| [piece-7](Day-5/piece-7/) | Week 1 smoke test + retrospective |

---

## Day 7 — SQL: CTEs, Window Functions, Set Operations

**Database:** `QuoteDB` (SQL Server) — `dbo.Authors` + `dbo.Quotes`

| Piece | What it covers |
|-------|---------------|
| [piece-1](Day-7/piece-1/) | CTEs — `QuoteStats` (count per author) + `RankedQuotes` (`ROW_NUMBER OVER PARTITION BY`) joined to return top authors with their latest quote |
| [piece-2](Day-7/piece-2/) | Window functions — `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `LAG`, `LEAD` over quote data |
| [piece-3](Day-7/piece-3/) | Set operations — `UNION`, `INTERSECT`, `EXCEPT` across author and quote sets |

Each piece: `schema-and-seed.sql` + query `.sql` + `result.csv` + `output.png` + `README.md`

---

## Day 8 — SQL: Indexes

**Database:** `IndexDemo` (SQL Server) — `dbo.Orders`, 100 000 rows

| Piece | What it covers |
|-------|---------------|
| [Piece-1](Day-8/Piece-1/) | Heap baseline → clustered index (`CIX_Orders_OrderID`) → non-clustered seek + key lookup (`IX_Orders_CustomerID`) → covering NCI. Logical reads: 1 147 → 3 |
| [piece-2](Day-8/piece-2/) | Covering index deep-dive — drop plain NCI, recreate with `INCLUDE (OrderDate, TotalAmount)` — key lookup eliminated. 24 reads → 3 |

Each piece: `schema-and-seed.sql` + demo `.sql` + `STATISTICS IO` before/after screenshots + `README.md`

---

## Day 9 — SQL: Isolation Levels + Deadlocks

**Database:** `IsolationDemo` (SQL Server) — `dbo.Accounts`, 10 named rows

### Piece 1 — Isolation Levels + Read Anomalies

Two SSMS sessions step through three anomalies and the isolation level that prevents each.

| Anomaly | Caused by | Prevented by |
|---------|-----------|-------------|
| Dirty Read | `READ UNCOMMITTED` | `READ COMMITTED` |
| Non-Repeatable Read | `READ COMMITTED` | `REPEATABLE READ` |
| Phantom Read | `REPEATABLE READ` | `SERIALIZABLE` |

Files: [`schema-and-seed.sql`](Day-9/piece-1/schema-and-seed.sql) · [`Session A.sql`](Day-9/piece-1/Session%20A.sql) · [`Session B.sql`](Day-9/piece-1/Session%20B.sql) · [`README`](Day-9/piece-1/README.md)

### Piece 2 — Deadlock Reproduction, Diagnosis, Fix

Two SSMS sessions force a classic two-resource deadlock via inconsistent lock ordering, capture the deadlock graph from `system_health` XE, then fix it by aligning the acquisition order.

| Stage | What happens |
|-------|-------------|
| Reproduction | Session A locks Alice → Bob; Session B locks Bob → Alice → circular wait → error 1205 |
| Diagnosis | XE `system_health` ring buffer → graphical deadlock viewer in SSMS |
| Fix | Both sessions lock Alice → Bob — back-edge never forms, no cycle, no deadlock |

Files: [`schema-and-seed.sql`](Day-9/piece-2/schema-and-seed.sql) · [`Session A.sql`](Day-9/piece-2/Session%20A.sql) · [`Session B.sql`](Day-9/piece-2/Session%20B.sql) · [`README`](Day-9/piece-2/README.md)

---

## Repo conventions

- **Each piece is self-contained.** A `schema-and-seed.sql` (SQL days) or a standalone project folder (.NET days) is the starting point. Running it requires no state from a previous piece.
- **SQL pieces follow a fixed structure:** `schema-and-seed.sql` → main demo `.sql` → screenshots → `README.md`
- **`Session A.sql` / `Session B.sql`** — used in Day-9 for concurrency demos that require two simultaneous SSMS windows.
- **`.NET pieces** carry the full project forward — each piece folder is a snapshot of the API at that stage, not a diff.
