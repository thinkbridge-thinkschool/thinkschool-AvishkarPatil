-- ═══════════════════════════════════════════════════════════════════════
-- Day 11 Piece-2 — Clear SQL Server plan cache + refresh statistics
--
-- Run BETWEEN k6 runs if you applied fix-add-index.sql AFTER the first
-- measurement.  SQL Server may still be using a cached plan from before
-- the index existed; clearing the cache forces re-compilation against the
-- new index.
--
-- Safe in a dev environment.  Do NOT run this in production — it forces
-- every active query to re-compile its plan, which is expensive at scale.
-- ═══════════════════════════════════════════════════════════════════════

USE QuotesApiPerf;
GO

-- Wipe every cached plan for this database (and any others on this instance).
DBCC FREEPROCCACHE;
GO

-- Refresh statistics so the optimizer picks the new index when it recompiles.
UPDATE STATISTICS dbo.CollectionItems;
UPDATE STATISTICS dbo.Quotes;
UPDATE STATISTICS dbo.Collections;
GO

PRINT 'Plan cache cleared, statistics refreshed.';
PRINT 'Next k6 run will compile plans fresh against the current index inventory.';
GO
