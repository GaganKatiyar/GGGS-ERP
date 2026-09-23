import { Injectable, computed, signal } from '@angular/core';
import type { DataScope, PermissionGrant } from '@gggs/shared/models';

/**
 * The permission set held by the signed-in user in the current tenant.
 *
 * This drives what the UI offers, and nothing more. Every endpoint checks the same permission
 * server-side, so hiding a button is a courtesy to the user, never a security control - a hidden
 * button is one devtools click away from being visible again.
 */
@Injectable({ providedIn: 'root' })
export class PermissionStore {
  private readonly grants = signal<ReadonlyMap<string, PermissionGrant>>(
    new Map(),
  );

  /** Feature codes the tenant's plan enables. A permission held but not licensed stays hidden. */
  private readonly enabledFeatures = signal<ReadonlySet<string>>(new Set());

  readonly count = computed(() => this.grants().size);

  setPermissions(
    grants: readonly PermissionGrant[],
    features: readonly string[],
  ): void {
    this.grants.set(
      new Map(grants.map((grant) => [grant.code.toLowerCase(), grant])),
    );
    this.enabledFeatures.set(
      new Set(features.map((feature) => feature.toLowerCase())),
    );
  }

  clear(): void {
    this.grants.set(new Map());
    this.enabledFeatures.set(new Set());
  }

  /** True when the user holds this permission code. Comparison is case-insensitive. */
  has(code: string): boolean {
    return this.grants().has(code.toLowerCase());
  }

  /** True when the user holds at least one of these codes. */
  hasAny(codes: readonly string[]): boolean {
    return codes.some((code) => this.has(code));
  }

  /** True when the user holds every one of these codes. */
  hasAll(codes: readonly string[]): boolean {
    return codes.every((code) => this.has(code));
  }

  /** How far the permission reaches, or undefined when it is not held at all. */
  scopeFor(code: string): DataScope | undefined {
    return this.grants().get(code.toLowerCase())?.scope;
  }

  hasFeature(featureCode: string): boolean {
    return this.enabledFeatures().has(featureCode.toLowerCase());
  }
}
