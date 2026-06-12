# 0001 — Trade data ingestion via custom MT5 EA pushing to our API

## Status
Accepted (2026-06-12)

## Context
The system needs closed-trade data from multiple MetaTrader 5 accounts. Options considered:

- **A. Custom MQL5 EA** installed on each account, pushing closed trades to our backend over HTTPS.
- **B. MetaApi (metaapi.cloud)** — third-party cloud bridge, ~$10+/month per account, requires depositing account credentials with a third party.
- **C. Manual report import** — user exports MT5 reports and uploads them; contradicts the core goal of eliminating manual work.

The user already runs trading EAs on an always-on VPS, so an MT5 terminal is permanently available.

## Decision
Option A: a custom MQL5 data-collector EA on the VPS pushes trade history to the backend API.

## Consequences
- No recurring third-party cost; full control over payload (magic number, comment, swap, commission).
- The EA must be installed/attached per account; ingestion stops if the VPS or terminal is down.
- Backend must expose an authenticated ingestion endpoint and handle idempotent/duplicate submissions.

## Safety requirements (must not disturb trading EAs)
The collector EA runs on its own chart, in its own thread, alongside live trading EAs. It must:
- Be read-only with respect to trading: only `HistoryDealsGet`; never send, modify, or close orders.
- Use `OnTimer` at a 1–5 minute interval, never `OnTick`.
- Fetch deltas only: remember the last successfully pushed deal ticket/time and query from there; full history is sent once at first attach (backfill).
- Use a short `WebRequest` timeout (~5 s); on failure, drop the cycle and wait for the next timer tick — no tight retry loops (idempotency guarantees no data loss).
- Installation requires whitelisting the API URL in MT5's Expert Advisors options.
