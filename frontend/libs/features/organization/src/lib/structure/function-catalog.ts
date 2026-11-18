/**
 * Functions that usually belong to a department, for a small IT company. Offered as suggestions
 * when a department is added or a function is added to one; nothing is created from this list
 * without someone choosing it.
 */
export const FUNCTION_CATALOG: Readonly<Record<string, readonly string[]>> = {
  Management: ['Leadership', 'Strategy & Planning', 'PMO', 'Legal & Compliance'],
  Engineering: ['Frontend', 'Backend', 'Mobile', 'Platform', 'Architecture', 'Data & AI'],
  'Quality Assurance': ['Manual Testing', 'Test Automation', 'Performance Testing'],
  'DevOps & IT': ['Cloud & Infrastructure', 'CI/CD', 'IT Support', 'Information Security'],
  'Product & Design': ['Product Management', 'UI/UX Design', 'Business Analysis'],
  'Sales & Marketing': ['Sales', 'Inside Sales', 'Pre-sales', 'Marketing', 'Digital Marketing', 'Partnerships'],
  'Customer Success': ['Client Support', 'Account Management', 'Implementation'],
  'Ops & Process': ['Operations', 'Process & Quality', 'Admin & Facilities', 'Procurement', 'Project Coordination'],
  'Finance & Accounts': ['Accounts', 'Payroll', 'Billing & Invoicing', 'Taxation & Compliance'],
  'Human Resources': ['Talent Acquisition', 'HR Operations', 'Learning & Development', 'Employee Engagement'],
};

/** Departments are matched loosely ("Finance" finds "Finance & Accounts", "People team" finds nothing). */
export function suggestedFunctions(departmentName: string): readonly string[] {
  const name = departmentName.trim().toLowerCase();
  const key = Object.keys(FUNCTION_CATALOG).find((entry) => {
    const candidate = entry.toLowerCase();
    return candidate === name || candidate.startsWith(name) || name.startsWith(candidate);
  });
  return key ? FUNCTION_CATALOG[key] : [];
}

/** A short code part from a name: "Admin & Facilities" → "ADMFAC". */
export function codePart(name: string): string {
  return (
    name
      .toUpperCase()
      .replace(/&/g, ' ')
      .replace(/[^A-Z0-9]+/g, ' ')
      .trim()
      .split(' ')
      .filter(Boolean)
      .map((word) => word.slice(0, 3))
      .join('')
      .slice(0, 8) || 'NEW'
  );
}
