import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { KpiCard, sparkPaths } from './kpi-card';
import { Tilt } from './tilt';

function render(inputs: Partial<Record<keyof KpiCard, unknown>>): HTMLElement {
  const fixture = TestBed.createComponent(KpiCard);
  fixture.componentRef.setInput('label', 'Headcount');
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('sparkPaths', () => {
  it('needs two points to draw a line', () => {
    expect(sparkPaths([])).toBeNull();
    expect(sparkPaths([5])).toBeNull();
  });

  it('runs edge to edge, highest value at the top of the box', () => {
    const spark = sparkPaths([10, 30, 20]);
    expect(spark?.line).toBe('M0.00 22.00 L50.00 2.00 L100.00 12.00');
    expect(spark?.area).toBe(`${spark?.line} L100 24 L0 24 Z`);
  });

  it('draws a flat series as a flat line instead of dividing by zero', () => {
    expect(sparkPaths([4, 4, 4])?.line).toBe('M0.00 22.00 L50.00 22.00 L100.00 22.00');
  });
});

describe('KpiCard', () => {
  it('shows a dash rather than a blank when there is no value', () => {
    expect(render({}).querySelector('.kpi__value')?.textContent?.trim()).toBe('—');
  });

  it('draws a sparkline and a meter only when given the figures', () => {
    const bare = render({ value: 7 });
    expect(bare.querySelector('.kpi__spark')).toBeNull();
    expect(bare.querySelector('.kpi__meter')).toBeNull();

    const full = render({ value: 7, series: [1, 2, 3], progress: 140 });
    expect(full.querySelector('.kpi__spark')).not.toBeNull();
    expect(full.querySelector('.kpi__meter')?.getAttribute('aria-valuenow')).toBe('100');
  });

  it('says what the trend means, not only which way the arrow points', () => {
    const worse = render({ value: '46.9%', trend: 'up', trendIsGood: false });
    expect(worse.querySelector('.kpi__trend--bad')).not.toBeNull();
    expect(worse.querySelector('.sr-only')?.textContent).toBe('Up, a decline.');

    const better = render({ value: 12, trend: 'down', trendIsGood: false });
    expect(better.querySelector('.kpi__trend--good')).not.toBeNull();
    expect(better.querySelector('.sr-only')?.textContent).toBe('Down, an improvement.');
  });
});

@Component({
  imports: [Tilt],
  template: `<div gggsTilt style="width: 100px; height: 100px"></div>`,
})
class TiltHost {}

describe('Tilt', () => {
  it('stays still where hover tilting is not allowed (touch, reduced motion, no media queries)', () => {
    const fixture = TestBed.createComponent(TiltHost);
    fixture.detectChanges();
    const element = (fixture.nativeElement as HTMLElement).querySelector('div') as HTMLElement;
    element.dispatchEvent(Object.assign(new Event('pointermove'), { pointerType: 'mouse', clientX: 90, clientY: 10 }));
    expect(element.style.transform).toBe('');
  });
});
