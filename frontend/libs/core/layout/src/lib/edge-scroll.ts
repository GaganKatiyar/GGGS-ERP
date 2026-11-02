import { DestroyRef, Directive, ElementRef, afterNextRender, inject } from '@angular/core';

/** How close to an end, in pixels, the pointer starts the row moving. */
const EDGE_ZONE = 64;
/** Fastest scroll, in pixels per frame, with the pointer right at the end. */
const MAX_SPEED = 12;

/**
 * A row that scrolls sideways by itself: resting the mouse near either end glides it that way,
 * faster the nearer the end, and a vertical mouse wheel scrolls it sideways. It marks which ends
 * have more beyond them (`edge-scroll--start`, `edge-scroll--end`) so styles can fade them.
 *
 * Mouse only - touch already swipes - and no gliding for people who ask for reduced motion; the
 * wheel still works for them. Nothing runs while the pointer is elsewhere.
 */
@Directive({
  selector: '[gggsEdgeScroll]',
  host: {
    '(pointermove)': 'onPointerMove($event)',
    '(pointerleave)': 'stop()',
    '(wheel)': 'onWheel($event)',
    '(scroll)': 'markEdges()',
  },
})
export class EdgeScroll {
  private readonly element: HTMLElement = inject(ElementRef).nativeElement;
  private readonly glides = typeof window !== 'undefined' && typeof window.matchMedia === 'function' && !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  private speed = 0;
  private frame = 0;

  constructor() {
    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => cancelAnimationFrame(this.frame));

    // The ends change when the window resizes or the row's tabs change (another module).
    afterNextRender(() => {
      this.markEdges();
      const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => this.markEdges());
      resize?.observe(this.element);
      const mutation = typeof MutationObserver === 'undefined' ? null : new MutationObserver(() => this.markEdges());
      mutation?.observe(this.element, { childList: true, subtree: true });
      destroyRef.onDestroy(() => {
        resize?.disconnect();
        mutation?.disconnect();
      });
    });
  }

  protected onPointerMove(event: PointerEvent): void {
    if (!this.glides || event.pointerType !== 'mouse' || !this.overflows()) {
      return;
    }

    const rect = this.element.getBoundingClientRect();
    const fromStart = event.clientX - rect.left;
    const fromEnd = rect.right - event.clientX;
    const speed = fromStart < EDGE_ZONE ? -MAX_SPEED * (1 - fromStart / EDGE_ZONE) : fromEnd < EDGE_ZONE ? MAX_SPEED * (1 - fromEnd / EDGE_ZONE) : 0;
    // At least a pixel a frame: smaller steps can round away, which would read as the end.
    this.speed = speed === 0 ? 0 : Math.sign(speed) * Math.max(1, Math.abs(speed));
    if (this.speed && !this.frame) {
      this.frame = requestAnimationFrame(() => this.glide());
    }
  }

  protected onWheel(event: WheelEvent): void {
    // A vertical wheel moves the row sideways; a trackpad's own sideways swipe is left alone.
    if (this.overflows() && Math.abs(event.deltaY) > Math.abs(event.deltaX)) {
      this.element.scrollLeft += event.deltaY;
      event.preventDefault();
    }
  }

  protected stop(): void {
    this.speed = 0;
    cancelAnimationFrame(this.frame);
    this.frame = 0;
  }

  protected markEdges(): void {
    const { scrollLeft, scrollWidth, clientWidth } = this.element;
    this.element.classList.toggle('edge-scroll--start', scrollLeft > 1);
    this.element.classList.toggle('edge-scroll--end', scrollLeft + clientWidth < scrollWidth - 1);
  }

  private glide(): void {
    const before = this.element.scrollLeft;
    this.element.scrollLeft += this.speed;
    // Stop at either end rather than spinning on.
    if (!this.speed || this.element.scrollLeft === before) {
      this.frame = 0;
      return;
    }
    this.frame = requestAnimationFrame(() => this.glide());
  }

  private overflows(): boolean {
    return this.element.scrollWidth > this.element.clientWidth + 1;
  }
}
