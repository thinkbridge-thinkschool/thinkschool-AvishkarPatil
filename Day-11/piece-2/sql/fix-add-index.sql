-- ═══════════════════════════════════════════════════════════════════════
-- Day 11 · Piece 1 — The "missing index" fix
--
-- Run this AFTER capturing the baseline execution plan (profiling.sql).
-- It adds the nonclustered index that sys.dm_db_missing_index_details would
-- typically recommend for the FAST endpoint's IN-list lookup pattern.
--
-- The slow endpoint's per-item query already uses PK_Quotes (clustered seek)
-- so this index does not change the slow path directly.  What it DOES change:
--
--   1.  When CollectionItems is filtered/joined by QuoteId (e.g. "which
--       collections contain this quote?"), there's no supporting index today.
--       Every such query is a full Clustered Index Scan of CollectionItems.
--       Adding IX_CollectionItems_QuoteId converts that into a seek.
--
--   2.  The DMV often recommends a covering index on Quotes for the projection
--       in the FAST endpoint (Id, Author, Text, CreatedAt).  Compare the DMV
--       output before and after to confirm.
-- ═══════════════════════════════════════════════════════════════════════

USE QuotesApiPerf;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CollectionItems_QuoteId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CollectionItems_QuoteId
        ON dbo.CollectionItems (QuoteId)
        INCLUDE (AddedAt);
    PRINT 'Index IX_CollectionItems_QuoteId created.';
END
ELSE
BEGIN
    PRINT 'Index IX_CollectionItems_QuoteId already exists.';
END
GO

-- Re-run the index inventory query at the bottom of profiling.sql now to
-- confirm the new index appears alongside PK_CollectionItems.
