import {
  Component, Input, OnChanges, OnDestroy, SimpleChanges, signal,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import {
  NgApexchartsModule, ApexChart, ApexFill, ApexStroke, ApexTooltip,
} from 'ng-apexcharts';

/**
 * ui-stat-card — labelled metric card with a tabular count-up value and an
 * embedded ApexCharts area sparkline.
 *
 * Presentation only. The `value` drives a tasteful rAF count-up tween (~500ms)
 * and its sign drives profit/loss coloring (reserved P/L semantics). The
 * `series` is a tiny numeric array derived by the caller from data it already
 * loaded — this component never fetches anything.
 *
 * Usage:
 *   <ui-stat-card label="Today" [value]="today()" [series]="todaySpark()" />
 */
@Component({
  selector: 'ui-stat-card',
  standalone: true,
  imports: [NgApexchartsModule, DecimalPipe],
  template: `
    <div
      class="relative overflow-hidden rounded-lg bg-surface border border-border p-4 flex flex-col gap-2"
      [style.boxShadow]="'var(--shadow-card), var(--ring-glass)'"
    >
      <span class="text-xs font-medium uppercase tracking-wide text-text-muted">{{ label }}</span>
      <b
        class="text-2xl font-semibold tabular-nums leading-none"
        [class.text-profit]="value >= 0"
        [class.text-loss]="value < 0"
      >{{ display() | number:'1.2-2' }}</b>

      <div class="absolute bottom-0 left-0 right-0 h-12 opacity-90 pointer-events-none">
        <apx-chart
          [series]="[{ name: label, data: series }]"
          [chart]="chart"
          [colors]="[color]"
          [fill]="fill"
          [stroke]="stroke"
          [tooltip]="tooltip"
        ></apx-chart>
      </div>
    </div>
  `,
})
export class UiStatCardComponent implements OnChanges, OnDestroy {
  @Input() label = '';
  @Input() value = 0;
  /** Tiny numeric series for the sparkline (caller-derived, no fetch). */
  @Input() series: number[] = [];

  /** Animated display value for the count-up tween. */
  readonly display = signal(0);
  private raf = 0;

  get color(): string {
    return this.value < 0 ? '#e5484d' : '#30a46c';
  }

  readonly chart: ApexChart = {
    type: 'area',
    height: 48,
    sparkline: { enabled: true },
    background: 'transparent',
    animations: { enabled: true, speed: 400 },
  };

  readonly fill: ApexFill = {
    type: 'gradient',
    gradient: {
      shadeIntensity: 1,
      opacityFrom: 0.35,
      opacityTo: 0,
      stops: [0, 100],
    },
  };

  readonly stroke: ApexStroke = { curve: 'smooth', width: 2 };

  readonly tooltip: ApexTooltip = { enabled: false };

  ngOnChanges(changes: SimpleChanges) {
    if (changes['value']) this.countUp(this.display(), this.value);
  }

  ngOnDestroy() {
    if (this.raf) cancelAnimationFrame(this.raf);
  }

  /** Simple rAF tween over ~500ms — presentation only. */
  private countUp(from: number, to: number) {
    if (this.raf) cancelAnimationFrame(this.raf);
    const duration = 500;
    const start = performance.now();
    const step = (now: number) => {
      const t = Math.min(1, (now - start) / duration);
      // easeOutCubic
      const eased = 1 - Math.pow(1 - t, 3);
      this.display.set(from + (to - from) * eased);
      if (t < 1) this.raf = requestAnimationFrame(step);
      else { this.display.set(to); this.raf = 0; }
    };
    this.raf = requestAnimationFrame(step);
  }
}
