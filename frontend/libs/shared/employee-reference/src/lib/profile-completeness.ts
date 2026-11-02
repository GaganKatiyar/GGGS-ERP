import type { EmployeeProfile } from '@gggs/shared/models';

export interface ProfileGap {
  readonly label: string;
  /** Which dialog fills it in. */
  readonly section: 'photo' | 'personal' | 'contact' | 'education';
}

export interface ProfileCompleteness {
  readonly percent: number;
  readonly gaps: readonly ProfileGap[];
}

/**
 * How much of what an employee can add about themselves is there. HR's fields (designation,
 * manager, dates) are always set, so they are not counted: the score is about the employee's part.
 * Education is counted when the caller knows how many entries there are (it is a separate read).
 */
export function profileCompleteness(profile: EmployeeProfile, educationCount?: number): ProfileCompleteness {
  const checks: readonly [ProfileGap, boolean][] = [
    [{ label: 'Photo', section: 'photo' }, !!profile.photoId],
    [{ label: 'Date of birth', section: 'personal' }, !!profile.dateOfBirth],
    [{ label: 'Gender', section: 'personal' }, !!profile.gender],
    [{ label: 'Blood group', section: 'personal' }, !!profile.bloodGroup],
    [{ label: 'Marital status', section: 'personal' }, !!profile.maritalStatus],
    [{ label: 'Nationality', section: 'personal' }, !!profile.nationality],
    [{ label: 'Mobile', section: 'contact' }, !!profile.mobilePhone],
    [{ label: 'Personal email', section: 'contact' }, !!profile.personalEmail],
    [{ label: 'Current address', section: 'contact' }, !!profile.currentAddress],
    [{ label: 'Permanent address', section: 'contact' }, !!profile.permanentAddress],
    ...(educationCount === undefined ? [] : [[{ label: 'Education', section: 'education' }, educationCount > 0] as [ProfileGap, boolean]]),
  ];

  const done = checks.filter(([, present]) => present).length;
  return {
    percent: Math.round((done / checks.length) * 100),
    gaps: checks.filter(([, present]) => !present).map(([gap]) => gap),
  };
}
