import { DestroyRef, Directive, ElementRef, booleanAttribute, inject, input, numberAttribute } from '@angular/core';

/** Fine pointers that hover, for users who have not asked for reduced motion. */
const TILT_QUERY = '(hover: hover) and (pointer: fine) and (prefers-reduced-motion: no-preference)';

/**
 * Tilts an element a few degrees toward the pointer, as if it were a card held under a light.
 *
 * For a handful of hero cards (the home dashboard's headline row), not for lists: every pointer
 * move schedules at most one frame, and nothing runs while the pointer is elsewhere. Touch screens
 * and reduced-motion users never get it. Text tilts with its card rather than separately, so it
 * stays legible.
 */
@Directive({
  selector: '[gggsTilt]',
  host: {
    '(pointermove)': 'onMove($event)',
    '(pointerleave)': 'reset()',
  },
})
export class Tilt {
  /** Whether tilting is on; lets a template switch it with a binding. */
  readonly enabled = input(true, { alias: 'gggsTilt', transform: booleanAttribute });
  /** The largest tilt, in degrees, reached at the element's edges. */
  readonly max = input(3, { alias: 'gggsTiltMax', transform: numberAttribute });

  private readonly element: HTMLElement = inject(ElementRef).nativeElement;
  private readonly allowed = typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(TILT_QUERY).matches;
  private frame = 0;
  private x = 0;
  private y = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => cancelAnimationFrame(this.frame));
  }

  protected onMove(event: PointerEvent): void {
    if (!this.allowed || !this.enabled() || event.pointerType !== 'mouse') {
      return;
    }

    this.x = event.clientX;
    this.y = event.clientY;
    this.frame ||= requestAnimationFrame(() => this.apply());
  }

  protected reset(): void {
    cancelAnimationFrame(this.frame);
    this.frame = 0;
    this.element.style.transition = 'transform var(--gggs-duration-slow) var(--gggs-ease-out)';
    this.element.style.transform = '';
  }

  private apply(): void {
    this.frame = 0;
    const rect = this.element.getBoundingClientRect();
    if (!rect.width || !rect.height) {
      return;
    }

    // -0.5 at one edge to 0.5 at the other; the edge under the pointer dips toward the viewer.
    const across = (this.x - rect.left) / rect.width - 0.5;
    const down = (this.y - rect.top) / rect.height - 0.5;
    const max = this.max();
    this.element.style.transition = 'transform 80ms linear';
    this.element.style.transform = `perspective(56rem) rotateX(${(-down * 2 * max).toFixed(2)}deg) rotateY(${(across * 2 * max).toFixed(2)}deg)`;
  }
}
