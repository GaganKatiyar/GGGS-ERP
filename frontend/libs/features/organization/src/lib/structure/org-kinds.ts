import type { DataTableColumn } from '@gggs/shared/data-table';
import type { OrgKind, OrgUnit } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';

/** How one form field is edited. */
export type OrgFieldType =
  | { readonly kind: 'text'; readonly maxLength?: number }
  | { readonly kind: 'textarea' }
  | { readonly kind: 'number'; readonly min?: number; readonly step?: number }
  | { readonly kind: 'date' }
  | { readonly kind: 'checkbox' }
  | { readonly kind: 'lookup'; readonly list: string; readonly parentField?: string }
  | { readonly kind: 'org'; readonly orgKind: OrgKind }
  | { readonly kind: 'employee' }
  | { readonly kind: 'tags' };

export interface OrgField {
  /** Property on the API record; dotted for nested values (`address.city`). */
  readonly key: string;
  readonly label: string;
  readonly type: OrgFieldType;
  readonly required?: boolean;
  /** Set on create only - codes never change once other records point at them. */
  readonly createOnly?: boolean;
  readonly hint?: string;
  readonly fullWidth?: boolean;
}

/** Everything the generic organization page needs to know about one kind. */
export interface OrgKindConfig {
  readonly kind: OrgKind;
  readonly title: string;
  readonly singular: string;
  readonly subtitle: string;
  readonly managePermission: string;
  readonly columns: readonly DataTableColumn<OrgUnit>[];
  readonly fields: readonly OrgField[];
  /** Departments and positions end on a date rather than being deactivated. */
  readonly closable?: boolean;
}

const MANAGE = 'org.structure.manage';

const identity: readonly OrgField[] = [
  { key: 'code', label: 'Code', type: { kind: 'text', maxLength: 32 }, required: true, createOnly: true, hint: 'Short and permanent, e.g. ENG or BLR.' },
  { key: 'name', label: 'Name', type: { kind: 'text', maxLength: 200 }, required: true },
];

const address: readonly OrgField[] = [
  { key: 'address.line1', label: 'Address line 1', type: { kind: 'text', maxLength: 200 } },
  { key: 'address.line2', label: 'Address line 2', type: { kind: 'text', maxLength: 200 } },
  { key: 'address.city', label: 'City', type: { kind: 'text', maxLength: 100 } },
  { key: 'address.countryCode', label: 'Country', type: { kind: 'lookup', list: LOOKUP_LISTS.country } },
  { key: 'address.stateCode', label: 'State', type: { kind: 'lookup', list: LOOKUP_LISTS.state, parentField: 'address.countryCode' } },
  { key: 'address.postalCode', label: 'Postal code', type: { kind: 'text', maxLength: 16 } },
];

const codeName: readonly DataTableColumn<OrgUnit>[] = [
  { field: 'code', header: 'Code', sortable: true, width: '9rem' },
  { field: 'name', header: 'Name', sortable: true },
];

const status: DataTableColumn<OrgUnit> = { field: 'isActive', header: 'Active', sortable: true, format: 'boolean', width: '6rem' };

export const ORG_KINDS: Readonly<Record<string, OrgKindConfig>> = {
  companies: {
    kind: 'legal-entities',
    title: 'Companies',
    singular: 'company',
    subtitle: 'Registered legal entities - the employer of record and the payroll boundary.',
    managePermission: 'org.legal-entity.manage',
    columns: [...codeName, { field: 'countryCode', header: 'Country', width: '7rem' }, { field: 'gstin', header: 'GSTIN' }, status],
    fields: [
      ...identity,
      { key: 'legalName', label: 'Registered name', type: { kind: 'text', maxLength: 300 }, fullWidth: true },
      { key: 'countryCode', label: 'Country', type: { kind: 'lookup', list: LOOKUP_LISTS.country }, required: true },
      { key: 'currencyCode', label: 'Currency', type: { kind: 'lookup', list: LOOKUP_LISTS.currency }, required: true },
      { key: 'gstin', label: 'GSTIN', type: { kind: 'text', maxLength: 15 } },
      { key: 'pan', label: 'Company PAN', type: { kind: 'text', maxLength: 10 } },
      { key: 'cin', label: 'CIN', type: { kind: 'text', maxLength: 21 } },
      { key: 'tan', label: 'TAN', type: { kind: 'text', maxLength: 10 } },
      ...address,
    ],
  },
  'business-units': {
    kind: 'business-units',
    title: 'Business units',
    singular: 'business unit',
    subtitle: 'Lines of business, each optionally inside one company.',
    managePermission: MANAGE,
    columns: [...codeName, status],
    fields: [
      ...identity,
      { key: 'legalEntityId', label: 'Company', type: { kind: 'org', orgKind: 'legal-entities' } },
      { key: 'headEmployeeId', label: 'Head', type: { kind: 'employee' } },
    ],
  },
  divisions: {
    kind: 'divisions',
    title: 'Divisions',
    singular: 'division',
    subtitle: 'Groupings of departments inside a business unit.',
    managePermission: MANAGE,
    columns: [...codeName, status],
    fields: [
      ...identity,
      { key: 'businessUnitId', label: 'Business unit', type: { kind: 'org', orgKind: 'business-units' } },
      { key: 'headEmployeeId', label: 'Head', type: { kind: 'employee' } },
    ],
  },
  departments: {
    kind: 'departments',
    title: 'Departments',
    singular: 'department',
    subtitle: 'The reporting structure people sit in. Closing a department ends it on a date and keeps its history.',
    managePermission: 'org.department.manage',
    closable: true,
    columns: [...codeName, { field: 'effectiveFrom', header: 'From', format: 'date', sortable: false, width: '8rem' }, { field: 'effectiveTo', header: 'Until', format: 'date', width: '8rem' }, status],
    fields: [
      ...identity,
      { key: 'parentDepartmentId', label: 'Parent department', type: { kind: 'org', orgKind: 'departments' } },
      { key: 'divisionId', label: 'Division', type: { kind: 'org', orgKind: 'divisions' } },
      { key: 'headEmployeeId', label: 'Head', type: { kind: 'employee' } },
      { key: 'locationId', label: 'Location', type: { kind: 'org', orgKind: 'locations' } },
      { key: 'costCenterId', label: 'Cost center', type: { kind: 'org', orgKind: 'cost-centers' } },
      { key: 'profitCenterId', label: 'Profit center', type: { kind: 'org', orgKind: 'profit-centers' } },
      { key: 'effectiveFrom', label: 'Effective from', type: { kind: 'date' }, createOnly: true, hint: 'Defaults to today.' },
    ],
  },
  locations: {
    kind: 'locations',
    title: 'Locations',
    singular: 'location',
    subtitle: 'Offices, sites and remote pools people work from.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'timeZone', header: 'Time zone' }, status],
    fields: [
      ...identity,
      { key: 'legalEntityId', label: 'Company', type: { kind: 'org', orgKind: 'legal-entities' } },
      { key: 'timeZone', label: 'Time zone', type: { kind: 'text', maxLength: 64 }, hint: 'IANA name, e.g. Asia/Kolkata.' },
      ...address,
    ],
  },
  'cost-centers': {
    kind: 'cost-centers',
    title: 'Cost centers',
    singular: 'cost center',
    subtitle: 'Where costs are booked.',
    managePermission: MANAGE,
    columns: [...codeName, status],
    fields: [...identity, { key: 'ownerEmployeeId', label: 'Owner', type: { kind: 'employee' } }],
  },
  'profit-centers': {
    kind: 'profit-centers',
    title: 'Profit centers',
    singular: 'profit center',
    subtitle: 'Where revenue and margin are reported.',
    managePermission: MANAGE,
    columns: [...codeName, status],
    fields: [...identity, { key: 'ownerEmployeeId', label: 'Owner', type: { kind: 'employee' } }],
  },
  grades: {
    kind: 'grades',
    title: 'Grades',
    singular: 'grade',
    subtitle: 'Pay and seniority bands, ordered from junior to senior.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'rank', header: 'Rank', sortable: false, align: 'right', width: '6rem' }, status],
    fields: [...identity, { key: 'rank', label: 'Rank', type: { kind: 'number', min: 0, step: 1 }, required: true, hint: '1 is the most junior.' }],
  },
  'job-levels': {
    kind: 'job-levels',
    title: 'Job levels',
    singular: 'job level',
    subtitle: 'Career levels used by jobs and career paths.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'rank', header: 'Rank', align: 'right', width: '6rem' }, status],
    fields: [...identity, { key: 'rank', label: 'Rank', type: { kind: 'number', min: 0, step: 1 }, required: true }],
  },
  jobs: {
    kind: 'jobs',
    title: 'Jobs',
    singular: 'job',
    subtitle: 'Role definitions: what the work is, at what level, needing what. Positions are seats for a job.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'jobFamilyCode', header: 'Family' }, status],
    fields: [
      ...identity,
      { key: 'jobFamilyCode', label: 'Job family', type: { kind: 'lookup', list: LOOKUP_LISTS.jobFamily } },
      { key: 'jobLevelId', label: 'Job level', type: { kind: 'org', orgKind: 'job-levels' } },
      { key: 'departmentId', label: 'Department', type: { kind: 'org', orgKind: 'departments' } },
      { key: 'minimumExperienceYears', label: 'Minimum experience (years)', type: { kind: 'number', min: 0, step: 0.5 } },
      { key: 'maximumExperienceYears', label: 'Maximum experience (years)', type: { kind: 'number', min: 0, step: 0.5 } },
      { key: 'requiredSkills', label: 'Required skills', type: { kind: 'tags' }, hint: 'Comma-separated, e.g. .NET, Angular, Azure.', fullWidth: true },
      { key: 'description', label: 'Description', type: { kind: 'textarea' }, fullWidth: true },
    ],
  },
  positions: {
    kind: 'positions',
    title: 'Positions',
    singular: 'position',
    subtitle: 'Actual seats in the organization: this job, in this department, reporting to that position.',
    managePermission: MANAGE,
    closable: true,
    columns: [...codeName, { field: 'headcount', header: 'Seats', align: 'right', width: '6rem' }, { field: 'isBudgeted', header: 'Budgeted', format: 'boolean', width: '7rem' }, status],
    fields: [
      ...identity,
      { key: 'jobId', label: 'Job', type: { kind: 'org', orgKind: 'jobs' }, required: true },
      { key: 'departmentId', label: 'Department', type: { kind: 'org', orgKind: 'departments' }, required: true },
      { key: 'reportsToPositionId', label: 'Reports to position', type: { kind: 'org', orgKind: 'positions' } },
      { key: 'gradeId', label: 'Grade', type: { kind: 'org', orgKind: 'grades' } },
      { key: 'locationId', label: 'Location', type: { kind: 'org', orgKind: 'locations' } },
      { key: 'headcount', label: 'Seats', type: { kind: 'number', min: 1, step: 1 }, hint: 'How many people may hold it at once.' },
      { key: 'isBudgeted', label: 'Budgeted', type: { kind: 'checkbox' } },
      { key: 'effectiveFrom', label: 'Effective from', type: { kind: 'date' }, createOnly: true },
    ],
  },
};
