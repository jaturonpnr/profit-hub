# Profit Hub

A trading dashboard that aggregates closed trades from multiple MetaTrader 5 accounts, shows profit/loss by day/week/month, and exports reports — replacing manual calculation in the MT5 app.

## Language

**Trade**:
A closed position on an MT5 account, with open/close price, lots, and resulting profit.
_Avoid_: Deal, order, position (MT5-internal terms with different meanings)

**Gross Profit**:
The price difference of a Trade times its volume, before any costs.

**Net Profit**:
Gross Profit plus commission and swap of the Trade. The primary number shown on the dashboard and in exports. Excludes Balance Operations.
_Avoid_: Profit (ambiguous), P/L (use only for period aggregates)

**Deal Ticket**:
MT5's unique id for a closed deal. Used as the idempotency key — re-sending the same Trade never creates a duplicate.

**Balance Operation**:
A deposit or withdrawal on an account. Stored separately and never counted as profit.
_Avoid_: Balance trade

**EA**:
A trading strategy (Expert Advisor) identified by its MT5 magic number. Trades carry their magic number so profit can be broken down per EA; the User can give each magic number a friendly name.
_Avoid_: Robot, strategy, magic (use "magic number" only for the raw id)

**User**:
A person with a login to the dashboard. The system is multi-user; each User sees only their own Accounts and Trades.

**Ingest Key**:
A per-Account API key. The collector EA on the user's own VPS sends trades with this key, which identifies the Account (and therefore the User) server-side.
_Avoid_: API key (too generic), token

**Account**:
A single MT5 trading account connected to the system, owned by exactly one User. A User can have many Accounts.
