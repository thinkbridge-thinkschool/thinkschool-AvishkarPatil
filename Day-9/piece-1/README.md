# Day 9 · Piece 1 — Isolation Levels + Read Anomalies

SQL Server exposes four isolation levels that trade *concurrency* against *consistency*. This piece reproduces all three classic read anomalies in two live SSMS sessions, then shows which isolation level eliminates each one.

- **Schema + seed:** [schema-and-seed.sql](schema-and-seed.sql) — `IsolationDemo.dbo.Accounts`, 10 named rows, clustered PK on `AccountID`
- **Session A:** [Session A.sql](Session%20A.sql) — the long-running transaction (writer or reader depending on the anomaly)
- **Session B:** [Session B.sql](Session%20B.sql) — the concurrent session that triggers or is blocked by the anomaly

> Run `schema-and-seed.sql` once before starting. Open two separate SSMS query windows. Follow the numbered steps in order across both windows — never run the full file in one shot.

---

## The table

```sql
CREATE TABLE dbo.Accounts (
    AccountID   INT           NOT NULL,
    HolderName  VARCHAR(50)   NOT NULL,
    Balance     DECIMAL(10,2) NOT NULL,
    CONSTRAINT PK_Accounts PRIMARY KEY CLUSTERED (AccountID)
);
```

10 rows — Alice (5 000), Bob (3 200), Charlie (800), Diana (7 500), Ethan (1 200), Fiona (4 400), George (9 900), Hannah (2 100), Ivan (6 300), Julia (3 750).

---

## Anomaly 1 — Dirty Read

A dirty read happens when Session B reads a row that Session A has modified but not yet committed. If Session A rolls back, Session B saw data that never permanently existed.

**Isolation level that causes it:** `READ UNCOMMITTED`  
**Isolation level that prevents it:** `READ COMMITTED` (SQL Server default)

### How it happens

| Step | Session | Action |
|------|---------|--------|
| 1 | A | `BEGIN TRANSACTION` → `UPDATE` Alice's balance to `99999.00` — does NOT commit |
| 2 | B | `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` → `SELECT` Alice's row |
| 3 | A | `ROLLBACK TRANSACTION` — balance reverts to `5000.00` |
| 4 | B | `SELECT` Alice again — sees `5000.00` now |

**Step 2 returns `Alice | 99999.00`** — a value that was never committed and disappeared at Step 3. Session B made a decision based on data that never existed.

### Result — anomaly reproduced

Session A (left): `BEGIN TRANSACTION` + `UPDATE` open, `(1 row affected)` in Messages — transaction still live.  
Session B (right): `READ UNCOMMITTED` SELECT returns `Alice | 99999.00` immediately — no blocking, no waiting.

![Dirty Read — Session B reads Alice's uncommitted balance of 99999.00](Dirty%20Read%20Result.jpg)

### Prevention — READ COMMITTED blocks the read

When Session B switches to `READ COMMITTED`, the SELECT cannot acquire a shared lock while Session A holds an exclusive lock. Session B blocks until Session A commits or rolls back — it can never see the dirty value.

Session A (left): `BEGIN TRANSACTION` + `UPDATE` still open.  
Session B (right): `SET TRANSACTION ISOLATION LEVEL READ COMMITTED` → SELECT is **executing/blocked** — no result grid, spinning status bar.

![Dirty Read Prevention — READ COMMITTED holds Session B at the lock boundary](Dirty%20Read%20Prevention.png)

---

## Anomaly 2 — Non-Repeatable Read

A non-repeatable read happens when Session A reads the same row twice within one transaction and gets different values because Session B committed an UPDATE in between. `READ COMMITTED` releases shared locks immediately after each statement — so the row is unprotected between A's two reads.

**Isolation level that causes it:** `READ COMMITTED`  
**Isolation level that prevents it:** `REPEATABLE READ`

### How it happens

| Step | Session | Action |
|------|---------|--------|
| 1 | A | `SET READ COMMITTED` → `BEGIN TRANSACTION` → `SELECT` Bob → `3200.00` (first read) |
| 2 | B | `UPDATE` Bob to `100.00` — auto-commits immediately, no blocking |
| 3 | A | `SELECT` Bob again (same transaction) → `100.00` (second read — different value) |

Same query, same transaction, two different values. Any logic that assumed "the balance I just read is still what I'm working with" is now wrong.

### Result — anomaly reproduced

Session A (left): `READ COMMITTED` + `BEGIN TRANSACTION` + first SELECT showing `Bob | 3200.00` in the result grid.  
Session B (right): `UPDATE` returning `(1 row affected)` — committed instantly because Session A's shared lock was already released after its first SELECT.

![Non-Repeatable Read — Session B commits an UPDATE between Session A's two reads](Non-Repeatable%20Read%20Result.png)

### Prevention — REPEATABLE READ holds the shared lock

`REPEATABLE READ` holds shared locks for the **entire duration** of the transaction, not just the statement. Session B's `UPDATE` cannot acquire an exclusive lock while Session A's shared lock is active — it blocks until Session A commits.

Session A (left): `REPEATABLE READ` + first SELECT showing `Bob | 3200.00` — transaction still open.  
Session B (right): `UPDATE` query **executing/blocked** — spinning, waiting for Session A's shared lock to release.

![Non-Repeatable Read Prevention — REPEATABLE READ blocks Session B's UPDATE](Non-Repeatable%20Read%20Prevention.png)

---

## Anomaly 3 — Phantom Read

A phantom read happens when Session A runs the same range query twice and gets a different **number of rows** because Session B inserted a new row that matches the predicate in between. `REPEATABLE READ` holds locks on rows that exist, but does not lock the *gaps* between index keys — new rows can insert into those gaps.

**Isolation level that causes it:** `REPEATABLE READ`  
**Isolation level that prevents it:** `SERIALIZABLE`

### How it happens

| Step | Session | Action |
|------|---------|--------|
| 1 | A | `REPEATABLE READ` → `BEGIN TRANSACTION` → `SELECT WHERE Balance > 5000` → **3 rows** (Diana, George, Ivan) |
| 2 | B | `INSERT` Kevin (`AccountID = 11`, `Balance = 8800`) — no blocking, auto-commits |
| 3 | A | Same `SELECT WHERE Balance > 5000` again → **4 rows** — Kevin appeared |

The row count changed inside a single open transaction. Any logic that loops over the first result set, or allocates resources based on the count, is now inconsistent.

### Result — anomaly reproduced

Session A (left): `REPEATABLE READ` + `BEGIN TRANSACTION` + first SELECT showing **3 rows** — Diana (7500), George (9900), Ivan (6300).  
Session B (right): `INSERT INTO dbo.Accounts VALUES (11, 'Kevin', 8800.00)` — `(1 row affected)`, returned immediately with no blocking.

![Phantom Read — Session B inserts Kevin between Session A's two range queries](Phantom%20Read%20Result.png)

### Prevention — SERIALIZABLE acquires key-range locks

`SERIALIZABLE` acquires **KEY RANGE locks** that cover the gaps in the index range matching the predicate — not just the rows that exist. Any `INSERT` whose value would fall inside that predicate range is blocked until Session A's transaction ends.

Session A (left): `SERIALIZABLE` + `BEGIN TRANSACTION` + first SELECT showing **3 rows** — Diana, George, Ivan.  
Session B (right): `INSERT` of Kevin **executing/blocked** — the key-range lock held by Session A prevents any new row satisfying `Balance > 5000` from being inserted.

![Phantom Read Prevention — SERIALIZABLE key-range locks block Session B's INSERT](Phantom%20Read%20Prevention.png)

---

## Summary

| Anomaly | Root cause | Prevented by |
|---|---|---|
| Dirty Read | `READ UNCOMMITTED` bypasses shared-lock acquisition | `READ COMMITTED` — waits for exclusive lock to release |
| Non-Repeatable Read | `READ COMMITTED` releases shared locks after each statement | `REPEATABLE READ` — holds shared locks until transaction ends |
| Phantom Read | `REPEATABLE READ` locks rows but not index gaps | `SERIALIZABLE` — acquires key-range locks over the predicate |

Each isolation level also prevents all anomalies from the levels below it — `SERIALIZABLE` eliminates all three. The trade-off: stricter isolation means more blocking and lower throughput under concurrent load.

---

## Run it

```powershell
# Step 0 — seed the database (run once)
sqlcmd -S localhost -i schema-and-seed.sql

# Then open Session A.sql and Session B.sql in two SSMS windows
# and follow the numbered steps in order.
```
