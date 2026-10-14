import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  effect,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { NG_VALUE_ACCESSOR, type ControlValueAccessor } from '@angular/forms';
import type { LookupList, LookupValue } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * Lookup lists (spec §113). Values are cached per list and parent for the session: they change
 * rarely, every form reads them, and an admin edit calls {@link invalidate}.
 */
@Injectable({ providedIn: 'root' })
export class MasterDataApi {
  private readonly http = inject(HttpClient);
  private readonly cache = new Map<string, Promise<readonly LookupValue[]>>();

  lists(): Promise<readonly LookupList[]> {
    return firstValueFrom(this.http.get<readonly LookupList[]>('/api/v1/master-data'));
  }

  /** Active values of a list, optionally under a parent (a country's states). */
  values(list: string, parent?: string | null): Promise<readonly LookupValue[]> {
    const key = `${list}|${parent ?? ''}`;
    let cached = this.cache.get(key);

    if (!cached) {
      const params: Record<string, string> = parent ? { parent } : {};
      cached = firstValueFrom(this.http.get<readonly LookupValue[]>(`/api/v1/master-data/${list}`, { params }));
      cached.catch(() => this.cache.delete(key));
      this.cache.set(key, cached);
    }

    return cached;
  }

  /** Every value including inactive ones, uncached - for the administration screen. */
  allValues(list: string): Promise<readonly LookupValue[]> {
    return firstValueFrom(this.http.get<readonly LookupValue[]>(`/api/v1/master-data/${list}`, { params: { includeInactive: true } }));
  }

  async create(list: string, value: { code: string; label: string; parentCode?: string | null }): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/master-data/${list}`, value));
    this.invalidate(list);
  }

  async update(list: string, id: string, value: { label: string; sortOrder: number }): Promise<void> {
    await firstValueFrom(this.http.put(`/api/v1/master-data/${list}/${id}`, value));
    this.invalidate(list);
  }

  async setActive(list: string, id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/master-data/${list}/${id}/${active ? 'activate' : 'deactivate'}`, {}));
    this.invalidate(list);
  }

  invalidate(list: string): void {
    for (const key of [...this.cache.keys()]) {
      if (key.startsWith(`${list}|`)) {
        this.cache.delete(key);
      }
    }
  }
}

/**
 * A dropdown fed by a lookup list - the replacement for every hard-coded `<option>` list. Works
 * with reactive forms; the value is the code. Give `parent` to show only a parent's children
 * (states of the chosen country).
 */
@Component({
  selector: 'gggs-lookup-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => LookupSelect), multi: true }],
  template: `
    <select
      [value]="value() ?? ''"
      [disabled]="disabled()"
      [attr.aria-label]="ariaLabel()"
      (change)="choose($event)"
      (blur)="onTouched()"
    >
      <option value="">{{ placeholder() }}</option>
      @for (option of options(); track option.code) {
        <option [value]="option.code" [selected]="option.code === value()">{{ option.label }}</option>
      }
    </select>
  `,
  styles: `
    select {
      width: 100%;
    }
  `,
})
export class LookupSelect implements ControlValueAccessor {
  private readonly api = inject(MasterDataApi);

  readonly list = input.required<string>();
  readonly parent = input<string | null | undefined>(undefined);
  readonly placeholder = input('Select…');
  readonly ariaLabel = input<string | undefined>(undefined);

  protected readonly options = signal<readonly LookupValue[]>([]);
  protected readonly value = signal<string | null>(null);
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    effect(async () => {
      const list = this.list();
      const parent = this.parent();

      // A child list with no parent chosen yet shows nothing, rather than every state on earth.
      if (parent === null || parent === '') {
        this.options.set([]);
        return;
      }

      this.options.set(await this.api.values(list, parent));
    });
  }

  writeValue(value: string | null): void {
    this.value.set(value || null);
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected choose(event: Event): void {
    const value = (event.target as HTMLSelectElement).value || null;
    this.value.set(value);
    this.onChange(value);
  }
}
