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
  | { readonly kind: 'org'; readonly orgKind: OrgKind; readonly topLevelOnly?: boolean }
  | { readonly kind: 'employee' }
  | { readonly kind: 'tags' };

export interface OrgField {
  /** Property on the API record; dotted for nested values (`address.city`). */
  readonly key: string;
  readonly label: string;
  readonly type: OrgFieldType;
  readonly required?: boolean;
  /** Set on create only, hidden when editing (an effective-from date, for example). */
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
  /** Departments end on a date rather than being deactivated. */
  readonly closable?: boolean;
  /** Columns that hold another record's id, shown by that record's name. */
  readonly references?: Readonly<Partial<Record<ReferenceColumn, OrgKind>>>;
}

/** The id columns {@link OrgKindConfig.references} can show by name. */
export type ReferenceColumn = 'departmentId' | 'roleId' | 'levelId';

const MANAGE = 'org.structure.manage';

const identity: readonly OrgField[] = [
  {
    key: 'code',
    label: 'Code',
    type: { kind: 'text', maxLength: 32 },
    required: true,
    hint: 'Short, e.g. ENG or BLR. Changing it later is fine inside the app; update any import files that use the old code.',
  },
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
    kind: 'companies',
    title: 'Companies',
    singular: 'company',
    subtitle: 'Registered companies (legal entities) - the employer of record and the payroll boundary.',
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
      { key: 'companyId', label: 'Company', type: { kind: 'org', orgKind: 'companies' } },
      { key: 'headEmployeeId', label: 'Head', type: { kind: 'employee' } },
    ],
  },
  departments: {
    kind: 'departments',
    title: 'Departments & functions',
    singular: 'department or function',
    subtitle:
      'Two levels: a department sits in a business unit, and its functions are the teams inside it. Closing one ends it on a date and keeps its history.',
    managePermission: 'org.department.manage',
    closable: true,
    columns: [...codeName, { field: 'effectiveFrom', header: 'From', format: 'date', sortable: false, width: '8rem' }, { field: 'effectiveTo', header: 'Until', format: 'date', width: '8rem' }, status],
    fields: [
      ...identity,
      {
        key: 'parentDepartmentId',
        label: 'Belongs to department',
        type: { kind: 'org', orgKind: 'departments', topLevelOnly: true },
        hint: 'Leave empty for a department. Choose one to make this a function of that department.',
      },
      { key: 'businessUnitId', label: 'Business unit', type: { kind: 'org', orgKind: 'business-units' }, hint: 'A function always takes its department’s business unit.' },
      { key: 'headEmployeeId', label: 'Head', type: { kind: 'employee' } },
      { key: 'locationId', label: 'Location', type: { kind: 'org', orgKind: 'locations' } },
      { key: 'costCenterId', label: 'Cost center', type: { kind: 'org', orgKind: 'cost-centers' } },
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
      { key: 'companyId', label: 'Company', type: { kind: 'org', orgKind: 'companies' } },
      {
        key: 'costCenterId',
        label: 'Cost center',
        type: { kind: 'org', orgKind: 'cost-centers' },
        hint: 'The company name people at this office work under. Leave empty to use the company default.',
      },
      { key: 'timeZone', label: 'Time zone', type: { kind: 'text', maxLength: 64 }, hint: 'IANA name, e.g. Asia/Kolkata.' },
      ...address,
    ],
  },
  'cost-centers': {
    kind: 'cost-centers',
    title: 'Cost centers',
    singular: 'cost center',
    subtitle:
      'The company name each office works under, printed on offer letters and payslips. Every company has one default, named after it, that anyone without an office-specific cost center uses.',
    managePermission: MANAGE,
    columns: [
      { field: 'code', header: 'Code', sortable: true, width: '9rem' },
      { field: 'name', header: 'Name on letters', sortable: true },
      { field: 'isDefault', header: 'Default', format: 'boolean', width: '7rem' },
      { field: 'gstin', header: 'GSTIN', width: '11rem' },
      status,
    ],
    fields: [
      identity[0],
      { key: 'name', label: 'Name on letters', type: { kind: 'text', maxLength: 200 }, required: true, hint: 'The company name this office works under, e.g. GGGS Infotech.' },
      { key: 'companyId', label: 'Parent company', type: { kind: 'org', orgKind: 'companies' } },
      { key: 'isDefault', label: 'Default for this company', type: { kind: 'checkbox' }, hint: 'Used for anyone whose office has no cost center of its own.' },
      { key: 'gstin', label: 'GSTIN', type: { kind: 'text', maxLength: 15 }, hint: 'Only when this office has its own registration.' },
      { key: 'pan', label: 'PAN', type: { kind: 'text', maxLength: 10 } },
      { key: 'ownerEmployeeId', label: 'Owner', type: { kind: 'employee' } },
      ...address,
    ],
  },
  'levels': {
    kind: 'levels',
    title: 'Levels',
    singular: 'level',
    subtitle: 'How senior someone is, from junior to senior. Salary bands, notice periods and career paths use them.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'rank', header: 'Rank', align: 'right', width: '6rem' }, status],
    fields: [...identity, { key: 'rank', label: 'Rank', type: { kind: 'number', min: 0, step: 1 }, required: true, hint: '1 is the most junior.' }],
  },
  designations: {
    kind: 'designations',
    title: 'Designations',
    singular: 'designation',
    subtitle: 'The titles people hold - a role at a level, e.g. "Business Analyst L2". Each role gets one per level automatically.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'roleId', header: 'Role' }, { field: 'levelId', header: 'Level', width: '10rem' }, status],
    references: { roleId: 'roles', levelId: 'levels' },
    fields: [
      ...identity,
      {
        key: 'roleId',
        label: 'Role',
        type: { kind: 'org', orgKind: 'roles' },
        hint: 'Designations made for a role follow it: renamed, recoded and switched off with it. Leave empty for a title of its own.',
      },
      { key: 'levelId', label: 'Level', type: { kind: 'org', orgKind: 'levels' } },
    ],
  },
  roles: {
    kind: 'roles',
    title: 'Roles',
    singular: 'role',
    subtitle: 'The work people do. A role belongs to a department and is open to all of its functions; it gets a designation for every level.',
    managePermission: MANAGE,
    columns: [...codeName, { field: 'departmentId', header: 'Department' }, { field: 'jobFamilyCode', header: 'Family', hidden: true }, status],
    references: { departmentId: 'departments' },
    fields: [
      ...identity,
      {
        key: 'departmentId',
        label: 'Department',
        type: { kind: 'org', orgKind: 'departments', topLevelOnly: true },
        hint: 'People in this department, or any of its functions, can hold the role. Empty: any department.',
      },
      { key: 'jobFamilyCode', label: 'Job family', type: { kind: 'lookup', list: LOOKUP_LISTS.jobFamily } },
      { key: 'minimumExperienceYears', label: 'Minimum experience (years)', type: { kind: 'number', min: 0, step: 0.5 } },
      { key: 'maximumExperienceYears', label: 'Maximum experience (years)', type: { kind: 'number', min: 0, step: 0.5 } },
      { key: 'requiredSkills', label: 'Required skills', type: { kind: 'tags' }, hint: 'Comma-separated, e.g. .NET, Angular, Azure.', fullWidth: true },
      { key: 'description', label: 'Description', type: { kind: 'textarea' }, fullWidth: true },
    ],
  },
};
