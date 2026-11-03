// PeopleOS role audit: signs in as one user per role and walks the app the way that person would.
//
//   node tools/role-audit/role-audit.mjs [--only=employee,manager] [--setup-only] [--parallel=3]
//
// For each persona it
//   1. opens every rail module and collects the pages it offers (the module and its page tabs),
//   2. visits each of those pages (and the first record behind a list) and flags script errors,
//      failed API calls (403 on a page the menu offers is a permission mismatch, 5xx is a bug) and
//      error toasts,
//   3. visits every other page in the navigation manifest directly by URL and records whether it
//      was refused (forbidden page or 403s) or opened with data - the latter is a possible leak or a
//      menu entry hidden by mistake.
// Missing persona accounts are created first through the API as the administrator (invite, then
// accept the invitation with the shared password), so the run is repeatable on a fresh DevSeed.
//
// Needs the app running (web on :4200 proxying the API). Writes ../.local/role-audit/: report.md,
// report.json and screenshots of pages with problems. Nothing is submitted on the walked pages.
import { chromium } from 'playwright';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const frontend = resolve(here, '..', '..');
const out = resolve(frontend, '..', '.local', 'role-audit');
const args = Object.fromEntries(process.argv.slice(2).map((arg) => arg.replace(/^--/, '').split('=')));
const BASE = process.env.ROLE_AUDIT_BASE ?? 'http://localhost:4200';
const TENANT = 'acme';
const PASSWORD = 'Passw0rd!2026';
const ADMIN = 'admin@acme.example';
const PARALLEL = Number(args.parallel ?? 3);

/** Who signs in, linked to which demo employee, holding which roles (by name). */
const PERSONAS = [
  { key: 'admin', label: 'Tenant admin + payroll admin', email: ADMIN, roles: null },
  { key: 'employee', label: 'Employee', email: 'meera.iyer@acme.example', name: 'Meera Iyer', employee: 'EMP00003', roles: ['Employee'] },
  { key: 'manager', label: 'Manager (team lead)', email: 'vikram.shah@acme.example', name: 'Vikram Shah', employee: 'EMP00002', roles: ['Employee', 'Manager'] },
  { key: 'executive', label: 'Department head + executive', email: 'anita.rao@acme.example', name: 'Anita Rao', employee: 'EMP00001', roles: ['Employee', 'Department Head', 'Executive'] },
  { key: 'hr', label: 'HR administrator', email: 'arjun.nair@acme.example', name: 'Arjun Nair', employee: 'EMP00004', roles: ['Employee', 'HR Administrator'] },
  { key: 'finance', label: 'Finance', email: 'rohan.das@acme.example', name: 'Rohan Das', employee: 'EMP00006', roles: ['Employee', 'Finance'] },
  { key: 'recruiter', label: 'Recruiter (no employee record)', email: 'recruiter@acme.example', name: 'Riya Recruiter', roles: ['Recruiter'] },
  { key: 'auditor', label: 'Auditor (no employee record)', email: 'auditor@acme.example', name: 'Ajay Auditor', roles: ['Auditor'] },
];

const only = args.only ? args.only.split(',') : null;
const personas = PERSONAS.filter((persona) => !only || only.includes(persona.key));
mkdirSync(out, { recursive: true });

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const patternOf = (path) => path.split('?')[0].split('/').map((s) => (UUID.test(s) ? ':id' : /^\d+$/.test(s) ? ':n' : s)).join('/');
const slug = (text) => text.replace(/^\//, '').replace(/[^a-z0-9]+/gi, '_') || 'root';

function manifestRoutes() {
  const source = readFileSync(join(frontend, 'libs/core/layout/src/lib/navigation.ts'), 'utf8');
  return [...new Set([...source.matchAll(/route: '([^']+)'/g)].map((match) => match[1]))];
}

async function signIn(context, email) {
  const page = await context.newPage();
  for (let attempt = 1; attempt <= 4; attempt++) {
    await page.goto(`${BASE}/sign-in`);
    await page.fill('input[formcontrolname=tenantCode]', TENANT);
    await page.fill('input[formcontrolname=email]', email);
    await page.fill('input[formcontrolname=password]', PASSWORD);
    await page.click('button[type=submit]');
    const ok = await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 20000 }).then(() => true, () => false);
    if (ok) return page;
    const message = (await page.locator('main, form').first().innerText().catch(() => '')).slice(0, 200);
    if (!/too many|try again/i.test(message) || attempt === 4) throw new Error(`Sign-in failed for ${email}: ${message.replace(/\s+/g, ' ')}`);
    await page.waitForTimeout(20000);
  }
  throw new Error('unreachable');
}

/** Creates the persona accounts that do not exist yet, as the administrator, through the API. */
async function ensureAccounts(browser) {
  const context = await browser.newContext();
  await signIn(context, ADMIN);
  const api = context.request;
  const headers = { 'X-CSRF': '1' };
  const json = async (response, what) => {
    if (!response.ok()) throw new Error(`${what}: ${response.status()} ${(await response.text()).slice(0, 300)}`);
    return response.status() === 204 ? null : response.json();
  };
  const roles = await json(await api.get(`${BASE}/api/v1/roles`, { headers }), 'roles');
  const roleItems = roles.items ?? roles;
  const employees = await json(await api.get(`${BASE}/api/v1/employees?pageSize=200`, { headers }), 'employees');
  const employeeItems = employees.items ?? employees;
  const log = [];
  for (const persona of PERSONAS.filter((p) => p.roles)) {
    const existing = await json(await api.get(`${BASE}/api/v1/users?search=${encodeURIComponent(persona.email)}`, { headers }), 'users');
    if ((existing.items ?? []).some((user) => user.email?.toLowerCase() === persona.email)) {
      log.push(`${persona.email}: already exists`);
      continue;
    }
    const roleIds = persona.roles.map((code) => {
      const role = roleItems.find((item) => item.name === code);
      if (!role) throw new Error(`Role ${code} not found`);
      return role.id;
    });
    const employee = persona.employee ? employeeItems.find((item) => item.employeeCode === persona.employee || item.code === persona.employee) : null;
    if (persona.employee && !employee) throw new Error(`Employee ${persona.employee} not found`);
    const invited = await json(await api.post(`${BASE}/api/v1/users/invite`, { headers, data: { email: persona.email, displayName: persona.name, employeeId: employee?.id ?? null, roleIds } }), `invite ${persona.email}`);
    await json(await api.post(`${BASE}/api/v1/auth/complete-invitation`, { headers, data: { tenantCode: TENANT, userId: invited.userId, token: invited.invitationToken, newPassword: PASSWORD } }), `accept ${persona.email}`);
    log.push(`${persona.email}: created with ${persona.roles.join(', ')}`);
  }
  await context.close();
  return log;
}

/** Tracks API traffic on a page so a visit can wait for it to finish and report what failed. */
function instrument(page) {
  const state = { inflight: 0, api: [], errors: [], pending: new Map(), slow: [] };
  const label = (request) => `${request.method()} ${new URL(request.url()).pathname}${new URL(request.url()).search}`;
  page.on('request', (request) => {
    if (!request.url().includes('/api/')) return;
    state.inflight++;
    state.pending.set(request, Date.now());
  });
  const done = (request) => {
    if (!state.pending.has(request)) return;
    const took = Date.now() - state.pending.get(request);
    state.pending.delete(request);
    state.inflight = state.pending.size;
    if (took > 4000) state.slow.push(`${label(request)} took ${(took / 1000).toFixed(1)}s`);
  };
  page.on('requestfinished', done);
  page.on('requestfailed', done);
  page.on('response', (response) => {
    const url = response.url();
    if (url.includes('/api/')) state.api.push({ status: response.status(), method: response.request().method(), url: new URL(url).pathname + new URL(url).search });
  });
  page.on('console', (message) => message.type() === 'error' && state.errors.push(message.text().slice(0, 300)));
  page.on('pageerror', (error) => state.errors.push(`PAGEERROR ${error.message.slice(0, 300)}`));
  return state;
}

async function settle(page, state) {
  await page.waitForLoadState('domcontentloaded').catch(() => {});
  const started = Date.now();
  let quietSince = Date.now();
  while (Date.now() - started < 12000) {
    await page.waitForTimeout(150);
    if (state.inflight > 0) quietSince = Date.now();
    else if (Date.now() - quietSince > 700) break;
  }
  await page.waitForTimeout(250);
}

async function visit(page, state, path) {
  state.api = [];
  state.errors = [];
  state.slow = [];
  state.pending.clear();
  state.inflight = 0;
  await page.goto(BASE + path).catch((error) => state.errors.push(`NAV ${error.message.slice(0, 200)}`));
  await settle(page, state);
  const hung = [...state.pending.entries()].map(([request, since]) => `${request.method()} ${new URL(request.url()).pathname} still waiting after ${((Date.now() - since) / 1000).toFixed(1)}s`);
  const landed = new URL(page.url());
  const toasts = await page.locator('.p-toast-message-error, .p-toast-message-warn').allInnerTexts().catch(() => []);
  const main = (await page.locator('#main-content').innerText().catch(() => '')).trim();
  return {
    path,
    landed: landed.pathname + landed.search,
    api: [...state.api],
    errors: [...new Set(state.errors)].filter((text) => !/Failed to load resource: the server responded with a status of 40[134]/.test(text)),
    toasts: toasts.map((text) => text.replace(/\s+/g, ' ').trim()).filter(Boolean),
    slow: [...state.slow],
    hung,
    mainLength: main.length,
    mainStart: main.replace(/\s+/g, ' ').slice(0, 120),
  };
}

/** Opens each rail module and collects the pages it offers: the module itself and its page tabs. */
async function menuRoutes(page, state) {
  await page.goto(`${BASE}/dashboard`);
  await settle(page, state);
  const hrefs = (selector, attribute = 'href') => page.locator(selector).evaluateAll((links, name) => links.map((link) => link.getAttribute(name)), attribute);
  const routes = new Set();
  const add = (list) => list.filter((href) => href && href.startsWith('/')).forEach((href) => routes.add(href.split(/[?#]/)[0]));
  // Each module's entry page, from whichever menu style is showing (the sidebar's modules are buttons).
  const modules = (await hrefs('#shell-navigation [data-nav-module]', 'data-nav-module')).filter(Boolean);
  add(modules);
  for (const module of modules) {
    await page.goto(BASE + module);
    await settle(page, state);
    // A module's pages are its tabs; the Settings home lists its pages as cards instead.
    add(await hrefs('.module-tabs a[href], .settings-card__link[href]'));
  }
  return [...routes].filter((route) => !/^\/(bff|api)\b/.test(route));
}

/** The first record a list page opens, by link or by clicking its first row. */
async function firstRecord(page, state) {
  const row = page.locator('#main-content .p-datatable-tbody > tr:not(.p-datatable-emptymessage)').first();
  if (!(await row.count()) || !(await row.isVisible().catch(() => false))) return null;
  const link = row.locator('a[href^="/"]').first();
  if (await link.count()) return link.getAttribute('href');
  const before = page.url();
  await row.click({ position: { x: 12, y: 12 } }).catch(() => {});
  await page.waitForTimeout(1000);
  const after = page.url();
  if (after === before) return null;
  await settle(page, state);
  return new URL(after).pathname + new URL(after).search;
}

function problemsOf(result, offered) {
  const problems = [];
  if (result.landed.startsWith('/sign-in')) problems.push({ severity: 'blocker', rule: 'signed-out', detail: 'Visiting the page signed the user out.' });
  if (offered && result.landed.startsWith('/forbidden')) problems.push({ severity: 'major', rule: 'menu-page-forbidden', detail: 'The menu offers this page but it opens the forbidden page.' });
  for (const call of result.api.filter((c) => c.status >= 500)) problems.push({ severity: 'blocker', rule: 'api-5xx', detail: `${call.status} ${call.method} ${call.url}` });
  if (offered) for (const call of result.api.filter((c) => c.status === 403)) problems.push({ severity: 'major', rule: 'api-403-on-offered-page', detail: `${call.method} ${call.url}` });
  for (const call of result.api.filter((c) => c.status === 404 && c.method === 'GET')) problems.push({ severity: 'minor', rule: 'api-404', detail: `${call.method} ${call.url}` });
  for (const error of result.errors) problems.push({ severity: /PAGEERROR|TypeError|NG0/.test(error) ? 'major' : 'minor', rule: 'console-error', detail: error });
  for (const call of result.hung) problems.push({ severity: 'major', rule: 'api-hung', detail: call });
  for (const call of result.slow) problems.push({ severity: 'minor', rule: 'api-slow', detail: call });
  for (const toast of result.toasts) problems.push({ severity: 'major', rule: 'error-toast', detail: toast.slice(0, 200) });
  if (offered && result.mainLength < 20 && !result.landed.startsWith('/forbidden')) problems.push({ severity: 'major', rule: 'empty-page', detail: 'The main area rendered (almost) nothing.' });
  return problems;
}

/** How a page the menu does not offer behaved when opened by URL. */
function refusalOf(result) {
  if (result.landed.startsWith('/forbidden')) return 'forbidden-page';
  if (result.landed.startsWith('/feature-unavailable')) return 'feature-page';
  if (result.landed.startsWith('/sign-in')) return 'signed-out';
  const reads = result.api.filter((call) => call.method === 'GET' && !/\/(me|bff|notifications|approvals\/count|search)\b/.test(call.url));
  const denied = reads.filter((call) => call.status === 403);
  const allowed = reads.filter((call) => call.status >= 200 && call.status < 300);
  if (denied.length && !allowed.length) return 'api-403';
  if (denied.length && allowed.length) return 'partly-open';
  if (allowed.length) return 'open';
  return 'no-data-calls';
}

async function auditPersona(browser, persona, manifest) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const started = Date.now();
  const report = { ...persona, offered: [], pages: [], hidden: [], problems: [] };
  try {
    const page = await signIn(context, persona.email);
    const state = instrument(page);
    await settle(page, state);
    await page.screenshot({ path: join(out, persona.key, 'home.png') }).catch(() => {});
    report.offered = await menuRoutes(page, state);
    // Menu pages first, then one level of what they link to (records, sub-pages) - the screens a
    // person reaches by clicking, which is where row-level scoping shows.
    const seen = new Set();
    const queue = report.offered.map((path) => ({ path, depth: 0 }));
    while (queue.length) {
      const { path, depth } = queue.shift();
      const pattern = patternOf(path);
      if (seen.has(pattern)) continue;
      seen.add(pattern);
      const result = await visit(page, state, path);
      const problems = problemsOf(result, true);
      report.pages.push({ pattern, path, depth, landed: result.landed, problems: problems.length });
      if (problems.length) {
        await page.screenshot({ path: join(out, persona.key, `${slug(pattern)}.png`), fullPage: false }).catch(() => {});
        report.problems.push(...problems.map((problem) => ({ ...problem, pattern })));
      }
      if (depth > 0) continue;
      const links = await page.locator('#main-content a[href^="/"]').evaluateAll((anchors) => anchors.map((a) => a.getAttribute('href'))).catch(() => []);
      const record = await firstRecord(page, state).catch(() => null);
      for (const link of [...links, record].filter(Boolean)) {
        const clean = link.split('#')[0];
        if (!/^\/(bff|api|sign-in|forbidden)\b/.test(clean) && !seen.has(patternOf(clean))) queue.push({ path: clean, depth: 1 });
      }
    }
    const offeredPatterns = new Set(report.offered.map(patternOf));
    for (const route of manifest.filter((r) => !offeredPatterns.has(patternOf(r)))) {
      const result = await visit(page, state, route);
      const outcome = refusalOf(result);
      report.hidden.push({ route, outcome, landed: result.landed, sample: result.api.filter((c) => c.status < 300 && c.method === 'GET').slice(0, 3).map((c) => c.url) });
      const crashes = problemsOf(result, false).filter((p) => p.severity === 'blocker' || p.rule === 'console-error' && /PAGEERROR/.test(p.detail));
      report.problems.push(...crashes.map((problem) => ({ ...problem, pattern: `${route} (not in menu)` })));
      if (outcome === 'open' || outcome === 'partly-open') await page.screenshot({ path: join(out, persona.key, `hidden_${slug(route)}.png`) }).catch(() => {});
    }
  } catch (error) {
    report.problems.push({ severity: 'blocker', rule: 'persona-failed', detail: String(error).slice(0, 300), pattern: '-' });
  }
  await context.close();
  report.seconds = Math.round((Date.now() - started) / 1000);
  console.log(`${persona.key}: ${report.offered.length} menu pages, ${report.pages.length} visited, ${report.hidden.length} hidden checked, ${report.problems.length} problems (${report.seconds}s)`);
  return report;
}

function render(reports, setupLog) {
  const lines = [`# Role audit - ${new Date().toISOString()}`, '', '## Accounts', '', ...setupLog.map((line) => `- ${line}`), '', '## Summary', '',
    '| Persona | Roles | Menu pages | Pages visited | Problems (blocker/major/minor) | Hidden pages: refused / open |', '|---|---|---|---|---|---|'];
  for (const r of reports) {
    const count = (s) => r.problems.filter((p) => p.severity === s).length;
    const refused = r.hidden.filter((h) => ['forbidden-page', 'feature-page', 'api-403'].includes(h.outcome)).length;
    const open = r.hidden.filter((h) => ['open', 'partly-open'].includes(h.outcome)).length;
    lines.push(`| ${r.label} (${r.email}) | ${(r.roles ?? ['Tenant Administrator', 'Payroll Administrator']).join(', ')} | ${r.offered.length} | ${r.pages.length} | ${count('blocker')}/${count('major')}/${count('minor')} | ${refused} / ${open} |`);
  }
  for (const r of reports) {
    lines.push('', `## ${r.label}`, '');
    const grouped = new Map();
    for (const p of r.problems) {
      const key = `${p.severity} ${p.rule} - ${p.detail}`;
      grouped.set(key, [...(grouped.get(key) ?? []), p.pattern]);
    }
    if (!grouped.size) lines.push('No problems on the pages its menu offers.');
    for (const [key, patterns] of [...grouped].sort()) lines.push(`- **${key}** on ${[...new Set(patterns)].slice(0, 6).map((p) => `\`${p}\``).join(', ')}${patterns.length > 6 ? ` +${patterns.length - 6}` : ''}`);
    const open = r.hidden.filter((h) => ['open', 'partly-open'].includes(h.outcome));
    if (open.length) {
      lines.push('', `Pages not in the menu that still returned data when opened by URL (${open.length}):`, '');
      for (const h of open) lines.push(`- \`${h.route}\` ${h.outcome}: ${h.sample.join(', ')}`);
    }
    const quiet = r.hidden.filter((h) => h.outcome === 'no-data-calls');
    if (quiet.length) lines.push('', `Pages not in the menu that rendered without calling the API: ${quiet.map((h) => `\`${h.route}\``).join(', ')}`);
  }
  return lines.join('\n');
}

const browser = await chromium.launch();
const setupLog = await ensureAccounts(browser);
console.log(setupLog.join('\n'));
if (args['setup-only'] === undefined) {
  for (const persona of personas) mkdirSync(join(out, persona.key), { recursive: true });
  const manifest = manifestRoutes();
  const reports = [];
  const pending = [...personas];
  await Promise.all(Array.from({ length: PARALLEL }, async () => {
    while (pending.length) reports.push(await auditPersona(browser, pending.shift(), manifest));
  }));
  reports.sort((a, b) => PERSONAS.indexOf(PERSONAS.find((p) => p.key === a.key)) - PERSONAS.indexOf(PERSONAS.find((p) => p.key === b.key)));
  writeFileSync(join(out, 'report.json'), JSON.stringify(reports, null, 2));
  writeFileSync(join(out, 'report.md'), render(reports, setupLog));
  console.log(`Report: ${join(out, 'report.md')}`);
}
await browser.close();
