import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FormDialog } from './overlays';

/** Listens where PrimeNG does, on the document after the capture phase, so a block in capture stops this too. */
function dialogListenerSees(fire: () => void, type: 'keydown' | 'mousedown'): boolean {
  const heard = vi.fn();
  document.addEventListener(type, heard);
  fire();
  document.removeEventListener(type, heard);
  return heard.mock.calls.length > 0;
}

const pressEscape = () => document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
const pressOtherKey = () => document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));

function mask(): HTMLElement {
  const element = document.createElement('div');
  element.classList.add('p-dialog-mask');
  document.body.append(element);
  return element;
}

describe('FormDialog while saving', () => {
  afterEach(() => {
    document.querySelectorAll('.p-dialog-mask').forEach((element) => element.remove());
  });

  function dialog(busy: boolean) {
    const fixture = TestBed.createComponent(FormDialog);
    fixture.componentRef.setInput('busy', busy);
    TestBed.tick();
    return fixture;
  }

  it('keeps Escape from reaching the dialog', () => {
    dialog(true);
    expect(dialogListenerSees(pressEscape, 'keydown')).toBe(false);
  });

  it('keeps a click outside from reaching the dialog', () => {
    dialog(true);
    const outside = mask();
    expect(dialogListenerSees(() => outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })), 'mousedown')).toBe(false);
  });

  it('leaves other keys and clicks inside the form alone', () => {
    dialog(true);
    const inside = document.createElement('input');
    document.body.append(inside);
    expect(dialogListenerSees(pressOtherKey, 'keydown')).toBe(true);
    expect(dialogListenerSees(() => inside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })), 'mousedown')).toBe(true);
    inside.remove();
  });

  it('lets Escape and the outside click through when not saving', () => {
    dialog(false);
    const outside = mask();
    expect(dialogListenerSees(pressEscape, 'keydown')).toBe(true);
    expect(dialogListenerSees(() => outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })), 'mousedown')).toBe(true);
  });

  it('lets them through again once the save has finished', () => {
    const fixture = dialog(true);
    fixture.componentRef.setInput('busy', false);
    TestBed.tick();
    expect(dialogListenerSees(pressEscape, 'keydown')).toBe(true);
  });

  it('stops listening when the dialog is destroyed mid-save', () => {
    const fixture = dialog(true);
    fixture.destroy();
    expect(dialogListenerSees(pressEscape, 'keydown')).toBe(true);
  });
});
