import { Component, Input } from '@angular/core';

/** Centered loading spinner for in-page data fetches. */
@Component({
  selector: 'ui-spinner',
  standalone: true,
  template: `
    <div class="flex items-center justify-center gap-3 py-16 text-text-muted" role="status" aria-label="Loading">
      <span class="h-7 w-7 rounded-full border-2 border-brand-500/25 border-t-brand-500 animate-spin"></span>
      @if (label) { <span class="text-sm">{{ label }}</span> }
    </div>
  `,
})
export class UiSpinnerComponent {
  @Input() label = '';
}
