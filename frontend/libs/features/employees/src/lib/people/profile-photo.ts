/** The side of the square a photo is stored at: sharp at the largest avatar on a high-density screen. */
export const PHOTO_SIZE = 512;

/** What the picker accepts; the API checks the bytes again. */
export const PHOTO_ACCEPT = 'image/jpeg,image/png,image/webp';

export interface SquareCrop {
  readonly x: number;
  readonly y: number;
  readonly size: number;
}

/** The largest centred square in a picture - a portrait loses its top and bottom, a landscape its sides. */
export function centredSquare(width: number, height: number): SquareCrop {
  const size = Math.min(width, height);
  return { x: Math.round((width - size) / 2), y: Math.round((height - size) / 2), size };
}

/**
 * Crops a chosen picture to a centred square and scales it down to {@link PHOTO_SIZE}, so a 6 MB
 * phone photo goes up as a ~50 KB WebP (JPEG where the browser cannot encode WebP). Decoding through
 * a canvas also drops the camera's EXIF data, location included.
 */
export async function preparePhoto(file: Blob): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  try {
    const crop = centredSquare(bitmap.width, bitmap.height);
    const side = Math.min(PHOTO_SIZE, crop.size);
    const canvas = document.createElement('canvas');
    canvas.width = side;
    canvas.height = side;
    const context = canvas.getContext('2d');
    if (!context) {
      throw new Error('This browser cannot resize pictures.');
    }

    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, crop.x, crop.y, crop.size, crop.size, 0, 0, side, side);

    const webp = await toBlob(canvas, 'image/webp');
    return webp?.type === 'image/webp' ? webp : ((await toBlob(canvas, 'image/jpeg')) ?? Promise.reject(new Error('The picture could not be read.')));
  } finally {
    bitmap.close();
  }
}

function toBlob(canvas: HTMLCanvasElement, type: string): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, 0.88));
}
