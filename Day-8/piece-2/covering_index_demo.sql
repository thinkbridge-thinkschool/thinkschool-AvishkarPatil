USE IndexDemo;
GO

-- Prereq: schema-and-seed.sql has been run, leaving these indexes in place:
--   CIX_Orders_OrderID    (clustered)
--   IX_Orders_CustomerID  (non-clustered, single column — what we're about to fix)

------------------------------------------------------------
-- BEFORE: NCI seek + key lookup
------------------------------------------------------------
SET STATISTICS IO ON;
PRINT '--- BEFORE: IX_Orders_CustomerID (no INCLUDE) ---';

SELECT OrderID, CustomerID, OrderDate, TotalAmount
FROM   dbo.Orders
WHERE  CustomerID = 1234;
-- Expected: ~24 logical reads
--   ~2  on the NCI leaf to find the 23 matching keys
--   ~22 on the clustered index, one key lookup per row (one row's lookup hits
--       the same CI page already buffered, so it doesn't add a read)

SET STATISTICS IO OFF;
GO

------------------------------------------------------------
-- THE FIX: drop the plain NCI, recreate it with INCLUDE
------------------------------------------------------------
DROP INDEX IX_Orders_CustomerID ON dbo.Orders;

CREATE NONCLUSTERED INDEX IX_Orders_CustomerID_Covering
    ON      dbo.Orders (CustomerID)
    INCLUDE (OrderDate, TotalAmount);
-- Notes:
--   * OrderID is the clustering key, so it's already in every NCI leaf row
--     as the row locator — no need to INCLUDE it.
--   * CustomerID stays in the key (not INCLUDE) because the seek predicate
--     must land on a key column; INCLUDEd columns are leaf-payload only.
GO

------------------------------------------------------------
-- AFTER: NCI seek, no key lookup
------------------------------------------------------------
SET STATISTICS IO ON;
PRINT '--- AFTER: IX_Orders_CustomerID_Covering (INCLUDE OrderDate, TotalAmount) ---';

SELECT OrderID, CustomerID, OrderDate, TotalAmount
FROM   dbo.Orders
WHERE  CustomerID = 1234;
-- Expected: ~3 logical reads (root + branch + leaf, all served from the covering NCI)

SET STATISTICS IO OFF;
GO
