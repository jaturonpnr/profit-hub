import { Component, Input } from '@angular/core';

export type UiBadgeVariant = 'brand' | 'profit' | 'loss' | 'neutral' | 'amber';

/**
 * ui-badge — small pill label.
 * Variants: brand (violet), profit (green), loss (red), neutral (gray), amber.
 * profit/loss are the reserved P/L semantics; brand is generic UI emphasis.
 *
 * Usage: <ui-badge variant="profit">+12.40</ui-badge>
 */
@Component({
  selector: 'ui-badge',
  standalone: true,
  template: `<span [class]="classes"><ng-content /></span>`,
})
export class UiBadgeComponent {
  @Input() variant: UiBadgeVariant = 'neutral';

  private readonly base =
    'inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium leading-none whitespace-nowrap border';

  private readonly variants: Record<UiBadgeVariant, string> = {
    brand: 'bg-brand-500/10 text-brand-300 border-brand-500/20',
    profit: 'bg-profit/10 text-profit border-profit/20',
    loss: 'bg-loss/10 text-loss border-loss/20',
    neutral: 'bg-surface-raised text-text-muted border-border',
    amber: 'bg-amber/10 text-amber border-amber/20',
  };

  get classes(): string {
    return `${this.base} ${this.variants[this.variant]}`;
  }
}
