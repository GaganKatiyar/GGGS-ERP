/**
 * The company's legal details, in one place so the policies never disagree. Replace each bracketed
 * value with the real one before going live: anything left in brackets shows highlighted on the page.
 */
export const LEGAL = {
  company: 'GGGS Technologies Pvt Ltd',
  effectiveDate: '[effective date]',
  registeredAddress: '[registered address]',
  website: '[website]',
  privacyEmail: '[privacy email]',
  grievanceOfficer: '[grievance officer name]',
  grievanceEmail: '[grievance officer email]',
  courtCity: '[city]',
  hostingLocation: '[hosting location]',
} as const;

/** One numbered part of a policy. A paragraph or list item may hold a bracketed blank. */
export interface LegalSection {
  readonly title: string;
  readonly paragraphs?: readonly string[];
  readonly items?: readonly string[];
  readonly after?: readonly string[];
}

/** Splits text around bracketed blanks, so the page can highlight the ones not yet filled in. */
export function legalParts(text: string): readonly { readonly text: string; readonly blank: boolean }[] {
  return text
    .split(/(\[[^\]]+\])/)
    .filter((part) => part.length > 0)
    .map((part) => ({ text: part, blank: part.startsWith('[') }));
}
