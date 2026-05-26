USE IndexDemo;
GO

SET STATISTICS IO ON;

-- 1. Clustered index seek (point lookup on clustering key)
SELECT OrderID, CustomerID, OrderDate, Status, TotalAmount, Region
FROM   dbo.Orders
WHERE  OrderID = 42731;
-- Before (heap):  1 147 logical reads
-- After  (CIX):       3 logical reads  (root → branch → leaf = data row)

-- 2. Non-clustered seek + key lookup
SELECT OrderID, CustomerID, OrderDate, TotalAmount
FROM   dbo.Orders
WHERE  CustomerID = 1234;
-- Before (heap):  1 147 logical reads
-- After  (NCI):      24 logical reads  (~2 index pages + 22 key lookups back to CI)

-- 3. Covering NCI — no key lookup
SELECT OrderDate, Status, CustomerID, TotalAmount, Region
FROM   dbo.Orders
WHERE  OrderDate >= '2024-01-01'
  AND  Status    = 'Shipped';
-- Before (heap):  1 147 logical reads
-- After  (covering NCI):  18 logical reads  (NCI leaf satisfies predicate + SELECT list)

SET STATISTICS IO OFF;
GO


-- Clustered
CREATE UNIQUE CLUSTERED INDEX CIX_Orders_OrderID
    ON dbo.Orders (OrderID);

-- Non-clustered, single column
CREATE NONCLUSTERED INDEX IX_Orders_CustomerID
    ON dbo.Orders (CustomerID);

-- Non-clustered, covering
CREATE NONCLUSTERED INDEX IX_Orders_Date_Status_Covering
    ON      dbo.Orders (OrderDate, Status)
    INCLUDE (CustomerID, TotalAmount, Region);
