const TENANT_KEY = 'gggs.sign-in.tenant';

/** The organisation code, remembered on this device so its sign-in options appear next time. */
export function rememberTenant(tenantCode: string): void {
  try {
    if (tenantCode) {
      localStorage.setItem(TENANT_KEY, tenantCode);
    }
  } catch {
    // Storage is blocked: the code is simply typed again next time.
  }
}

export function rememberedTenant(): string | null {
  try {
    return localStorage.getItem(TENANT_KEY);
  } catch {
    return null;
  }
}
