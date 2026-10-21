// PeopleOS UI audit: crawls every reachable screen as a signed-in administrator and reports layout,
// content and accessibility defects with a screenshot of each.
//
//   node tools/ui-audit/audit.mjs [--only=/payroll] [--variants=desktop,mobile,dark] [--max=400]
//
// Needs the app running (web on :4200 proxying the API). Writes to ../.local/ui-audit/ (git-ignored):
// report.md, report.json and one folder of screenshots per route pattern. Nothing is submitted:
// dialogs are opened from page-header actions, inspected and closed with Escape.
import { AxeBuilder } from '@axe-core/playwright';
import { chromium } from 'playwright';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const frontend = resolve(here, '..', '..');
const out = resolve(frontend, '..', '.local', 'ui-audit');
const args = Object.fromEntries(process.argv.slice(2).map((arg) => arg.replace(/^--/, '').split('=')));
const BASE = process.env.UI_AUDIT_BASE ?? 'http://localhost:4200';
const ONLY = args.only ?? '';
const VARIANTS = (args.variants ?? 'desktop,mobile,dark').split(',');
const MAX = Number(args.max ?? 400);
const VIEWPORTS = { desktop: { width: 1440, height: 900 }, mobile: { width: 390, height: 844 }, dark: { width: 1440, height: 900 } };
const DIALOG_TRIGGER = /^(new|add|create|hire|invite|raise|record|upload|apply for|give|request)\b/i;

mkdirSync(out, { recursive: true });

/** `/employees/0193...-.../edit?tab=bank` -> `/employees/:id/edit?tab=bank` */
function patternOf(url) {
  const parsed = new URL(url, BASE);
  const path = parsed.pathname
    .split('/')
    .map((segment) =>
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(segment) ? ':id'
        : /^\d{4}-\d{2}-\d{2}$/.test(segment) ? ':date'
          : /^\d+$/.test(segment) ? ':n'
            : segment)
    .join('/');
  const tab = parsed.searchParams.get('tab');
  return tab ? `${path}?tab=${tab}` : path;
}

const slug = (pattern) => pattern.replace(/^\//, '').replace(/[/:?=&]+/g, '_') || 'root';

function seeds() {
  const source = readFileSync(join(frontend, 'libs/core/layout/src/lib/navigation.ts'), 'utf8');
  return [...new Set([...source.matchAll(/route: '([^']+)'/g)].map((match) => match[1]))];
}

async function signIn(browser) {
  const context = await browser.newContext({ viewport: VIEWPORTS.desktop });
  const page = await context.newPage();
  await page.goto(`${BASE}/sign-in`);
  await page.fill('input[formcontrolname=tenantCode]', process.env.UI_AUDIT_TENANT ?? 'acme');
  await page.fill('input[formcontrolname=email]', process.env.UI_AUDIT_EMAIL ?? 'admin@acme.example');
  await page.fill('input[formcontrolname=password]', process.env.UI_AUDIT_PASSWORD ?? 'Passw0rd!2026');
  await page.click('button[type=submit]');
  await page.waitForURL((url) => !url.pathname.startsWith('/sign-in'), { timeout: 30000 });
  const state = await context.storageState();
  await context.close();
  return state;
}

async function settle(page) {
  await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
  await page.waitForTimeout(900);
}

/** Layout and content checks that run inside the page. */
function inspect() {
  const findings = [];
  const add = (severity, rule, detail) => findings.push({ severity, rule, detail });
  const describe = (element) => {
    const classes = [...element.classList].slice(0, 2).join('.');
    const text = (element.innerText || element.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 40);
    return `${element.tagName.toLowerCase()}${classes ? '.' + classes : ''}${text ? ` "${text}"` : ''}`;
  };
  const visible = (element) => {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
  };
  const scrollsX = (element) => {
    for (let node = element.parentElement; node && node !== document.body; node = node.parentElement) {
      const overflow = getComputedStyle(node).overflowX;
      if (overflow === 'auto' || overflow === 'scroll' || overflow === 'hidden' || overflow === 'clip') return true;
    }
    return false;
  };

  const main = document.querySelector('#main-content') ?? document.body;
  const mainScroller = document.querySelector('.shell__main') ?? document.documentElement;
  const text = (main.innerText || '').trim();
  if (text.length < 15 && !main.querySelector('canvas, img, svg')) add('blocker', 'blank-page', 'The content area is empty.');

  if (document.documentElement.scrollWidth > innerWidth + 1) add('blocker', 'page-overflow-x', `Page is ${document.documentElement.scrollWidth}px wide in a ${innerWidth}px window.`);
  if (mainScroller.scrollWidth > mainScroller.clientWidth + 1) add('blocker', 'content-overflow-x', `Content is ${mainScroller.scrollWidth}px wide in ${mainScroller.clientWidth}px.`);

  const bounds = main.getBoundingClientRect();
  let overflowing = 0;
  for (const element of main.querySelectorAll('*')) {
    if (overflowing >= 5 || !visible(element)) continue;
    const rect = element.getBoundingClientRect();
    if ((rect.right > bounds.right + 2 || rect.left < bounds.left - 2) && !scrollsX(element)) {
      add('blocker', 'element-overflow', describe(element));
      overflowing++;
    }
  }

  let clipped = 0;
  for (const element of main.querySelectorAll('h1, h2, h3, th, label, button, .btn, .badge, .kpi__label, .field__label, a')) {
    if (clipped >= 6 || !visible(element)) continue;
    // Visually hidden on purpose (screen-reader-only labels) is not clipping.
    if (element.getBoundingClientRect().width <= 2 || element.closest('.sr-only')) continue;
    const style = getComputedStyle(element);
    if ((style.overflow === 'hidden' || style.textOverflow === 'ellipsis') && element.scrollWidth > element.clientWidth + 1) {
      add('major', 'clipped-text', describe(element));
      clipped++;
    }
  }

  let overlaps = 0;
  for (const container of main.querySelectorAll('*')) {
    if (overlaps >= 5) break;
    const display = getComputedStyle(container).display;
    if (!/flex|grid/.test(display) || container.children.length > 40) continue;
    const children = [...container.children].filter((child) => visible(child) && !/absolute|fixed/.test(getComputedStyle(child).position));
    for (let i = 0; i < children.length && overlaps < 5; i++) {
      for (let j = i + 1; j < children.length; j++) {
        const a = children[i].getBoundingClientRect();
        const b = children[j].getBoundingClientRect();
        const w = Math.min(a.right, b.right) - Math.max(a.left, b.left);
        const h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        if (w > 3 && h > 3) {
          add('major', 'overlap', `${describe(children[i])} overlaps ${describe(children[j])}`);
          overlaps++;
          break;
        }
      }
    }
  }

  const walker = document.createTreeWalker(main, NodeFilter.SHOW_TEXT);
  const raw = new Set();
  for (let node = walker.nextNode(); node && raw.size < 5; node = walker.nextNode()) {
    const value = node.nodeValue.trim();
    if (!value || node.parentElement.closest('code, pre, input, textarea, script, style')) continue;
    if (/\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)) raw.add(`ISO timestamp shown: "${value.slice(0, 40)}"`);
    else if (/\b(undefined|NaN)\b|\[object Object\]|^null$/.test(value)) raw.add(`Raw value shown: "${value.slice(0, 40)}"`);
  }
  raw.forEach((detail) => add('major', 'raw-value', detail));

  return { findings, links: [...document.querySelectorAll('a[href^="/"]')].map((link) => link.getAttribute('href')) };
}

async function axe(page) {
  try {
    const result = await new AxeBuilder({ page })
      .include('#main-content')
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
      .disableRules(['region'])
      .analyze();
    return result.violations.map((violation) => ({
      severity: violation.impact === 'critical' || violation.impact === 'serious' ? 'major' : 'minor',
      rule: `axe:${violation.id}`,
      detail: `${violation.help} (${violation.nodes.length}): ${violation.nodes.slice(0, 2).map((node) => node.target.join(' ')).join(' | ')}`,
    }));
  } catch (error) {
    return [{ severity: 'minor', rule: 'axe-error', detail: String(error).slice(0, 120) }];
  }
}

async function audit(page, pattern, variant, errors, label = variant) {
  const folder = join(out, slug(pattern));
  mkdirSync(folder, { recursive: true });
  const screenshot = join(folder, `${label}.png`);
  await page.screenshot({ path: screenshot, fullPage: false });
  const { findings, links } = await page.evaluate(inspect);
  const all = [...findings, ...(await axe(page)), ...errors.splice(0).map((detail) => ({ severity: 'blocker', rule: 'console-error', detail: detail.slice(0, 200) }))];
  return { links, findings: all.map((finding) => ({ ...finding, pattern, variant: label, screenshot })) };
}

/** Opens each page-header action that shows a dialog, inspects it, and closes it. */
async function auditDialogs(page, pattern, variant, errors) {
  const results = [];
  const buttons = page.locator('.page-header__actions button, .page-header__actions a.p-button:not([href])');
  const count = Math.min(await buttons.count(), 4);
  for (let index = 0; index < count; index++) {
    const button = buttons.nth(index);
    const name = ((await button.innerText().catch(() => '')) || '').trim();
    if (!DIALOG_TRIGGER.test(name) || !(await button.isVisible().catch(() => false))) continue;
    const before = page.url();
    await button.click().catch(() => {});
    await page.waitForTimeout(800);
    const dialog = page.locator('.p-dialog:visible, .p-drawer:visible').first();
    if (await dialog.count()) {
      const label = `${variant}-dialog-${slug(name).toLowerCase()}`;
      const { findings } = await audit(page, pattern, variant, errors, label);
      const box = await dialog.boundingBox();
      const viewport = page.viewportSize();
      const footer = page.locator('.p-dialog:visible .p-dialog-footer').first();
      if (box && (box.y < 0 || box.y + box.height > viewport.height + 1 || box.x < 0 || box.x + box.width > viewport.width + 1)) {
        findings.push({ severity: 'blocker', rule: 'dialog-off-screen', detail: `"${name}" dialog extends beyond the viewport.`, pattern, variant: label });
      }
      if ((await footer.count()) && !(await footer.isVisible())) {
        findings.push({ severity: 'major', rule: 'dialog-footer-hidden', detail: `"${name}" dialog footer is not visible.`, pattern, variant: label });
      }
      results.push(...findings);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(400);
    } else if (page.url() !== before) {
      await page.goBack().catch(() => {});
      await settle(page);
    }
  }
  return results;
}

/** List rows open records by click rather than by link: click the first row to discover the detail route. */
async function discoverRowTarget(page) {
  const row = page.locator('#main-content .p-datatable-tbody > tr:not(.p-datatable-emptymessage)').first();
  if (!(await row.count()) || !(await row.isVisible().catch(() => false))) return null;
  const before = page.url();
  await row.click({ position: { x: 12, y: 12 } }).catch(() => {});
  await page.waitForTimeout(1200);
  const after = page.url();
  if (after !== before) {
    await page.goBack().catch(() => {});
    await settle(page);
    return new URL(after).pathname + new URL(after).search;
  }
  return null;
}

/** Clicks through in-page tabs (role=tab) on detail pages. */
async function auditTabs(page, pattern, variant, errors) {
  const results = [];
  const tabs = page.locator('#main-content [role="tab"]:not([aria-disabled="true"]):not([disabled])');
  const count = Math.min(await tabs.count(), 16);
  for (let index = 1; index < count; index++) {
    const tab = tabs.nth(index);
    const name = ((await tab.innerText().catch(() => '')) || `tab${index}`).trim().split('\n')[0];
    if (!(await tab.isVisible().catch(() => false))) continue;
    await tab.click().catch(() => {});
    await settle(page);
    const { findings } = await audit(page, `${pattern}#${slug(name).toLowerCase()}`, variant, errors);
    results.push(...findings);
  }
  return results;
}

const browser = await chromium.launch();
const state = await signIn(browser);
const contexts = {};
for (const variant of VARIANTS) {
  contexts[variant] = await browser.newContext({ viewport: VIEWPORTS[variant], storageState: state, colorScheme: variant === 'dark' ? 'dark' : 'light' });
}

const queue = seeds().filter((route) => route.startsWith(ONLY));
const samples = new Map();
const findings = [];
const desktop = await contexts[VARIANTS[0]].newPage();
const errors = [];
desktop.on('console', (message) => message.type() === 'error' && errors.push(message.text()));
desktop.on('pageerror', (error) => errors.push(`PAGEERROR ${error.message}`));

// Pass 1: discover and audit at the first variant (desktop), with tabs and dialogs.
while (queue.length && samples.size < MAX) {
  const url = queue.shift();
  const pattern = patternOf(url);
  if (samples.has(pattern) || /^\/(bff|api)\b/.test(url)) continue;
  samples.set(pattern, url);
  try {
    await desktop.goto(BASE + url);
    await settle(desktop);
    if (new URL(desktop.url()).pathname.startsWith('/sign-in')) throw new Error('Signed out');
    const result = await audit(desktop, pattern, VARIANTS[0], errors);
    findings.push(...result.findings);
    findings.push(...(await auditTabs(desktop, pattern, VARIANTS[0], errors)));
    await desktop.goto(BASE + url);
    await settle(desktop);
    const rowTarget = await discoverRowTarget(desktop);
    if (rowTarget) result.links.push(rowTarget);
    findings.push(...(await auditDialogs(desktop, pattern, VARIANTS[0], errors)));
    for (const link of result.links) {
      if (link.startsWith(ONLY) && !samples.has(patternOf(link)) && !/^\/(bff|api)\b/.test(link)) queue.push(link);
    }
    console.log(`[${samples.size}] ${pattern} (${result.findings.length})`);
  } catch (error) {
    findings.push({ severity: 'blocker', rule: 'navigation-failed', detail: String(error).slice(0, 160), pattern, variant: VARIANTS[0] });
  }
}

// Pass 2: the other variants for every sampled page (and its dialogs on mobile).
for (const variant of VARIANTS.slice(1)) {
  const page = await contexts[variant].newPage();
  const variantErrors = [];
  page.on('pageerror', (error) => variantErrors.push(`PAGEERROR ${error.message}`));
  if (variant === 'dark') await page.addInitScript(() => document.addEventListener('DOMContentLoaded', () => document.documentElement.classList.add('gggs-dark')));
  for (const [pattern, url] of samples) {
    try {
      await page.goto(BASE + url);
      await settle(page);
      if (variant === 'dark') await page.evaluate(() => document.documentElement.classList.add('gggs-dark'));
      await page.waitForTimeout(300);
      findings.push(...(await audit(page, pattern, variant, variantErrors)).findings);
      if (variant === 'mobile') findings.push(...(await auditDialogs(page, pattern, variant, variantErrors)));
    } catch (error) {
      findings.push({ severity: 'blocker', rule: 'navigation-failed', detail: String(error).slice(0, 160), pattern, variant });
    }
  }
  console.log(`${variant}: ${samples.size} pages`);
}
await browser.close();

// Report.
const order = { blocker: 0, major: 1, minor: 2 };
findings.sort((a, b) => order[a.severity] - order[b.severity] || a.pattern.localeCompare(b.pattern));
writeFileSync(join(out, 'report.json'), JSON.stringify({ pages: [...samples.keys()], findings }, null, 2));
const counts = Object.fromEntries(Object.keys(order).map((severity) => [severity, findings.filter((finding) => finding.severity === severity).length]));
const lines = [
  `# UI audit - ${new Date().toISOString()}`,
  '',
  `${samples.size} route patterns, variants ${VARIANTS.join(', ')}. Blocker ${counts.blocker}, major ${counts.major}, minor ${counts.minor}.`,
  '',
  '## By rule',
  '',
  ...Object.entries(findings.reduce((acc, finding) => ({ ...acc, [`${finding.severity} ${finding.rule}`]: (acc[`${finding.severity} ${finding.rule}`] ?? 0) + 1 }), {})).map(([rule, n]) => `- ${rule}: ${n}`),
];
for (const severity of Object.keys(order)) {
  lines.push('', `## ${severity}`, '');
  for (const finding of findings.filter((item) => item.severity === severity)) {
    lines.push(`- \`${finding.pattern}\` [${finding.variant}] **${finding.rule}** - ${finding.detail}`);
  }
}
writeFileSync(join(out, 'report.md'), lines.join('\n'));
console.log(`Done: ${samples.size} patterns; blocker ${counts.blocker}, major ${counts.major}, minor ${counts.minor}. Report: ${join(out, 'report.md')}`);
