import { describe, expect, it } from 'vitest';
import { MAX_ZOOM, MIN_CROP_SIDE, centredSquare, clampView, cropFromView, maxZoomFor, panView, startView, type SquareCrop } from './profile-photo';

const PICTURES: readonly (readonly [string, number, number])[] = [
  ['portrait', 3000, 4000],
  ['landscape', 1920, 1080],
  ['square', 512, 512],
  ['small', 300, 200],
  ['tiny', 64, 48],
  ['odd sized', 1001, 777],
];

function expectInside(crop: SquareCrop, width: number, height: number): void {
  expect(crop.x).toBeGreaterThanOrEqual(0);
  expect(crop.y).toBeGreaterThanOrEqual(0);
  expect(crop.x + crop.size).toBeLessThanOrEqual(width);
  expect(crop.y + crop.size).toBeLessThanOrEqual(height);
}

describe('the zoom range', () => {
  it('reaches the most zoom on a large picture', () => {
    expect(maxZoomFor(3000, 4000)).toBe(MAX_ZOOM);
  });

  it('stops before the crop gets too small to stay sharp', () => {
    expect(maxZoomFor(640, 480)).toBeCloseTo(480 / MIN_CROP_SIDE);
    const view = clampView(640, 480, { cx: 320, cy: 240, zoom: 99 });
    expect(cropFromView(640, 480, view).size).toBe(MIN_CROP_SIDE);
  });

  it('allows no zoom on a picture too small to enlarge', () => {
    expect(maxZoomFor(200, 100)).toBe(1);
    expect(clampView(200, 100, { cx: 100, cy: 50, zoom: 3 }).zoom).toBe(1);
  });

  it('does not go below the largest square that fits', () => {
    expect(clampView(1000, 800, { cx: 500, cy: 400, zoom: 0.2 }).zoom).toBe(1);
  });
});

describe('where a picture opens', () => {
  it.each(PICTURES)('selects the centred square of a %s picture', (_name, width, height) => {
    expect(cropFromView(width, height, startView(width, height))).toEqual(centredSquare(width, height));
  });
});

describe('moving the crop', () => {
  it.each(PICTURES)('never leaves a %s picture, at any zoom or distance', (_name, width, height) => {
    for (const zoom of [1, 1.37, 2, 3.333, MAX_ZOOM]) {
      for (const [dx, dy] of [[-1e6, -1e6], [1e6, 1e6], [-1e6, 1e6], [1e6, -1e6], [13, -7]]) {
        const zoomed = clampView(width, height, { ...startView(width, height), zoom });
        const moved = panView(width, height, zoomed, dx, dy, 272);
        expectInside(cropFromView(width, height, moved), width, height);
      }
    }
  });

  it('follows the finger: dragging the picture left moves the crop right', () => {
    const view = startView(1200, 800);
    const moved = panView(1200, 800, view, -100, 0, 400);
    // The circle is 400 px across and shows 800 picture pixels, so one screen pixel is two picture pixels.
    expect(moved.cx).toBe(view.cx + 200);
    expect(moved.cy).toBe(view.cy);
  });

  it('moves less across the picture the further it is zoomed in', () => {
    const view = clampView(1200, 800, { cx: 600, cy: 400, zoom: 2 });
    expect(panView(1200, 800, view, 0, -100, 400).cy).toBe(400 + 100);
  });

  it('stops at the edge of the picture', () => {
    expect(panView(1200, 800, startView(1200, 800), -5000, 0, 400).cx).toBe(800);
    expect(panView(1200, 800, startView(1200, 800), 5000, 0, 400).cx).toBe(400);
  });
});

describe('the square that gets saved', () => {
  it('is the area shown in the circle after moving and zooming', () => {
    const view = panView(1200, 800, clampView(1200, 800, { cx: 600, cy: 400, zoom: 2 }), -200, 0, 400);
    expect(cropFromView(1200, 800, view)).toEqual({ x: 600, y: 200, size: 400 });
  });

  it('keeps the crop centred when only the zoom changes', () => {
    expect(cropFromView(1000, 1000, clampView(1000, 1000, { cx: 500, cy: 500, zoom: 2 }))).toEqual({ x: 250, y: 250, size: 500 });
  });

  it('pulls the crop back inside when zooming out near an edge', () => {
    const zoomedIn = clampView(1000, 1000, { cx: 900, cy: 900, zoom: 3 });
    const zoomedOut = clampView(1000, 1000, { ...zoomedIn, zoom: 1 });
    expect(cropFromView(1000, 1000, zoomedOut)).toEqual({ x: 0, y: 0, size: 1000 });
  });
});
