# Execution Sidecar

Posts the real MT5 **execution time** (the journal's `order #… done in X ms`) into Profit Hub.

This value is measured by the MetaTrader terminal at order time and only exists in the
**journal log file** — it is not in trade/order history, and MQL5 cannot read the log
(its file access is sandboxed). So a tiny external script reads the log and posts it.
See [ADR 0005](../../docs/adr/0005-journal-execution-time-via-log-sidecar.md).

It is **read-only with respect to trading**: it only reads a log file and makes HTTPS
POSTs. It never touches MetaTrader, orders, or positions.

## Prerequisites
- Windows (the MT5 VPS) — **PowerShell is built in, nothing to install**.
- The collector EA deployed and run once with `ForceBackfill` so Trades carry their
  `ClosingOrderTicket` (the match key). New trades get it automatically.

## Find the values
- **LogDir** — MetaTrader data folder → `logs`. In the terminal: *File → Open Data Folder*,
  then open the `logs` subfolder; copy its path. It looks like
  `C:\Users\<you>\AppData\Roaming\MetaQuotes\Terminal\<HASH>\logs`.
  Daily files are named `YYYYMMDD.log`.
- **IngestKey** — the account's ingest key from the Accounts page (same key the EA uses).
- **ApiUrl** — your backend base URL (e.g. `https://profit-hub-service.onrender.com`).
- **AccountLogin** *(optional but recommended)* — the MT5 account number; restricts parsing
  to that login's lines in case the terminal logged into more than one account.

## Run it
```powershell
powershell -ExecutionPolicy Bypass -File Send-ExecutionTimes.ps1 `
  -ApiUrl "https://profit-hub-service.onrender.com" `
  -IngestKey "<account ingest key>" `
  -LogDir "C:\Users\<you>\AppData\Roaming\MetaQuotes\Terminal\<HASH>\logs" `
  -AccountLogin 27505156
```
By default it follows **today's** log file only (`YYYYMMDD.log`) and rolls to the next
day's file at midnight. It loops every 15s (override with `-IntervalSec`), tracks how far
it has read (offset file in `%TEMP%`), and only advances after a successful POST. Safe to
stop/restart — the backend is idempotent and re-reading a day's log just re-sends the same
values.

### Backfill older trades (one-time)
Trades that closed **before today** won't get execution time from the default mode, because
their `done in X ms` lines live in older log files it doesn't read. To fill them once, add
`-Backfill`: it scans **every `*.log`** in the folder (oldest first), posts them all, then
continues following today's file as usual.
```powershell
powershell -ExecutionPolicy Bypass -File Send-ExecutionTimes.ps1 -Backfill `
  -ApiUrl "..." -IngestKey "..." -LogDir "...\logs" -AccountLogin 27505156
```
Run `-Backfill` **after** the EA redeploy + `ForceBackfill` (so `ClosingOrderTicket` is
populated and lines can match). It's idempotent, so running it more than once is harmless.
For the persistent Task Scheduler entry, use the plain (no `-Backfill`) command.

## Keep it running (Task Scheduler)
Create a task that runs at startup:
1. *Task Scheduler → Create Task*. General: "Run whether user is logged on or not".
2. Triggers: *At startup* (optionally also *At log on*).
3. Actions: *Start a program*
   - Program: `powershell.exe`
   - Arguments: `-ExecutionPolicy Bypass -File "C:\path\to\Send-ExecutionTimes.ps1" -ApiUrl "..." -IngestKey "..." -LogDir "..." -AccountLogin 27505156`
4. Settings: "If the task fails, restart every 1 minute".

One task per terminal/account (each with its own `IngestKey` / `LogDir`).

## How matching works
The script extracts `order #(\d+) … done in ([\d.]+) ms` from each line and POSTs
`{ items: [{ orderTicket, executionMs }] }` to `/api/ingest/executions` (authenticated with
`X-Ingest-Key`). The backend updates the Trade whose `ClosingOrderTicket` equals `orderTicket`
within that account. Opening-order lines are sent too but simply don't match anything, so
they're ignored. Values outside a sane range (≤0 or ≥1,000,000 ms) are dropped.

## Troubleshooting
- `matched 0` every cycle → the EA hasn't populated `ClosingOrderTicket` yet (redeploy +
  `ForceBackfill`), or the wrong `LogDir`/`AccountLogin`.
- 401 → wrong `IngestKey`.
- Nothing sent → confirm today's `YYYYMMDD.log` exists and contains `done in … ms` lines
  (these appear under the **Journal** tab in the terminal).
