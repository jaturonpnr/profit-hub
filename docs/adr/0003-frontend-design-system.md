# 0003 — Frontend design system: Tailwind + pure-Tailwind primitives + ApexCharts

## Status
Accepted (2026-06-13)

## Context
The Angular 18 frontend is functional but visually plain: a flat dark theme in
`styles.scss`, per-component inline `styles: [...]`, bare HTML tables, a
text-only sidebar, and default Chart.js styling. We want an "award-grade"
refined dark fintech look (Linear / Vercel / Stripe) with trading-terminal
density for tables. This is a presentation-only redesign — no API, route,
signal, or data-flow changes.

Key requirements that shape tooling choices:
- A documented **design-token layer** so the look is cohesive and light mode is
  addable later without restructuring (dark only for now).
- A **headless / utility** styling approach so we own the markup density needed
  for terminal-style tables, rather than fighting an opinionated component kit.
- Charts with gradient area fills and sparklines.

## Decision

### Tailwind CSS v3 + design tokens
Adopt **Tailwind CSS v3** (`tailwindcss@^3.4`, `postcss`, `autoprefixer`).
Angular 18's `@angular-devkit/build-angular` runs PostCSS automatically when a
`postcss.config.js` is present. Component templates are inline strings in `.ts`
files, so the Tailwind `content` glob is `./src/**/*.{html,ts}`.

Design tokens live as CSS custom properties in `:root` (brand violet scale,
layered dark surfaces, text, `--profit`/`--loss`/`--amber`, `--radius`,
shadows, glass ring) and are mapped into `theme.extend.colors` so they are
usable both as Tailwind classes (`bg-surface`, `text-brand-300`) and raw vars.
Adding light mode later = add a `[data-theme="light"]` override block; no
component changes.

### Pure-Tailwind primitives (NOT Spartan UI)
The plan called for Spartan UI (shadcn-style Angular primitives) with a
fallback to pure Tailwind if Spartan proved awkward against Angular 18. We took
the **pure-Tailwind path**.

Reason: the current `@spartan-ng/brain` declares peer dependencies of
`@angular/core >=21 <23` **and `tailwindcss >=4.0.0`** (plus `tw-animate-css`,
`luxon`, `clsx`). Our stack is Angular 18 + Tailwind v3. Installing Spartan
would force either an Angular major upgrade or a Tailwind v4 migration — both
out of scope for a presentation-only task and both risk destabilising a working
app. The shadcn aesthetic is fully achievable with plain Tailwind, and owning
the primitives keeps them small, well-typed, and dependency-light.

Primitives created in `src/app/shared/ui/` (standalone, token-driven):
- `ui-button` (variants: primary=violet, secondary=surface, ghost, danger)
- `ui-card` (header/body/footer via content projection)
- `ui-badge` (variants: brand, profit, loss, neutral, amber)
- `ui-table` (styling wrapper: sticky header, zebra hover, tabular-nums, dense)

### ApexCharts for charts
Adopt **ApexCharts** via `ng-apexcharts@1.13.0` (the release that targets
Angular 18; `apexcharts@^4`). Latest `ng-apexcharts` requires Angular >=20, so
we pinned the Angular-18-compatible line. `chart.js` is retained for now because
the dashboard still imports it; it will be removed in the dashboard task.

### Color semantics: violet brand, reserved P/L green/red
The brand accent is **violet/indigo** — used for buttons, active nav, links,
and focus rings. **Green (`#30a46c`) and red (`#e5484d`) are reserved
exclusively for P/L values** (and the one sanctioned destructive-CTA red in the
danger button variant). This prevents "UI green" from being confused with
"profit green." Backward-compatible `.profit`/`.loss`/`.buy`/`.sell`/`.neg`
selectors are kept in `styles.scss` so existing pages retain their red/green
until each is individually redesigned.

### Icons & font
Lucide icons via `lucide-angular`; Inter via `@fontsource-variable/inter` with
`font-variant-numeric: tabular-nums` for numeric columns.

## Alternatives rejected
- **Spartan UI** — incompatible peer deps (Angular >=21, Tailwind v4) with our
  Angular 18 / Tailwind v3 stack; would force out-of-scope upgrades.
- **PrimeNG / Angular Material** — opinionated component kits that fight the
  custom terminal-density tables and bespoke fintech look; heavier and harder
  to token-theme to this exact aesthetic.
- **Keeping Chart.js** — lacks the gradient-area / sparkline polish wanted;
  ApexCharts is a better fit. (Removal deferred to the dashboard task to keep
  the build green now.)

## Consequences
- Tailwind adoption touches all components over the redesign — hard to reverse,
  but tokens keep the change cohesive and centralised.
- We own and maintain the UI primitives; the upside is full control and zero
  third-party UI-kit version risk.
- `ng-apexcharts` is pinned to an Angular-18 line; a future Angular upgrade will
  also bump this dependency.
- Light mode is a future token-override block, not a refactor.
