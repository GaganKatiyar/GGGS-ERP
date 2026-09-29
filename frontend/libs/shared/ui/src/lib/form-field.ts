import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  input,
} from '@angular/core';

/**
 * Label + input + hint/error, standardized.
 *
 * Every form in the app (sign-in, complete-invitation, the employee/admin create-and-edit screens)
 * hand-rolled this same `<label><span>…</span><input/><span class="hint">…</span></label>` shape.
 * This wraps the label/hint/error chrome and projects the actual control, so it works with a plain
 * `<input>`, a `pInputText`, or a `p-select` alike - the field only owns spacing, typography and the
 * error/hint slot, never the control itself.
 *
 * The caller still owns `aria-describedby` on its projected control, because only the caller knows
 * which id it needs (hint's, error's, or both) - pass `hintId`/`errorId` through to keep them stable.
 */
@Component({
  selector: 'gggs-form-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The linter can't see through ng-content to know the projected control is a real form
         element, but wrapping it in the label is exactly what associates the two natively. -->
    <!-- eslint-disable-next-line @angular-eslint/template/label-has-associated-control -->
    <label class="form-field">
      <span class="form-field__label">
        {{ label() }}
        @if (required()) {
          <span aria-hidden="true">*</span>
        }
      </span>

      <ng-content />

      @if (hint() && !error()) {
        <span [id]="hintId()" class="form-field__hint">{{ hint() }}</span>
      }

      @if (error()) {
        <span [id]="errorId()" class="form-field__error" role="alert">
          {{ error() }}
        </span>
      }
    </label>
  `,
  styles: `
    .form-field {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1, 0.25rem);
    }

    .form-field__label {
      font-weight: var(--gggs-font-weight-semibold, 600);
      font-size: var(--gggs-font-size-base, 0.875rem);
      color: var(--gggs-color-text, #1f2430);
    }

    .form-field__hint {
      font-size: var(--gggs-font-size-xs, 0.75rem);
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .form-field__error {
      font-size: var(--gggs-font-size-xs, 0.75rem);
      color: var(--gggs-color-danger, #b3261e);
    }

    /*
     * Not every projected control is a PrimeNG one (a plain <input>/<select>/<textarea> is still
     * common, e.g. sign-in). PrimeNG's own directives (pInputText etc.) already theme themselves and
     * simply pick these rules up too, since the properties below match what Aura would set anyway.
     */
    .form-field ::ng-deep > input,
    .form-field ::ng-deep > select,
    .form-field ::ng-deep > textarea {
      font: inherit;
      color: var(--gggs-color-text, #1f2430);
      background: var(--gggs-color-surface-raised, #ffffff);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-sm, 0.375rem);
      padding: var(--gggs-space-2, 0.5rem) var(--gggs-space-3, 0.75rem);
    }

    .form-field ::ng-deep > input:focus-visible,
    .form-field ::ng-deep > select:focus-visible,
    .form-field ::ng-deep > textarea:focus-visible {
      outline: 2px solid var(--gggs-color-primary, #1d4ed8);
      outline-offset: 1px;
    }
  `,
})
export class FormField {
  readonly label = input.required<string>();
  readonly required = input(false, { transform: booleanAttribute });
  readonly hint = input<string | undefined>(undefined);
  readonly error = input<string | undefined>(undefined);
  readonly hintId = input<string | undefined>(undefined);
  readonly errorId = input<string | undefined>(undefined);
}
