import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, input, linkedSignal, output, signal, viewChild } from '@angular/core';
import { FormDialog } from '@gggs/shared/ui';
import { clampView, cropFromView, maxZoomFor, panView, startView, type CropView, type SquareCrop } from './profile-photo';

/** How far one arrow key press moves the picture, in screen pixels; Shift moves four times as far. */
const KEY_STEP = 8;

/** The space between the circle and the stage's nearer edges, in screen pixels. */
const CIRCLE_MARGIN = 16;

const ZOOM_BUTTON_STEP = 0.25;
const ZOOM_KEY_STEP = 0.1;

/**
 * Lets a person choose which part of a picture becomes their photo: they move and zoom the picture
 * behind a round frame, because photos show as circles across the app. The frame's square is what
 * gets saved. The caller owns the picture and the upload; a null picture closes the window.
 */
@Component({
  selector: 'gggs-photo-crop-dialog',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog
      title="Crop your photo"
      description="Move and zoom until your face sits inside the circle."
      saveLabel="Use photo"
      size="sm"
      [visible]="picture() !== null"
      [busy]="busy()"
      [error]="error()"
      (visibleChange)="$event || cancelled.emit()"
      (save)="use()"
    >
      <div
        #stage
        class="crop-stage"
        [class.crop-stage--dragging]="dragging()"
        tabindex="0"
        role="group"
        aria-label="Photo position"
        aria-describedby="photo-crop-hint"
        (pointerdown)="onPointerDown($event)"
        (pointermove)="onPointerMove($event)"
        (pointerup)="onPointerEnd($event)"
        (pointercancel)="onPointerEnd($event)"
        (wheel)="onWheel($event)"
        (keydown)="onKeydown($event)"
      >
        <canvas #canvas class="crop-stage__canvas" aria-hidden="true"></canvas>
      </div>

      <div class="crop-zoom">
        <button type="button" class="btn btn--icon" aria-label="Zoom out" [disabled]="busy() || view().zoom <= 1" (click)="zoomTo(view().zoom - zoomButtonStep)">
          <i class="pi pi-minus" aria-hidden="true"></i>
        </button>
        <input
          type="range"
          class="crop-zoom__slider"
          aria-label="Zoom"
          min="1"
          step="0.01"
          [max]="maxZoom()"
          [value]="view().zoom"
          [disabled]="busy() || maxZoom() <= 1"
          [attr.aria-valuetext]="zoomLabel()"
          (input)="onSlider($event)"
        />
        <button type="button" class="btn btn--icon" aria-label="Zoom in" [disabled]="busy() || view().zoom >= maxZoom()" (click)="zoomTo(view().zoom + zoomButtonStep)">
          <i class="pi pi-plus" aria-hidden="true"></i>
        </button>
        <span class="crop-zoom__value" aria-hidden="true">{{ zoomLabel() }}</span>
      </div>

      <div class="crop-help">
        <p id="photo-crop-hint" class="crop-help__hint">Drag the picture or use the arrow keys to move it. Use the slider, mouse wheel or a two-finger pinch to zoom.</p>
        <button type="button" class="btn btn--ghost btn--sm" [disabled]="busy()" (click)="reset()">Reset</button>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .crop-stage {
      height: min(19rem, 50vh);
      overflow: hidden;
      border-radius: var(--gggs-radius-lg);
      /* A dark surround in both themes, the way a photo viewer has one. */
      background: var(--gggs-color-nav-bg-deep);
      cursor: grab;
      touch-action: none;
      user-select: none;

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }
    }

    .crop-stage--dragging {
      cursor: grabbing;
    }

    .crop-stage__canvas {
      display: block;
      width: 100%;
      height: 100%;
    }

    .crop-zoom {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto auto;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .crop-zoom__slider {
      width: 100%;
      min-width: 0;
      accent-color: var(--gggs-color-primary);
    }

    .crop-zoom__value {
      min-width: 2.75rem;
      color: var(--gggs-color-text-muted);
      font-variant-numeric: tabular-nums;
      text-align: end;
    }

    .crop-help {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--gggs-space-3);
    }

    .crop-help__hint {
      margin: 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }
  `,
})
export class PhotoCropDialog {
  /** The decoded picture to crop, or null to keep the window closed. */
  readonly picture = input.required<ImageBitmap | null>();
  readonly busy = input(false);
  readonly error = input<string | null>(null);
  /** The person chose this square of the picture. */
  readonly confirmed = output<SquareCrop>();
  readonly cancelled = output<void>();

  protected readonly zoomButtonStep = ZOOM_BUTTON_STEP;
  protected readonly dragging = signal(false);

  /** Starts over on each new picture. */
  protected readonly view = linkedSignal<ImageBitmap | null, CropView>({
    source: this.picture,
    computation: (picture) => (picture ? startView(picture.width, picture.height) : { cx: 0, cy: 0, zoom: 1 }),
  });
  protected readonly maxZoom = computed(() => {
    const picture = this.picture();
    return picture ? maxZoomFor(picture.width, picture.height) : 1;
  });
  protected readonly zoomLabel = computed(() => `${this.view().zoom.toFixed(1)}×`);

  private readonly stage = viewChild.required<ElementRef<HTMLElement>>('stage');
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  /** The stage has no size until the dialog shows it, and changes size with the window. */
  private readonly stageSize = signal({ width: 0, height: 0 });

  private readonly pointers = new Map<number, { x: number; y: number }>();
  private pinch: { startDistance: number; startZoom: number } | null = null;

  constructor() {
    effect((onCleanup) => {
      const stage = this.stage().nativeElement;
      const observer = new ResizeObserver(() => this.stageSize.set({ width: stage.clientWidth, height: stage.clientHeight }));
      observer.observe(stage);
      onCleanup(() => observer.disconnect());
    });

    effect(() => this.draw());
  }

  protected use(): void {
    const picture = this.picture();
    if (picture && !this.busy()) {
      this.confirmed.emit(cropFromView(picture.width, picture.height, this.view()));
    }
  }

  protected reset(): void {
    const picture = this.picture();
    if (picture) {
      this.view.set(startView(picture.width, picture.height));
    }
  }

  protected zoomTo(zoom: number): void {
    const picture = this.picture();
    if (picture) {
      this.view.update((view) => clampView(picture.width, picture.height, { ...view, zoom }));
    }
  }

  protected onSlider(event: Event): void {
    this.zoomTo(Number((event.target as HTMLInputElement).value));
  }

  protected onPointerDown(event: PointerEvent): void {
    if (this.busy()) {
      return;
    }

    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    this.dragging.set(true);
    if (this.pointers.size === 2) {
      this.pinch = { startDistance: this.pointerDistance(), startZoom: this.view().zoom };
    }
  }

  protected onPointerMove(event: PointerEvent): void {
    const last = this.pointers.get(event.pointerId);
    if (!last || this.busy()) {
      return;
    }

    const dx = event.clientX - last.x;
    const dy = event.clientY - last.y;
    this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });

    if (this.pinch) {
      this.zoomTo(this.pinch.startZoom * (this.pointerDistance() / this.pinch.startDistance));
    } else if (this.pointers.size === 1) {
      this.moveBy(dx, dy);
    }
  }

  protected onPointerEnd(event: PointerEvent): void {
    this.pointers.delete(event.pointerId);
    if (this.pointers.size < 2) {
      this.pinch = null;
    }

    if (this.pointers.size === 0) {
      this.dragging.set(false);
    }
  }

  protected onWheel(event: WheelEvent): void {
    event.preventDefault();
    if (!this.busy()) {
      this.zoomTo(this.view().zoom * Math.exp(-event.deltaY * 0.0015));
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (this.busy()) {
      return;
    }

    const step = event.shiftKey ? KEY_STEP * 4 : KEY_STEP;
    const moves: Record<string, readonly [number, number]> = {
      ArrowLeft: [-step, 0],
      ArrowRight: [step, 0],
      ArrowUp: [0, -step],
      ArrowDown: [0, step],
    };
    const move = moves[event.key];
    if (move) {
      this.moveBy(move[0], move[1]);
    } else if (event.key === '+' || event.key === '=') {
      this.zoomTo(this.view().zoom + ZOOM_KEY_STEP);
    } else if (event.key === '-') {
      this.zoomTo(this.view().zoom - ZOOM_KEY_STEP);
    } else {
      return;
    }

    event.preventDefault();
  }

  private moveBy(dx: number, dy: number): void {
    const picture = this.picture();
    const diameter = this.circleDiameter();
    if (picture && diameter > 0) {
      this.view.update((view) => panView(picture.width, picture.height, view, dx, dy, diameter));
    }
  }

  private pointerDistance(): number {
    const [first, second] = [...this.pointers.values()];
    return Math.hypot(first.x - second.x, first.y - second.y);
  }

  private circleDiameter(): number {
    const { width, height } = this.stageSize();
    return Math.min(width, height) - CIRCLE_MARGIN * 2;
  }

  /** Paints from the same decoded picture the saved photo is cut from, so what is seen is what is saved. */
  private draw(): void {
    const picture = this.picture();
    const view = this.view();
    const { width, height } = this.stageSize();
    const canvas = this.canvas().nativeElement;
    const context = canvas.getContext('2d');
    const diameter = this.circleDiameter();
    if (!picture || !context || diameter <= 0) {
      return;
    }

    const density = window.devicePixelRatio || 1;
    canvas.width = Math.round(width * density);
    canvas.height = Math.round(height * density);
    context.setTransform(density, 0, 0, density, 0, 0);

    const scale = diameter / (Math.min(picture.width, picture.height) / view.zoom);
    const centreX = width / 2;
    const centreY = height / 2;
    context.imageSmoothingQuality = 'medium';
    context.drawImage(picture, centreX - view.cx * scale, centreY - view.cy * scale, picture.width * scale, picture.height * scale);

    // A canvas cannot read the theme's tokens; these sit on a picture, so they are the same in both themes.
    context.fillStyle = 'rgb(8 12 16 / 62%)';
    context.beginPath();
    context.rect(0, 0, width, height);
    context.arc(centreX, centreY, diameter / 2, 0, Math.PI * 2);
    context.fill('evenodd');

    context.strokeStyle = '#ffffff';
    context.lineWidth = 2;
    context.beginPath();
    context.arc(centreX, centreY, diameter / 2, 0, Math.PI * 2);
    context.stroke();
  }
}
