import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BrandMark } from './brand-mark';

describe('BrandMark', () => {
  it('draws six points and a hub, hidden from screen readers', () => {
    const fixture = TestBed.createComponent(BrandMark);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(element.querySelectorAll('circle')).toHaveLength(7);
    expect(element.querySelectorAll('path')).toHaveLength(6);
  });

  it('alternates its two colours around the ring', () => {
    const fixture = TestBed.createComponent(BrandMark);
    fixture.detectChanges();
    const points = [...(fixture.nativeElement as HTMLElement).querySelectorAll('circle')].slice(0, 6).map((circle) => circle.getAttribute('class'));

    expect(points).toEqual(['accent', 'ink', 'accent', 'ink', 'accent', 'ink']);
  });
});
