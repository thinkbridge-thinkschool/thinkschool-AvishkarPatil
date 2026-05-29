-- ═══════════════════════════════════════════════════════════════════════
-- Day 11 · Piece 1 — Execution plan + STATISTICS capture
-- Database: QuotesApiPerf on .\SQLEXPRESS  (created by EnsureCreated() at API startup)
--
-- HOW TO USE:
--   1. Run the API once so the database and tables exist.
--   2. Open this file in SSMS.
--   3. Query → Include Actual Execution Plan  (Ctrl+M)
--   4. Run each section (F5).  Capture each plan via right-click → Save Execution Plan As.
-- ═══════════════════════════════════════════════════════════════════════

USE QuotesApiPerf;
GO

-- ── Section 1: The per-item query that fires INSIDE the N+1 loop ─────────
-- The slow endpoint emits this query once per CollectionItem (20 times
-- for our seeded data).  Each individual execution is cheap (PK seek),
-- but the LOOP turns 20 fast queries into one slow request.
SET STATISTICS IO   ON;
SET STATISTICS TIME ON;

DECLARE @quoteId INT = (SELECT TOP 1 QuoteId FROM CollectionItems WHERE CollectionId = 1);

-- This mirrors what EF Core emits for:
--    db.Quotes.FirstOrDefaultAsync(q => q.Id == item.QuoteId)
SELECT TOP(1) [q].[Id], [q].[Author], [q].[Text], [q].[CreatedAt], [q].[IsDeleted], [q].[OwnerId]
FROM   [Quotes] AS [q]
WHERE  [q].[Id] = @quoteId;

SET STATISTICS IO   OFF;
SET STATISTICS TIME OFF;
GO

-- Expected from a single execution: Scan count 1, logical reads ≈ 2, elapsed ≈ 0 ms.
-- The plan is a Clustered Index Seek on PK_Quotes.  Cheap once. Expensive twenty times.

-- ── Section 2: The batched query the FAST endpoint uses ──────────────────
-- One round-trip, all 20 Quote rows.  Same plan shape: Clustered Index Seek,
-- but with multi-value predicate (one seek per IN value, batched in a single batch).
SET STATISTICS IO   ON;
SET STATISTICS TIME ON;

DECLARE @ids TABLE (QuoteId INT);
INSERT INTO @ids SELECT QuoteId FROM CollectionItems WHERE CollectionId = 1;

-- Mirrors:
--    db.Quotes.AsNoTracking().Where(q => quoteIds.Contains(q.Id)).Select(...)
SELECT [q].[Id], [q].[Author], [q].[Text], [q].[CreatedAt]
FROM   [Quotes] AS [q]
WHERE  [q].[Id] IN (SELECT QuoteId FROM @ids);

SET STATISTICS IO   OFF;
SET STATISTICS TIME OFF;
GO

-- Expected: Scan count 1, logical reads ≈ 3-5.  ONE SQL execution for all 20 rows.

-- ── Section 3: What is sys.dm_db_missing_index_details recommending? ─────
-- Run this AFTER finishing the k6 load test so the DMV has accumulated stats.
SELECT
    mid.statement                            AS table_name,
    mid.equality_columns,
    mid.inequality_columns,
    mid.included_columns,
    migs.avg_total_user_cost                 AS avg_query_cost_reduction,
    migs.avg_user_impact                     AS avg_impact_pct,
    migs.user_seeks + migs.user_scans        AS total_uses
FROM
    sys.dm_db_missing_index_groups       AS mig
    INNER JOIN sys.dm_db_missing_index_group_stats AS migs
        ON mig.index_group_handle = migs.group_handle
    INNER JOIN sys.dm_db_missing_index_details AS mid
        ON mig.index_handle = mid.index_handle
WHERE
    mid.database_id = DB_ID()
ORDER BY
    migs.avg_total_user_cost * migs.avg_user_impact DESC;
GO

-- ── Section 4: Verify the existing index inventory ───────────────────────
-- Capture this BEFORE and AFTER applying fix-add-index.sql so the diff is visible.
SELECT
    OBJECT_NAME(i.object_id)  AS table_name,
    i.name                    AS index_name,
    i.type_desc,
    STUFF((
        SELECT ', ' + c.name
        FROM   sys.index_columns ic
               JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE  ic.object_id = i.object_id AND ic.index_id = i.index_id
        ORDER BY ic.key_ordinal
        FOR XML PATH('')), 1, 2, '') AS key_columns
FROM   sys.indexes i
WHERE  i.object_id IN (OBJECT_ID('Quotes'), OBJECT_ID('Collections'), OBJECT_ID('CollectionItems'))
   AND i.type > 0
ORDER BY OBJECT_NAME(i.object_id), i.index_id;
GO

-- ── Section 5: Piece-2 OPTIMIZED single-query pattern ────────────────────
-- This is exactly what EF emits for the new /optimized endpoint: one SQL
-- statement that joins Collections → CollectionItems → Quotes, orders by
-- AddedAt, and projects directly to the response shape.  Capture the plan
-- twice — once BEFORE running fix-add-index.sql, once AFTER — so the
-- before/after diff in the join operator is visible.
SET STATISTICS IO   ON;
SET STATISTICS TIME ON;

DECLARE @collectionId INT = 1;

SELECT
    [c].[Id]        AS collectionId,
    [c].[Name]      AS [name],
    [q].[Id]        AS quote_id,
    [q].[Author]    AS author,
    [q].[Text]      AS [text],
    [q].[CreatedAt] AS createdAt,
    [ci].[AddedAt]  AS addedAt
FROM   [Collections]    AS [c]
JOIN   [CollectionItems] AS [ci] ON [ci].[CollectionId] = [c].[Id]
JOIN   [Quotes]          AS [q]  ON [q].[Id]           = [ci].[QuoteId]
WHERE  [c].[Id] = @collectionId
ORDER BY [ci].[AddedAt];

SET STATISTICS IO   OFF;
SET STATISTICS TIME OFF;
GO

-- Expected BEFORE the index fix: a Clustered Index Scan on CollectionItems
-- (or a hash match) somewhere in the join.
-- Expected AFTER the index fix: Index Seek on IX_CollectionItems_QuoteId.
-- Logical reads on CollectionItems should drop accordingly.

