USE IsolationDemo;
GO

-- Prereq: schema-and-seed.sql has been run.
--
-- HOW TO USE THIS FILE
-- ====================
-- Open TWO query windows in SSMS, both connected to IsolationDemo.
--
-- PART 1 (reproduction) and PART 3 (fix) each require BOTH sessions to
-- run AT THE SAME TIME.  Highlight the correct block in each window,
-- then press F5 in Session A, immediately switch to Session B, press F5.
-- The WAITFOR DELAY inside each script handles the rest of the timing.
-- You do NOT need to switch windows mid-execution.
--
-- Three sections are covered:
--   1. Deadlock reproduction   — inconsistent lock order causes a cycle
--   2. Reading the deadlock    — XE system_health + trace flag 1222
--   3. Fix                     — consistent lock order breaks the cycle


------------------------------------------------------------------------
-- PART 1: DEADLOCK REPRODUCTION
--
-- Classic two-resource deadlock:
--   Session A locks Alice first, then tries to lock Bob.
--   Session B locks Bob first, then tries to lock Alice.
--   Each session holds what the other needs → circular wait → deadlock.
--
-- HOW TO RUN:
--   1. Highlight the Session A block below and press F5 in Session A.
--   2. Immediately switch to Session B and press F5 for the Session B block.
--      (within 1 second — WAITFOR handles the rest)
--   3. Watch both windows.  One will receive error 1205 (the victim).
--      The other will complete successfully (the winner).
------------------------------------------------------------------------

-- ── [Session A] — run first, then immediately run Session B ──────────
BEGIN TRANSACTION;

-- Step A-1: Lock Alice.
-- SQL Server grants an exclusive (X) lock on AccountID = 1.
-- No other session can modify or exclusively read this row
-- until Session A's transaction ends.
UPDATE dbo.Accounts
SET    Balance = Balance - 500.00
WHERE  AccountID = 1;
-- X lock on row 1 (Alice) is now HELD by Session A.

-- Step A-2: Pause to let Session B acquire its first lock.
-- Without this delay Session A might race ahead and finish before
-- Session B has a chance to lock row 2, and no deadlock would form.
WAITFOR DELAY '00:00:05';

-- Step A-3: Try to lock Bob.
-- Session B already holds an X lock on row 2 (Bob).
-- Session A blocks here, waiting for Session B to release it.
-- Meanwhile, Session B (after its own delay) will try to lock Alice —
-- which Session A still holds.  That back-edge completes the cycle.
UPDATE dbo.Accounts
SET    Balance = Balance + 500.00
WHERE  AccountID = 2;
-- If Session A is the WINNER: this UPDATE succeeds and we reach COMMIT.
-- If Session A is the VICTIM:  this line never completes; error 1205 fires.

COMMIT TRANSACTION;
-- Winner reaches here; changes are durable.
-- Victim never reaches here; SQL Server rolled back its transaction.
PRINT 'Session A committed.';
GO

-- ── [Session B] — run immediately after starting Session A ───────────
-- Step B-1: Deliberate pause so Session A can acquire its lock on Alice first.
-- This makes the acquisition order deterministic without requiring
-- split-second manual coordination.
WAITFOR DELAY '00:00:02';

BEGIN TRANSACTION;

-- Step B-2: Lock Bob.
-- Session A is currently sleeping (WAITFOR).  No contention yet.
-- SQL Server grants an X lock on AccountID = 2 (Bob) to Session B.
UPDATE dbo.Accounts
SET    Balance = Balance - 200.00
WHERE  AccountID = 2;
-- X lock on row 2 (Bob) is now HELD by Session B.

-- Step B-3: Another pause.
-- Session A's 5-second sleep is about to expire.  When it does, Session A
-- will try to UPDATE Bob (row 2) and block — because Session B holds it.
-- Session B's own pause here ensures that blocking has started before
-- Session B tries to grab Alice.
WAITFOR DELAY '00:00:05';

-- Step B-4: Try to lock Alice.
-- Session A holds an X lock on row 1 (Alice) and is already blocked
-- waiting for Session B to release row 2.
-- Session B is now also blocked waiting for Session A to release row 1.
--
-- Cycle:  A waits for B  (row 2)
--         B waits for A  (row 1)
--
-- SQL Server's lock monitor fires (≈ every 5 seconds), detects the cycle,
-- and rolls back the transaction with the lower undo cost — typically
-- the one that has done less work, usually Session B here.
-- The victim receives:
--   Msg 1205, Level 13, State 51
--   Transaction (Process ID N) was deadlocked on lock resources with
--   another process and has been chosen as the deadlock victim.
--   Rerun the transaction.
UPDATE dbo.Accounts
SET    Balance = Balance + 200.00
WHERE  AccountID = 1;

COMMIT TRANSACTION;
PRINT 'Session B committed.';
GO


------------------------------------------------------------------------
-- PART 2: READING THE DEADLOCK GRAPH
--
-- SQL Server logs every deadlock automatically in the system_health
-- Extended Events session, which ships with every SQL Server instance.
-- No configuration required.
--
-- Run this in ANY session after the deadlock fires.
-- Click the XML value in the result grid to open the graphical deadlock
-- view in SSMS — it shows both processes, the resources they held, and
-- which one was chosen as the victim.
------------------------------------------------------------------------

SELECT
    xdr.value('@timestamp', 'datetime2(0)')  AS deadlock_time,
    xdr.query('.')                            AS deadlock_graph_xml
FROM (
    SELECT CAST(target_data AS XML) AS target_data
    FROM   sys.dm_xe_session_targets AS t
    INNER JOIN sys.dm_xe_sessions    AS s ON s.address = t.event_session_address
    WHERE  s.name        = 'system_health'
      AND  t.target_name = 'ring_buffer'
) AS data
CROSS APPLY
    target_data.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS xdr_table(xdr)
ORDER BY deadlock_time DESC;
-- The most recent deadlock is the first row.
-- The XML contains:
--   <process>  nodes   → spid, isolation level, query text for each session
--   <resource> nodes   → the exact rows/pages that were locked
--   victim=""  attribute → the spid SQL Server chose to kill

-- Alternative: trace flag 1222 (writes full deadlock graph to the error log)
-- Run once to enable, then reproduce the deadlock, then check the error log:
--   DBCC TRACEON(1222, -1);   -- -1 = server-wide
--   -- ... reproduce deadlock ...
--   EXEC xp_readerrorlog 0, 1, N'deadlock';
-- Disable when done: DBCC TRACEOFF(1222, -1);
GO


------------------------------------------------------------------------
-- PART 3: FIX — CONSISTENT LOCK ORDERING
--
-- The deadlock formed because the two sessions acquired locks in
-- opposite orders:
--   Session A: Alice → Bob
--   Session B: Bob  → Alice
--
-- The fix is simple: both sessions must acquire locks in the SAME order.
-- Here both sessions update Alice first, then Bob.
--
-- With consistent ordering, Session B's first UPDATE (Alice) blocks
-- while Session A holds the lock — but this is ordinary blocking, not
-- a cycle.  Session A does not need anything Session B holds.
-- No back-edge → no cycle → no deadlock.
--
-- Execution timeline (fixed):
--   T=0s   Session A: UPDATE Alice (X lock row 1)
--   T=2s   Session B: UPDATE Alice → BLOCKS (Session A holds row 1)
--   T=5s   Session A: UPDATE Bob → succeeds → COMMIT → releases all locks
--   T=5s   Session B: unblocks, acquires row 1 → UPDATE Bob → COMMIT
--
-- HOW TO RUN: same as Part 1 — F5 Session A, immediately F5 Session B.
------------------------------------------------------------------------

-- Reset data first (run in any session before starting Part 3)
DELETE FROM dbo.Accounts WHERE AccountID IN (1, 2);
INSERT INTO dbo.Accounts (AccountID, HolderName, Balance)
VALUES (1, 'Alice', 5000.00), (2, 'Bob', 3200.00);
GO

-- ── [Session A — FIXED] ──────────────────────────────────────────────
BEGIN TRANSACTION;

-- Step A-1: Lock Alice first.
UPDATE dbo.Accounts
SET    Balance = Balance - 500.00
WHERE  AccountID = 1;
-- X lock on row 1 (Alice) acquired.

WAITFOR DELAY '00:00:05';

-- Step A-2: Lock Bob second.
-- Session B is blocked waiting for Alice (row 1).
-- Session A is NOT waiting for anything Session B holds.
-- Simple blocking — no cycle possible.
UPDATE dbo.Accounts
SET    Balance = Balance + 500.00
WHERE  AccountID = 2;
-- X lock on row 2 (Bob) acquired.

COMMIT TRANSACTION;
-- Both locks released.  Session B now unblocks and acquires row 1.
PRINT 'Session A (fixed) committed — no deadlock.';
GO

-- ── [Session B — FIXED] ──────────────────────────────────────────────
WAITFOR DELAY '00:00:02';

BEGIN TRANSACTION;

-- Step B-1: Lock Alice first — SAME ORDER as Session A.
-- Session A already holds an X lock on row 1.
-- Session B blocks here.  This is expected.
-- The key difference: Session B is NOT holding anything that Session A needs,
-- so there is no cycle.  Session B simply waits for Session A to commit.
UPDATE dbo.Accounts
SET    Balance = Balance - 200.00
WHERE  AccountID = 1;
-- Reaches here only after Session A commits (≈ T=5s).

-- Step B-2: Lock Bob second.
-- Session A has already committed and released all its locks.
-- Session B acquires row 2 immediately — no contention.
UPDATE dbo.Accounts
SET    Balance = Balance + 200.00
WHERE  AccountID = 2;

COMMIT TRANSACTION;
PRINT 'Session B (fixed) committed — no deadlock.';
GO


------------------------------------------------------------------------
-- SUMMARY
-- ┌───────────────────────┬────────────────────────────────────────────┐
-- │ Root cause            │ Sessions acquire the same two locks in     │
-- │                       │ opposite order → circular wait             │
-- ├───────────────────────┼────────────────────────────────────────────┤
-- │ SQL Server response   │ Lock monitor detects cycle; picks victim   │
-- │                       │ (less undo work); rolls back victim;       │
-- │                       │ returns error 1205 to victim session       │
-- ├───────────────────────┼────────────────────────────────────────────┤
-- │ Diagnosis             │ XE system_health ring buffer (no setup);   │
-- │                       │ or DBCC TRACEON(1222,-1) → error log       │
-- ├───────────────────────┼────────────────────────────────────────────┤
-- │ Fix                   │ Consistent lock ordering — both sessions   │
-- │                       │ lock resources in the same sequence;       │
-- │                       │ no back-edge → no cycle → no deadlock      │
-- └───────────────────────┴────────────────────────────────────────────┘
------------------------------------------------------------------------
