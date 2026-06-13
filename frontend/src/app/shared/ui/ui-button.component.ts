import { Component, Input, booleanAttribute } from '@angular/core';

export type UiButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type UiButtonSize = 'sm' | 'md';

/**
 * ui-button — token-driven button.
 * Variants: primary (violet brand), secondary (surface), ghost, danger.
 * NOTE: danger uses --loss red intentionally for destructive actions; this is
 * the one sanctioned non-P/L use of red and is visually distinct (filled CTA).
 *
 * Usage: <button uiButton variant="primary">Save</button>
 * Applied as an attribute directive-style component so host <button>/<a>
 * semantics, type, click handlers, routerLink etc. are preserved by the caller.
 */
@Component({
  selector: 'button[uiButton], a[uiButton]',
  standalone: true,
  template: `<ng-content />`,
  host: {
    '[class]': 'classes',
    '[attr.data-variant]': 'variant',
  },
})
export class UiButtonComponent {
  @Input() variant: UiButtonVariant = 'primary';
  @Input() size: UiButtonSize = 'md';
  @Input({ transform: booleanAttribute }) block = false;

  private readonly base =
    'inline-flex items-center justify-center gap-2 rounded-md font-medium ' +
    'transition-colors duration-150 select-none whitespace-nowrap ' +
    'focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-500 focus-visible:ring-offset-2 focus-visible:ring-offset-bg ' +
    'disabled:opacity-50 disabled:pointer-events-none cursor-pointer';

  private readonly sizes: Record<UiButtonSize, string> = {
    sm: 'h-8 px-3 text-xs',
    md: 'h-9 px-4 text-sm',
  };

  private readonly variants: Record<UiButtonVariant, string> = {
    primary: 'bg-brand-600 text-white hover:bg-brand-500 border border-transparent',
    secondary:
      'bg-surface-raised text-text hover:bg-border border border-border',
    ghost: 'bg-transparent text-text-muted hover:text-text hover:bg-surface-raised border border-transparent',
    danger: 'bg-loss text-white hover:brightness-110 border border-transparent',
  };

  get classes(): string {
    return [
      this.base,
      this.sizes[this.size],
      this.variants[this.variant],
      this.block ? 'w-full' : '',
    ].join(' ');
  }
}
