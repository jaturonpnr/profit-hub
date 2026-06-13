import { Component, Input } from '@angular/core';

/**
 * ui-card — glassy raised surface with optional header / footer slots via
 * content projection. Body is the default slot.
 *
 * Usage:
 *   <ui-card>
 *     <div uiCardHeader>Title</div>
 *     ...body content...
 *     <div uiCardFooter>actions</div>
 *   </ui-card>
 */
@Component({
  selector: 'ui-card',
  standalone: true,
  template: `
    <div
      class="rounded-lg bg-surface border border-border shadow-card overflow-hidden"
      [style.boxShadow]="'var(--shadow-card), var(--ring-glass)'"
    >
      @if (hasHeader) {
        <div class="px-5 pt-4 pb-3 border-b border-border-subtle">
          <ng-content select="[uiCardHeader]" />
        </div>
      }
      <div [class]="bodyClass">
        <ng-content />
      </div>
      @if (hasFooter) {
        <div class="px-5 py-3 border-t border-border-subtle">
          <ng-content select="[uiCardFooter]" />
        </div>
      }
    </div>
  `,
})
export class UiCardComponent {
  /** Toggle header/footer rendering. Default both off; set when projecting. */
  @Input() hasHeader = false;
  @Input() hasFooter = false;
  /** Override body padding when a flush layout (e.g. table) is needed. */
  @Input() padded = true;

  get bodyClass(): string {
    return this.padded ? 'p-5' : '';
  }
}
