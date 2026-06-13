import { Component, Input, booleanAttribute } from '@angular/core';

/**
 * ui-table — styling wrapper for native <table> markup. Projects a <table>
 * and applies terminal-style density: sticky header, subtle zebra rows,
 * tabular figures, hover highlight. Pages keep their own <thead>/<tbody>.
 *
 * Usage:
 *   <ui-table dense>
 *     <table>...</table>
 *   </ui-table>
 *
 * Styling targets projected elements via :host ::ng-deep so callers do not
 * need utility classes on every cell.
 */
@Component({
  selector: 'ui-table',
  standalone: true,
  template: `<div class="overflow-x-auto rounded-lg border border-border bg-surface" [attr.data-dense]="dense ? '' : null">
    <ng-content />
  </div>`,
  styles: [`
    :host ::ng-deep table {
      width: 100%;
      border-collapse: collapse;
      font-variant-numeric: tabular-nums;
    }
    :host ::ng-deep thead th {
      position: sticky;
      top: 0;
      z-index: 1;
      background: var(--surface);
      text-align: left;
      font-weight: 500;
      color: var(--text-muted);
      font-size: 11px;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      padding: 0.625rem 0.875rem;
      border-bottom: 1px solid var(--border);
    }
    :host ::ng-deep tbody td {
      padding: 0.625rem 0.875rem;
      border-bottom: 1px solid var(--border-subtle);
      color: var(--text);
    }
    :host ::ng-deep tbody tr:hover td {
      background: var(--surface-raised);
    }
    :host ::ng-deep tbody tr:last-child td {
      border-bottom: none;
    }
    :host([data-dense]) ::ng-deep thead th,
    :host([data-dense]) ::ng-deep tbody td {
      padding: 0.4rem 0.75rem;
    }
  `],
  host: { '[attr.data-dense]': "dense ? '' : null" },
})
export class UiTableComponent {
  @Input({ transform: booleanAttribute }) dense = false;
}
