USE IsolationDemo;
GO

-- Prereq: schema-and-seed.sql has been run.
--
-- HOW TO USE THIS FILE
-- ====================
-- Open TWO query windows in SSMS, both connected to IsolationDemo.
-- Follow the steps in order. Each step is labeled with [Session A] or [Session B].
-- Run one step at a time — do NOT run the whole file in one shot.
--
-- Three anomalies are covered:
--   1. Dirty Read          → prevented by READ COMMITTED
--   2. Non-Repeatable Read → prevented by REPEATABLE READ
--   3. Phantom Read        → prevented by SERIALIZABLE


------------------------------------------------------------------------
-- ANOMALY 1: DIRTY READ
-- A session reads data that another session has modified but not yet
-- committed. If the writer rolls back, the reader saw data that never
-- permanently existed in the database.
------------------------------------------------------------------------

-- ── Step 1 [Session A] ───────────────────────────────────────────────
-- Open a transaction and update Alice's balance WITHOUT committing.
-- This puts an exclusive lock on the row, but READ UNCOMMITTED ignores it.
BEGIN TRANSACTION;

UPDATE dbo.Accounts
SET    Balance = 99999.00      -- simulated fraudulent credit
WHERE  AccountID = 1;          -- Alice starts at 5 000.00

-- !! DO NOT COMMIT OR ROLLBACK YET. Switch to Session B.

-- ── Step 2 [Session B] ───────────────────────────────────────────────
-- READ UNCOMMITTED allows this session to bypass shared-lock acquisition,
-- so it reads whatever is currently on the data page — including dirty rows.
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 1;
-- Returns: Alice | 99 999.00   ← DIRTY READ — this value was never committed
--
-- Why this is dangerous: the application may make a business decision
-- (e.g. approve a loan) based on data that is about to disappear.

-- ── Step 3 [Session A] ───────────────────────────────────────────────
-- Now roll back. Alice's balance returns to 5 000.00.
ROLLBACK TRANSACTION;

-- ── Step 4 [Session B] ───────────────────────────────────────────────
-- Run the same SELECT again. The dirty value is gone.
SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 1;
-- Returns: Alice | 5 000.00   ← back to the real committed value

------------------------------------------------------------
-- PREVENTION: READ COMMITTED (SQL Server default)
-- Shared locks are held for the duration of each individual read.
-- Session B must wait for Session A's exclusive lock to release
-- (i.e., for Session A to COMMIT or ROLLBACK) before it can read.
------------------------------------------------------------

-- ── Step 5 [Session A] ───────────────────────────────────────────────
BEGIN TRANSACTION;

UPDATE dbo.Accounts
SET    Balance = 99999.00
WHERE  AccountID = 1;

-- !! DO NOT COMMIT OR ROLLBACK YET. Switch to Session B.

-- ── Step 6 [Session B] ───────────────────────────────────────────────
-- Switch to the default isolation level.
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 1;
-- This SELECT blocks (spins) until Session A commits or rolls back.
-- Once Session A rolls back (Step 7), you will see Alice | 5 000.00.
-- The dirty read is impossible.

-- ── Step 7 [Session A] ───────────────────────────────────────────────
ROLLBACK TRANSACTION;
-- Session B's SELECT now unblocks and returns the committed value.


------------------------------------------------------------------------
-- ANOMALY 2: NON-REPEATABLE READ
-- A session reads the same row twice within one transaction and gets
-- different values because another session committed an UPDATE in between.
------------------------------------------------------------------------

-- ── Step 1 [Session A] ───────────────────────────────────────────────
-- READ COMMITTED releases shared locks immediately after each read,
-- so other sessions can modify the row between two reads in the same
-- transaction — the "non-repeatable" part.
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 2;
-- Returns: Bob | 3 200.00   ← first read inside the transaction

-- !! DO NOT COMMIT YET. Switch to Session B.

-- ── Step 2 [Session B] ───────────────────────────────────────────────
-- Session B is outside any transaction (auto-commit).
-- It updates Bob's balance and immediately commits.
UPDATE dbo.Accounts
SET    Balance = 100.00
WHERE  AccountID = 2;
-- Commit is implicit (auto-commit mode). Switch back to Session A.

-- ── Step 3 [Session A] ───────────────────────────────────────────────
-- Read the same row a second time, still within the same transaction.
SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 2;
-- Returns: Bob | 100.00   ← NON-REPEATABLE READ
-- The value changed between two reads in the same transaction.
-- This breaks assumptions like: "balance was 3 200 when I started;
-- I'll now debit 500 from it" — but the balance is now only 100.

COMMIT TRANSACTION;

-- Reset for next demo
UPDATE dbo.Accounts SET Balance = 3200.00 WHERE AccountID = 2;

------------------------------------------------------------
-- PREVENTION: REPEATABLE READ
-- Shared locks are held until the END of the transaction, not just
-- for the duration of each individual statement. Session B's UPDATE
-- is blocked until Session A commits or rolls back.
------------------------------------------------------------

-- ── Step 4 [Session A] ───────────────────────────────────────────────
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 2;
-- Returns: Bob | 3 200.00   ← first read; shared lock is now HELD on this row

-- !! DO NOT COMMIT YET. Switch to Session B.

-- ── Step 5 [Session B] ───────────────────────────────────────────────
UPDATE dbo.Accounts
SET    Balance = 100.00
WHERE  AccountID = 2;
-- This UPDATE blocks — Session A holds a shared lock on the row,
-- and an exclusive lock cannot be granted until that shared lock is released.
-- Session B sits here waiting. Switch back to Session A.

-- ── Step 6 [Session A] ───────────────────────────────────────────────
-- Read the same row again — guaranteed to see the same value.
SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  AccountID = 2;
-- Returns: Bob | 3 200.00   ← identical to Step 4; non-repeatable read prevented

COMMIT TRANSACTION;
-- Session A commits → shared lock released → Session B's UPDATE now proceeds.

-- Reset for next demo
UPDATE dbo.Accounts SET Balance = 3200.00 WHERE AccountID = 2;


------------------------------------------------------------------------
-- ANOMALY 3: PHANTOM READ
-- A session issues the same range query twice within one transaction and
-- gets a different NUMBER of rows because another session inserted (or
-- deleted) rows that match the predicate in between.
-- REPEATABLE READ prevents changes to existing rows but does NOT lock
-- gaps in the index, so new rows can sneak in — they are "phantoms".
------------------------------------------------------------------------

-- ── Step 1 [Session A] ───────────────────────────────────────────────
-- REPEATABLE READ holds row locks but not gap/range locks.
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  Balance > 5000.00
ORDER BY AccountID;
-- Returns: Alice (5 000? No — 5 000 is NOT > 5 000), Diana, George, Ivan
-- Exact rows: AccountID 4 (7 500), 7 (9 900), 9 (6 300) — 3 rows
-- (Alice is 5 000, which equals the threshold, so she is excluded.)
-- Session A records: "there are 3 high-balance accounts."

-- !! DO NOT COMMIT YET. Switch to Session B.

-- ── Step 2 [Session B] ───────────────────────────────────────────────
-- Session B inserts a brand-new row whose Balance qualifies for the range.
-- REPEATABLE READ only holds locks on rows that existed at Step 1 —
-- there is no lock on the "gap" where AccountID = 11 would live.
INSERT INTO dbo.Accounts (AccountID, HolderName, Balance)
VALUES (11, 'Kevin', 8800.00);
-- Auto-commits immediately. Switch back to Session A.

-- ── Step 3 [Session A] ───────────────────────────────────────────────
-- Repeat the exact same range query.
SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  Balance > 5000.00
ORDER BY AccountID;
-- Returns: 4 rows now — Diana, George, Ivan, AND Kevin   ← PHANTOM READ
-- A new row "appeared" inside the same transaction's range predicate.
-- This breaks logic like: "I counted 3 accounts; I'll loop 3 times" —
-- but the actual count is now 4.

COMMIT TRANSACTION;

-- Cleanup phantom row so the demo is repeatable
DELETE FROM dbo.Accounts WHERE AccountID = 11;

------------------------------------------------------------
-- PREVENTION: SERIALIZABLE
-- SQL Server acquires KEY RANGE locks that cover both existing rows AND
-- the gaps between index keys for the query predicate. Session B's INSERT
-- is blocked because the gap lock is held by Session A.
------------------------------------------------------------

-- ── Step 4 [Session A] ───────────────────────────────────────────────
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  Balance > 5000.00
ORDER BY AccountID;
-- Returns: 3 rows (Diana, George, Ivan)
-- SQL Server now holds KEY RANGE locks covering this predicate's range.
-- No concurrent INSERT that would satisfy Balance > 5 000 can proceed
-- until this transaction ends.

-- !! DO NOT COMMIT YET. Switch to Session B.

-- ── Step 5 [Session B] ───────────────────────────────────────────────
INSERT INTO dbo.Accounts (AccountID, HolderName, Balance)
VALUES (11, 'Kevin', 8800.00);
-- This INSERT blocks — the key-range lock held by Session A covers
-- any potential row with Balance > 5 000.
-- Session B waits here. Switch back to Session A.

-- ── Step 6 [Session A] ───────────────────────────────────────────────
-- Repeat the range query — guaranteed same result set, no phantoms.
SELECT AccountID, HolderName, Balance
FROM   dbo.Accounts
WHERE  Balance > 5000.00
ORDER BY AccountID;
-- Returns: same 3 rows — Diana, George, Ivan.   ← Phantom read prevented.

COMMIT TRANSACTION;
-- Session A commits → key-range lock released → Session B's INSERT proceeds.

-- Cleanup
DELETE FROM dbo.Accounts WHERE AccountID = 11;
GO

------------------------------------------------------------------------
-- SUMMARY
-- ┌──────────────────────┬──────────────────┬──────────────────────────┐
-- │ Anomaly              │ Caused by        │ Prevented by             │
-- ├──────────────────────┼──────────────────┼──────────────────────────┤
-- │ Dirty Read           │ READ UNCOMMITTED │ READ COMMITTED (default) │
-- │ Non-Repeatable Read  │ READ COMMITTED   │ REPEATABLE READ          │
-- │ Phantom Read         │ REPEATABLE READ  │ SERIALIZABLE             │
-- └──────────────────────┴──────────────────┴──────────────────────────┘
--
-- Each higher isolation level prevents ALL anomalies below it too:
-- SERIALIZABLE also prevents dirty reads and non-repeatable reads.
-- The trade-off: stricter isolation → more blocking → lower concurrency.
------------------------------------------------------------------------
