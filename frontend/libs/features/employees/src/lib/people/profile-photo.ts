/** The side of the square a photo is stored at: sharp at the largest avatar on a high-density screen. */
export const PHOTO_SIZE = 512;

/** What the picker accepts; the API checks the bytes again. */
export const PHOTO_ACCEPT = 'image/jpeg,image/png,image/webp';

/** The most a picture can be zoomed while cropping. */
export const MAX_ZOOM = 4;

/** Zoom stops before the crop gets smaller than this, so a small picture is not enlarged into a blur. */
export const MIN_CROP_SIDE = 256;

/** A picture's longer side is held to this while cropping, so a very large photo stays smooth to drag and light on memory. */
const MAX_WORKING_SIDE = 4096;

export interface SquareCrop {
  readonly x: number;
  readonly y: number;
  readonly size: number;
}

/** Where the crop sits: its centre in picture pixels, and the zoom (1 is the largest square that fits). */
export interface CropView {
  readonly cx: number;
  readonly cy: number;
  readonly zoom: number;
}

/** The largest centred square in a picture - a portrait loses its top and bottom, a landscape its sides. */
export function centredSquare(width: number, height: number): SquareCrop {
  const size = Math.min(width, height);
  return { x: Math.round((width - size) / 2), y: Math.round((height - size) / 2), size };
}

/** How far this picture can be zoomed: 1 when it is too small to enlarge at all. */
export function maxZoomFor(width: number, height: number): number {
  return Math.max(1, Math.min(MAX_ZOOM, Math.min(width, height) / MIN_CROP_SIDE));
}

/** The view a picture opens on: the centred square. */
export function startView(width: number, height: number): CropView {
  const square = centredSquare(width, height);
  return { cx: square.x + square.size / 2, cy: square.y + square.size / 2, zoom: 1 };
}

/** Brings a view back within its limits: the zoom within range, and the whole crop inside the picture. */
export function clampView(width: number, height: number, view: CropView): CropView {
  const zoom = Math.min(maxZoomFor(width, height), Math.max(1, view.zoom));
  const half = Math.min(width, height) / zoom / 2;
  return {
    zoom,
    cx: Math.min(width - half, Math.max(half, view.cx)),
    cy: Math.min(height - half, Math.max(half, view.cy)),
  };
}

/** Moves the picture by screen pixels, so it follows the finger. `diameter` is the crop circle's size on screen. */
export function panView(width: number, height: number, view: CropView, dx: number, dy: number, diameter: number): CropView {
  const perScreenPixel = Math.min(width, height) / view.zoom / diameter;
  return clampView(width, height, { ...view, cx: view.cx - dx * perScreenPixel, cy: view.cy - dy * perScreenPixel });
}

/** The square a view selects, in whole picture pixels. */
export function cropFromView(width: number, height: number, view: CropView): SquareCrop {
  const size = Math.round(Math.min(width, height) / view.zoom);
  return {
    x: Math.min(width - size, Math.max(0, Math.round(view.cx - size / 2))),
    y: Math.min(height - size, Math.max(0, Math.round(view.cy - size / 2))),
    size,
  };
}

/** Reads a chosen picture the right way up. Rejects when the file is not a picture the browser can read. */
export async function decodePhoto(file: Blob): Promise<ImageBitmap> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  const longer = Math.max(bitmap.width, bitmap.height);
  if (longer <= MAX_WORKING_SIDE) {
    return bitmap;
  }

  try {
    const scale = MAX_WORKING_SIDE / longer;
    return await createImageBitmap(bitmap, {
      resizeWidth: Math.round(bitmap.width * scale),
      resizeHeight: Math.round(bitmap.height * scale),
      resizeQuality: 'high',
    });
  } finally {
    bitmap.close();
  }
}

/**
 * Cuts the chosen square and scales it down to {@link PHOTO_SIZE}, so a 6 MB phone photo goes up as a
 * ~50 KB WebP (JPEG where the browser cannot encode WebP). Going through a canvas also drops the
 * camera's EXIF data, location included.
 */
export async function cropPhoto(picture: ImageBitmap, crop: SquareCrop): Promise<Blob> {
  const side = Math.min(PHOTO_SIZE, crop.size);
  const canvas = document.createElement('canvas');
  canvas.width = side;
  canvas.height = side;
  const context = canvas.getContext('2d');
  if (!context) {
    throw new Error('This browser cannot resize pictures.');
  }

  context.imageSmoothingQuality = 'high';
  context.drawImage(picture, crop.x, crop.y, crop.size, crop.size, 0, 0, side, side);

  const webp = await toBlob(canvas, 'image/webp');
  return webp?.type === 'image/webp' ? webp : ((await toBlob(canvas, 'image/jpeg')) ?? Promise.reject(new Error('The picture could not be read.')));
}

function toBlob(canvas: HTMLCanvasElement, type: string): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, 0.88));
}
