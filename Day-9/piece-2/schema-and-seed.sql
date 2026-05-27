IF DB_ID('IsolationDemo') IS NULL
    CREATE DATABASE IsolationDemo;
GO

USE IsolationDemo;
GO

-- ① Create table if it does not already exist
--    (shared with Piece 1 — safe to run whether the DB is fresh or not)
IF OBJECT_ID('dbo.Accounts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Accounts (
        AccountID   INT           NOT NULL,
        HolderName  VARCHAR(50)   NOT NULL,
        Balance     DECIMAL(10,2) NOT NULL,
        CONSTRAINT PK_Accounts PRIMARY KEY CLUSTERED (AccountID)
    );
    PRINT 'Table created.';
END

-- ② Reset the two rows this demo will lock to known, predictable values.
--    DELETE + INSERT is intentional: if a previous deadlock run left one
--    session as the winner with a committed UPDATE, the balances may be
--    anything.  A clean reset makes every run identical.
DELETE FROM dbo.Accounts WHERE AccountID IN (1, 2);

INSERT INTO dbo.Accounts (AccountID, HolderName, Balance)
VALUES
    (1, 'Alice', 5000.00),
    (2, 'Bob',   3200.00);
GO

PRINT 'Seed ready. Run deadlock_repro_and_fix.sql next.';
