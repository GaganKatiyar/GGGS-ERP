import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import type { BannerKind, MyFeatureBanner } from '@gggs/shared/models';
import { BannerArt } from './banner-art';

const CHIPS: Readonly<Record<BannerKind, { readonly icon: string; readonly label: string }>> = {
  Announcement: { icon: 'pi pi-megaphone', label: 'Announcement' },
  Payroll: { icon: 'pi pi-wallet', label: 'Payroll' },
  Holiday: { icon: 'pi pi-calendar', label: 'Holiday' },
  Deadline: { icon: 'pi pi-clock', label: 'Deadline' },
  WhatsNew: { icon: 'pi pi-sparkles', label: 'New in Nuts and Bolts' },
  Recognition: { icon: 'pi pi-heart', label: 'Recognition' },
};

/**
 * The home dashboard's feature banner. Slides are either a full picture or a picture with text, in
 * any order. It moves by itself once through its slides and rests on the first, holding while the
 * pointer or keyboard focus is on it; steering it by hand stops it for good. It never moves for people
 * who prefer reduced motion. Each bar's fill is the timer, so pausing that animation pauses the slide.
 * All slides share one grid cell, which keeps the banner as tall as its tallest slide, so the page
 * below does not jump.
 */
@Component({
  selector: 'gggs-feature-banner',
  imports: [BannerArt],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="banner" [attr.data-auto]="running() ? 'on' : 'off'" [attr.data-hidden]="pageHidden()" aria-roledescription="carousel" aria-label="Featured for you">
      <div class="panels" [class.panels--moved]="moved()" [attr.aria-live]="running() ? 'off' : 'polite'" (pointerdown)="swipeStart($event)" (pointerup)="swipeEnd($event)" (pointercancel)="swipeFrom = null">
        @for (slide of slides(); track slide.id; let i = $index) {
          <div
            class="slide"
            [id]="baseId + '-slide-' + i"
            [class.slide--red]="redSlides().has(i)"
            [class.slide--full]="isFull(slide)"
            [class.is-active]="i === current()"
            [class.is-leaving]="i === leaving()"
            [style.--x]="i === current() ? direction() : -direction()"
            [attr.role]="many() ? 'tabpanel' : null"
            aria-roledescription="slide"
            [attr.aria-label]="i + 1 + ' of ' + slides().length"
            [attr.inert]="i === current() ? null : ''"
          >
            @if (isFull(slide)) {
              @if (slide.actionLink) {
                <a class="full" [href]="slide.actionLink" [attr.aria-label]="slide.title" [attr.target]="external(slide.actionLink) ? '_blank' : null" [attr.rel]="external(slide.actionLink) ? 'noopener noreferrer' : null" (click)="follow($event, slide.actionLink)">
                  <img class="full__blur" [src]="slide.imageUrl" alt="" />
                  <img class="full__img" [src]="slide.imageUrl" alt="" (error)="photoFailed(slide.imageUrl!)" />
                </a>
              } @else {
                <div class="full">
                  <img class="full__blur" [src]="slide.imageUrl" alt="" />
                  <img class="full__img" [src]="slide.imageUrl" [alt]="slide.title" (error)="photoFailed(slide.imageUrl!)" />
                </div>
              }
            } @else {
            <div class="slide__text">
              <span class="chip" [class.chip--due]="slide.kind === 'Deadline' && slide.endsOn"><i [class]="chipIcon(slide)" aria-hidden="true"></i>{{ chipLabel(slide) }}</span>
              <h2>{{ slide.title }}</h2>
              @if (slide.body) {
                <p>{{ slide.body }}</p>
              }
              @if (slide.actionLabel && slide.actionLink) {
                <div class="slide__actions">
                  <a class="cta" [href]="slide.actionLink" [attr.target]="external(slide.actionLink) ? '_blank' : null" [attr.rel]="external(slide.actionLink) ? 'noopener noreferrer' : null" (click)="follow($event, slide.actionLink)">
                    {{ slide.actionLabel }} <i class="pi pi-arrow-right" aria-hidden="true"></i>
                  </a>
                  @if (slide.secondaryLabel && slide.secondaryLink) {
                    <a class="more" [href]="slide.secondaryLink" [attr.target]="external(slide.secondaryLink) ? '_blank' : null" [attr.rel]="external(slide.secondaryLink) ? 'noopener noreferrer' : null" (click)="follow($event, slide.secondaryLink)">{{ slide.secondaryLabel }}</a>
                  }
                </div>
              }
            </div>
            <div class="slide__art" [class.slide__art--photo]="hasPhoto(slide)">
              @if (hasPhoto(slide)) {
                <img class="slide__photo" [src]="slide.imageUrl" alt="" (error)="photoFailed(slide.imageUrl!)" />
              } @else {
                <gggs-banner-art [kind]="slide.kind" />
              }
            </div>
            }
          </div>
        }
      </div>

      @if (many()) {
        <div class="bar">
          <div class="bars" role="tablist" aria-label="Featured items">
            @for (slide of slides(); track slide.id; let i = $index) {
              <button
                type="button"
                class="tab"
                role="tab"
                [id]="baseId + '-tab-' + i"
                [attr.aria-controls]="baseId + '-slide-' + i"
                [attr.aria-selected]="i === current()"
                [attr.aria-label]="slide.title"
                [title]="slide.title"
                [tabIndex]="i === current() ? 0 : -1"
                (click)="choose(i)"
                (keydown)="onKeydown($event)"
              >
                <span class="tab__rail"><span class="tab__fill" (animationend)="timerEnded()"></span></span>
              </button>
            }
          </div>
          <div class="controls">
            <button type="button" class="nav" aria-label="Previous slide" title="Previous" (click)="step(-1)"><i class="pi pi-chevron-left" aria-hidden="true"></i></button>
            <span class="count">{{ current() + 1 }} of {{ slides().length }}</span>
            <button type="button" class="nav" aria-label="Next slide" title="Next" (click)="step(1)"><i class="pi pi-chevron-right" aria-hidden="true"></i></button>
          </div>
        </div>
      }
    </section>
  `,
  styleUrl: './feature-banner.scss',
})
export class FeatureBanner {
  private readonly router = inject(Router);
  private readonly reducedMotion = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

  readonly slides = input.required<readonly MyFeatureBanner[]>();
  /** In the slide editor: it stays still, and its buttons do not leave the form. */
  readonly preview = input(false);

  protected readonly baseId = `banner-${Math.random().toString(36).slice(2, 8)}`;
  protected readonly current = signal(0);
  protected readonly leaving = signal<number | null>(null);
  protected readonly direction = signal(1);
  protected readonly moved = signal(false);
  protected readonly auto = signal(!this.reducedMotion);
  protected readonly pageHidden = signal(false);
  protected readonly many = computed(() => this.slides().length > 1);
  /** Slides with text take turns between charcoal and red; full pictures have no colour of their own. */
  protected readonly redSlides = computed(() => {
    const red = new Set<number>();
    let withText = 0;
    this.slides().forEach((slide, index) => {
      if (!this.isFull(slide) && withText++ % 2 === 1) {
        red.add(index);
      }
    });
    return red;
  });
  protected readonly running = computed(() => this.auto() && this.many() && !this.preview());
  protected swipeFrom: { x: number; y: number } | null = null;
  /** Pictures that would not load: those slides fall back to their drawn picture. */
  private readonly failedPhotos = signal<ReadonlySet<string>>(new Set());

  constructor() {
    // The list can shrink under it (an unpublished slide, or the editor's preview).
    effect(() => {
      if (this.current() >= this.slides().length) {
        this.current.set(0);
        this.leaving.set(null);
      }
    });

    const onVisibility = () => this.pageHidden.set(document.hidden);
    document.addEventListener('visibilitychange', onVisibility);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('visibilitychange', onVisibility));
  }

  /** A full picture whose picture will not load falls back to its description on the slide's colour. */
  protected isFull(slide: MyFeatureBanner): boolean {
    return slide.layout === 'FullPicture' && this.hasPhoto(slide);
  }

  protected hasPhoto(slide: MyFeatureBanner): boolean {
    return Boolean(slide.imageUrl) && !this.failedPhotos().has(slide.imageUrl!);
  }

  protected photoFailed(url: string): void {
    this.failedPhotos.update((failed) => new Set(failed).add(url));
  }

  protected chipIcon(slide: MyFeatureBanner): string {
    return CHIPS[slide.kind].icon;
  }

  protected chipLabel(slide: MyFeatureBanner): string {
    if (slide.chip) {
      return slide.chip;
    }

    if (slide.kind === 'Deadline' && slide.endsOn) {
      return `Due ${new Date(`${slide.endsOn}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}`;
    }

    return CHIPS[slide.kind].label;
  }

  protected external(link: string): boolean {
    return link.startsWith('https://');
  }

  /** A page in the app goes through the router; a modified click still opens a new tab as usual. */
  protected follow(event: MouseEvent, link: string): void {
    if (this.preview()) {
      event.preventDefault();
    } else if (!this.external(link) && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
      event.preventDefault();
      void this.router.navigateByUrl(link);
    }
  }

  protected step(delta: 1 | -1): void {
    this.auto.set(false);
    this.advance(delta);
  }

  protected choose(index: number): void {
    this.auto.set(false);
    this.show(index, index > this.current() ? 1 : -1);
  }

  protected timerEnded(): void {
    if (!this.running()) {
      return;
    }

    // One pass, then it rests on the first slide: nothing on a page loops for ever.
    const wasLast = this.current() === this.slides().length - 1;
    this.advance(1);
    if (wasLast) {
      this.auto.set(false);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.slides().length;
    const targets: Readonly<Record<string, [number, 1 | -1]>> = {
      ArrowRight: [(this.current() + 1) % count, 1],
      ArrowLeft: [(this.current() - 1 + count) % count, -1],
      Home: [0, -1],
      End: [count - 1, 1],
    };
    const target = targets[event.key];
    if (!target) {
      return;
    }

    event.preventDefault();
    this.auto.set(false);
    this.show(target[0], target[1]);
    (event.currentTarget as HTMLElement).parentElement?.querySelectorAll<HTMLElement>('[role="tab"]')[target[0]]?.focus();
  }

  /** Touch and pen only: a mouse drag stays free to select text. */
  protected swipeStart(event: PointerEvent): void {
    this.swipeFrom = event.pointerType === 'mouse' ? null : { x: event.clientX, y: event.clientY };
  }

  protected swipeEnd(event: PointerEvent): void {
    if (!this.swipeFrom || !this.many()) {
      return;
    }

    const dx = event.clientX - this.swipeFrom.x;
    const dy = event.clientY - this.swipeFrom.y;
    this.swipeFrom = null;
    if (Math.abs(dx) > 48 && Math.abs(dx) > Math.abs(dy)) {
      this.step(dx < 0 ? 1 : -1);
    }
  }

  private advance(delta: 1 | -1): void {
    const count = this.slides().length;
    this.show((this.current() + delta + count) % count, delta);
  }

  private show(index: number, direction: 1 | -1): void {
    if (index === this.current()) {
      return;
    }

    this.leaving.set(this.current());
    this.direction.set(direction);
    this.moved.set(true);
    this.current.set(index);
  }
}
