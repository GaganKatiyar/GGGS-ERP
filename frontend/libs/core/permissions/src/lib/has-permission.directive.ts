import {
  Directive,
  TemplateRef,
  ViewContainerRef,
  effect,
  inject,
  input,
} from '@angular/core';
import { PermissionStore } from './permission.store';

/**
 * Renders content only when the user holds the permission:
 *
 * ```html
 * <button *gggsHasPermission="'leave.request.approve'">Approve</button>
 * <div *gggsHasPermission="['payroll.run.view', 'payroll.run.manage']; mode: 'all'">…</div>
 * ```
 *
 * This is presentation, not protection: the API enforces the same permission on every call. Using
 * it keeps the UI honest - a screen full of buttons that all return 403 is worse than a smaller
 * screen that works.
 */
@Directive({
  selector: '[gggsHasPermission]',
})
export class HasPermissionDirective {
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);
  private readonly permissions = inject(PermissionStore);

  /** One code, or several. */
  readonly gggsHasPermission = input.required<string | readonly string[]>();

  /** `any` (the default) or `all` when several codes are given. */
  readonly gggsHasPermissionMode = input<'any' | 'all'>('any');

  /** Optionally also require a licensed feature, so unlicensed modules stay hidden. */
  readonly gggsHasPermissionFeature = input<string | undefined>(undefined);

  private isRendered = false;

  constructor() {
    effect(() => {
      const codes = this.normalizeCodes(this.gggsHasPermission());
      const feature = this.gggsHasPermissionFeature();

      const permitted =
        (this.gggsHasPermissionMode() === 'all'
          ? this.permissions.hasAll(codes)
          : this.permissions.hasAny(codes)) &&
        (feature === undefined || this.permissions.hasFeature(feature));

      this.render(permitted);
    });
  }

  private render(permitted: boolean): void {
    if (permitted && !this.isRendered) {
      this.viewContainer.createEmbeddedView(this.templateRef);
      this.isRendered = true;
      return;
    }

    if (!permitted && this.isRendered) {
      this.viewContainer.clear();
      this.isRendered = false;
    }
  }

  private normalizeCodes(value: string | readonly string[]): readonly string[] {
    return typeof value === 'string' ? [value] : value;
  }
}
