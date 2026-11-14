import { describe, expect, it } from 'vitest';
import type { NavItem } from './navigation';
import { recentLinks } from './recent-pages';

const MENU: readonly NavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-home', route: '/dashboard' },
  { label: 'Calendar', icon: 'pi pi-calendar', route: '/calendar' },
  {
    label: 'Finance',
    icon: 'pi pi-wallet',
    children: [
      { label: 'Payroll overview', icon: 'pi pi-chart-bar', route: '/payroll' },
      { label: 'Payroll runs', icon: 'pi pi-play', route: '/payroll/runs' },
      { label: 'Salaries', icon: 'pi pi-money-bill', route: '/payroll/salaries' },
    ],
  },
  {
    label: 'My Space',
    icon: 'pi pi-user',
    children: [
      { label: 'My profile', icon: 'pi pi-id-card', route: '/me/profile' },
      { label: 'My attendance', icon: 'pi pi-clock', route: '/me/attendance' },
      { label: 'My leave', icon: 'pi pi-calendar-plus', route: '/me/leave' },
      { label: 'My payslips', icon: 'pi pi-file', route: '/me/payslips' },
    ],
  },
];

describe('recentLinks', () => {
  it('keeps the order saved, newest first', () => {
    expect(recentLinks(['/me/leave', '/calendar', '/payroll/runs'], MENU).map((link) => link.label)).toEqual(['My leave', 'Calendar', 'Payroll runs']);
  });

  it('takes the name and icon from the menu', () => {
    expect(recentLinks(['/payroll/runs'], MENU)).toEqual([{ route: '/payroll/runs', label: 'Payroll runs', icon: 'pi pi-play' }]);
  });

  it('shows at most six', () => {
    const saved = ['/calendar', '/payroll', '/payroll/runs', '/payroll/salaries', '/me/profile', '/me/attendance', '/me/leave', '/me/payslips'];
    const links = recentLinks(saved, MENU);
    expect(links).toHaveLength(6);
    expect(links.at(-1)?.route).toBe('/me/attendance');
  });

  it('drops a page the person can no longer see, and fills the row from older ones', () => {
    const withoutPayroll = MENU.filter((group) => group.label !== 'Finance');
    expect(recentLinks(['/payroll/runs', '/calendar', '/me/leave'], withoutPayroll).map((link) => link.route)).toEqual(['/calendar', '/me/leave']);
  });

  it('does not turn a page that left the menu into its parent', () => {
    expect(recentLinks(['/payroll/old-report'], MENU)).toEqual([]);
  });

  it('never lists the home screen', () => {
    expect(recentLinks(['/dashboard', '/calendar'], MENU).map((link) => link.route)).toEqual(['/calendar']);
  });

  it('lists a page once', () => {
    expect(recentLinks(['/calendar', '/calendar'], MENU)).toHaveLength(1);
  });

  it('is empty with no history', () => {
    expect(recentLinks([], MENU)).toEqual([]);
  });
});
