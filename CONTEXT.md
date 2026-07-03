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

**Execution Time**:
How long it took to fill the order that closed a Trade, in milliseconds with 3 decimals — the terminal-measured round trip the MT5 journal reports as "order #… done in X ms". The single canonical execution-quality number. It is **not** present in trade/order history, so it is supplied by the Execution Sidecar that parses the journal; the collector EA only records the **Closing Order Ticket** used to match it. Unknown (null) for Trades the sidecar has not matched yet. (An earlier, abandoned approximation used the order's `DONE_MSC − SETUP_MSC` from history — a server-side figure that did not match the journal and is no longer stored.)
_Avoid_: Latency (informal), ping, server fill time (the rejected proxy)

**Closing Order Ticket**:
MT5's id of the order that closed a Trade's position. Recorded so the Execution Sidecar can match a journal "order #…" line to its Trade. Distinct from Deal Ticket (a deal id, the idempotency key).

**Execution Sidecar**:
A standalone process on the user's VPS (PowerShell, no install) that tails the MT5 terminal journal log, extracts each "order #… done in X ms" line, and posts it to the backend to fill a Trade's Execution Time. A third ingestion path alongside the collector EA (live trades) and Backtest file upload — needed because the journal latency exists only in the log, which MQL5 cannot read.
_Avoid_: Log parser (too generic)

**Deal Ticket**:
MT5's unique id for a closed deal. Used as the idempotency key — re-sending the same Trade never creates a duplicate.

**Balance Operation**:
A deposit or withdrawal on an account. Stored separately and never counted as profit.
_Avoid_: Balance trade

**Withdrawal Record**:
A user-entered log of a profit withdrawal from an Account: the amount actually taken out, the withdrawal date, and a snapshot of the **Suggested Amount** (the Account's Net Profit over the chosen period — this-month-to-date by default), that period, and the capital it was based on. A planning and bookkeeping aid — it does **not** change Balance, Net Deposits, or ROI. The real money movement is captured independently as a Balance Operation when the EA ingests it, so counting a Withdrawal Record against the balance too would double-count. Withdrawing more than the Suggested Amount is allowed but flagged as dipping into capital.
_Avoid_: Withdrawal (bare — ambiguous with the MT5 balance-operation withdrawal)

**Suggested Amount**:
The withdrawal a Withdrawal Record proposes by default: the Account's Net Profit over the selected period (the current calendar month to date unless a custom range is chosen). "Withdraw the profit, leave the capital." The user may edit the actual amount before saving.

**Risk Level**:
A named tolerance band for how much drawdown the User is willing to accept, expressed as a percentage of a user-entered capital figure: Very low 15%, Low 20%, Low–medium 30%, Medium 45% and 50%. Multiplying capital by the band's percentage gives the **Risk Budget** — the money the User accepts losing at that level. A pure planning aid: the capital is typed in (not read from an Account) and nothing is compared against real trading data or stored server-side.
_Avoid_: DD% control (implies live enforcement, which this is not)

**Risk Budget**:
Capital × a Risk Level's drawdown percentage — the amount of money the User accepts losing at that Risk Level (e.g. 5,500 × 30% = 1,650). Displayed for every Risk Level at once for comparison, plus for a custom percentage.

**Net Deposits**:
Sum of an account's Balance Operations (deposits positive, withdrawals negative) — the capital put in.

**Balance**:
Net Deposits plus Net Profit — the account's money from closed trades. Excludes floating P/L of open trades (those are not collected), so it mirrors MT5 "Balance", not "Equity".
_Avoid_: Equity

**ROI**:
Net Profit divided by Net Deposits, as a percentage. Undefined (shown as "—") when Net Deposits is zero or negative.

**EA**:
A trading strategy (Expert Advisor) identified by its MT5 magic number. Trades carry their magic number so profit can be broken down per EA; the User can give each magic number a friendly name.
_Avoid_: Robot, strategy, magic (use "magic number" only for the raw id)

**Profit Factor**:
For a set of Trades: total Net Profit of winning Trades divided by the absolute total Net Profit of losing Trades. Shown as "∞" when there are no losing Trades. A live-data analogue of the Backtest's Profit Factor, computed from realized closed Trades.

**Expectancy**:
Average Net Profit per Trade for a set of Trades (total Net Profit ÷ Trade count), expressed in account currency. The expected money won or lost on a typical Trade.
_Avoid_: Expected payoff (reserve that for the Backtest field of the same meaning)

**Realized Drawdown**:
The largest peak-to-trough drop in the cumulative Net Profit of an EA's closed Trades — reported both as an amount (account currency) and as a percentage of the running peak. Built from realized Trades only, so unlike a Backtest's Max Equity Drawdown it never includes floating losses of open positions. Kept as a distinct term to avoid confusion with that backtest-only measure.
_Avoid_: Drawdown (ambiguous), Max Equity Drawdown (backtest-only)

**User**:
A person with a login to the dashboard. The system is multi-user; each User sees only their own Accounts and Trades. There is no public sign-up — Users are created by an Admin.

**Admin**:
A User with elevated rights who can create, list, delete, and reset the password of other Users. The system always keeps at least one Admin. The first Admin is bootstrapped from the `Admin__Email` config at startup.

**Ingest Key**:
A per-Account API key. The collector EA on the user's own VPS sends trades with this key, which identifies the Account (and therefore the User) server-side.
_Avoid_: API key (too generic), token

**Account**:
A single MT5 trading account connected to the system, owned by exactly one User. A User can have many Accounts.

**Backtest**:
The result of one MT5 Strategy Tester run, imported from an exported report file: a single EA on one symbol over a date range, with a specific set of inputs and a starting deposit, producing summary metrics and a series of Backtest Trades. Hypothetical data — owned directly by a User and never mixed into the live Net Profit, Balance, or ROI. Re-running the same EA with different settings produces separate Backtests. Its identity is the **Expert name** from the report (always present); the magic number is captured best-effort and, when it matches a named EA, links the Backtest to that EA — but the link is optional and never required to import.
_Avoid_: Strategy Tester result, simulation

**Backtest Trade**:
One round-trip (entry + exit) within a Backtest, with its own profit. The backtest analogue of a Trade. Reconstructed from MT5's in/out Deals; never called a Deal.
_Avoid_: Deal

**Backtest Return**:
A Backtest's net profit divided by its starting deposit, as a percentage. The fair way to rank Backtests whose starting deposits differ, and the headline comparison metric. The backtest analogue of ROI — kept as a separate term because ROI is reserved for live accounts (Net Profit over Net Deposits).
_Avoid_: ROI (live-only)

**Max Equity Drawdown**:
The largest peak-to-trough drop in equity during a Backtest (MT5 "Equity Drawdown Maximal"), as a percentage. Treated as the real risk measure of a Backtest, because it captures floating losses an EA holds intra-trade — unlike Balance Drawdown, which only moves when trades close and can look deceptively small for grid/martingale EAs.
_Avoid_: Drawdown (ambiguous — always say which)
