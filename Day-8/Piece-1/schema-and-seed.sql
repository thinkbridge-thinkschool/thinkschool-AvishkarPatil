IF DB_ID('IndexDemo') IS NULL
    CREATE DATABASE IndexDemo;
GO

USE IndexDemo;
GO

-- ① Create table (heap — no indexes yet)
DROP TABLE IF EXISTS dbo.Orders;

CREATE TABLE dbo.Orders (
    OrderID      INT           NOT NULL,
    CustomerID   INT           NOT NULL,
    OrderDate    DATE          NOT NULL,
    Status       VARCHAR(20)   NOT NULL,
    TotalAmount  DECIMAL(10,2) NOT NULL,
    Region       VARCHAR(20)   NOT NULL
);

-- ② Seed 100 000 rows via a tally-table pattern
WITH n AS (
    SELECT TOP (100000)
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS rn
    FROM       sys.all_columns a
    CROSS JOIN sys.all_columns b
)
INSERT INTO dbo.Orders
    (OrderID, CustomerID, OrderDate, Status, TotalAmount, Region)
SELECT
    rn,
    ABS(CHECKSUM(NEWID()) % 5000) + 1,
    DATEADD(day, -(ABS(CHECKSUM(NEWID()) % 1095)), CAST(GETDATE() AS date)),
    CASE ABS(CHECKSUM(NEWID()) % 4)
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Shipped'
        WHEN 2 THEN 'Delivered'
        ELSE      'Cancelled'
    END,
    CAST(ABS(CHECKSUM(NEWID()) % 99000) + 100 AS DECIMAL(10,2)),
    CASE ABS(CHECKSUM(NEWID()) % 5)
        WHEN 0 THEN 'North'
        WHEN 1 THEN 'South'
        WHEN 2 THEN 'East'
        WHEN 3 THEN 'West'
        ELSE      'Central'
    END
FROM n;

GO
PRINT 'Rows inserted: ' + CAST(@@ROWCOUNT AS VARCHAR);