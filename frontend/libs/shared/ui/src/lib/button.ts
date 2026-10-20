import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md';

/**
 * The button every screen should use.
 *
 * Four pages independently reinvented `.btn`/`.btn--primary`/`.btn--danger` with slightly different
 * padding and font-size before this existed (ADR-10 says PrimeNG goes through `shared/ui` wrappers,
 * not page-local CSS). This wraps `pButton` so severity/size/loading state is consistent everywhere,
 * and a future look change happens here instead of N times.
 */
@Component({
  selector: 'gggs-button',
  imports: [ButtonModule, RouterLink, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The content is captured once and stamped into whichever element renders: an <ng-content> in
  // both branches would only ever fill one of them, leaving link buttons without a label.
  template: `
    <ng-template #content><ng-content /></ng-template>
    @if (link(); as link) {
      <a
        pButton
        [routerLink]="link"
        [icon]="icon()"
        [severity]="severity()"
        [text]="variant() === 'ghost'"
        [size]="size() === 'sm' ? 'small' : undefined"
        [style.width]="fullWidth() ? '100%' : null"
      >
        <ng-container *ngTemplateOutlet="content" />
      </a>
    } @else {
      <button
        pButton
        [type]="type()"
        [disabled]="disabled() || loading()"
        [loading]="loading()"
        [icon]="icon()"
        [severity]="severity()"
        [text]="variant() === 'ghost'"
        [size]="size() === 'sm' ? 'small' : undefined"
        [style.width]="fullWidth() ? '100%' : null"
      >
        <ng-container *ngTemplateOutlet="content" />
      </button>
    }
  `,
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<'button' | 'submit'>('button');
  readonly icon = input<string>('');
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly fullWidth = input(false);
  /** Renders as a router link styled like a button instead of a `<button>` (e.g. "Hire employee"). */
  readonly link = input<string | readonly unknown[] | undefined>(undefined);

  protected readonly severity = computed(() => {
    switch (this.variant()) {
      case 'secondary':
        return 'secondary';
      case 'danger':
        return 'danger';
      default:
        return undefined;
    }
  });
}
