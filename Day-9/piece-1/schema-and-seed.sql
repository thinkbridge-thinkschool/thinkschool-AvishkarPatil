IF DB_ID('IsolationDemo') IS NULL
    CREATE DATABASE IsolationDemo;
GO

USE IsolationDemo;
GO

-- ① Create table
DROP TABLE IF EXISTS dbo.Accounts;

CREATE TABLE dbo.Accounts (
    AccountID   INT           NOT NULL,
    HolderName  VARCHAR(50)   NOT NULL,
    Balance     DECIMAL(10,2) NOT NULL,

    CONSTRAINT PK_Accounts PRIMARY KEY CLUSTERED (AccountID)
    -- Clustered PK gives us predictable row-level lock granularity,
    -- which is essential for isolation-level demos.
);

-- ② Seed 10 named rows — small and human-readable so anomaly outputs are obvious
INSERT INTO dbo.Accounts (AccountID, HolderName, Balance)
VALUES
    (1,  'Alice',   5000.00),
    (2,  'Bob',     3200.00),
    (3,  'Charlie',  800.00),
    (4,  'Diana',   7500.00),
    (5,  'Ethan',   1200.00),
    (6,  'Fiona',   4400.00),
    (7,  'George',  9900.00),
    (8,  'Hannah',  2100.00),
    (9,  'Ivan',    6300.00),
    (10, 'Julia',   3750.00);
GO

PRINT 'Rows inserted: ' + CAST(@@ROWCOUNT AS VARCHAR);
PRINT 'Schema + seed ready. Run isolation_levels_read_anomalies.sql next.';
