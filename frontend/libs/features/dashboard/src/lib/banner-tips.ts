import type { MyFeatureBanner } from '@gggs/shared/models';

/** A built-in slide, and what a person needs for it to apply to them. */
export interface BannerTip extends MyFeatureBanner {
  readonly permission?: string;
  readonly feature?: string;
}

const tip = (slide: Partial<MyFeatureBanner> & Pick<MyFeatureBanner, 'id' | 'title' | 'body' | 'kind' | 'chip'>, permission?: string, feature?: string): BannerTip => ({
  actionLabel: null,
  actionLink: null,
  secondaryLabel: null,
  secondaryLink: null,
  endsOn: null,
  ...slide,
  permission,
  feature,
});

/**
 * What the banner shows until HR publishes slides of its own, so a new organisation's dashboard
 * still introduces the product. These stay true for anyone they are shown to: each says what the
 * app does, never something about the person ("your payslip is ready"), and is left out for someone
 * without the permission or plan feature behind it. The first HR slide replaces them all.
 *
 * The photos in `public/banners` are placeholders from Unsplash (free licence, through picsum.photos),
 * there so the banner can be reviewed with real pictures. Replace them with the company's own before
 * going live.
 */
export const BANNER_TIPS: readonly BannerTip[] = [
  tip({ id: 'tip-leave', imageUrl: '/banners/time-off.jpg', kind: 'Holiday', chip: 'Time off', title: 'Planning some time off?', body: 'See your balances and holidays, then apply for leave from one page.', actionLabel: 'Apply for leave', actionLink: '/me/leave', secondaryLabel: 'Open the calendar', secondaryLink: '/calendar' }, 'self.leave.apply', 'Time'),
  tip({ id: 'tip-payslips', imageUrl: '/banners/payslips.jpg', kind: 'Payroll', chip: 'Payroll', title: 'Your payslips are all in one place', body: 'See each payslip, and the tax deducted this year.', actionLabel: 'My payslips', actionLink: '/me/payslips' }, 'self.payslip.view', 'Payroll'),
  tip({ id: 'tip-recognition', imageUrl: '/banners/recognition.jpg', kind: 'Recognition', chip: 'Recognition', title: 'Who made your week easier?', body: 'Send a colleague a thank-you from My rewards.', actionLabel: 'Recognise a colleague', actionLink: '/me/rewards' }, 'self.recognition.give', 'Rewards'),
  tip({ id: 'tip-attendance', imageUrl: '/banners/attendance.jpg', kind: 'Deadline', chip: 'Attendance', title: 'Clock in right from this page', body: 'Use the "Let\'s get to work" card. It keeps count of your day while you are clocked in.', actionLabel: 'My attendance', actionLink: '/me/attendance' }, 'self.attendance.punch', 'Time'),
  tip({ id: 'tip-helpdesk', imageUrl: '/banners/helpdesk.jpg', kind: 'Announcement', chip: 'Helpdesk', title: 'Stuck on something?', body: 'Ask IT, HR, payroll or facilities for help, and follow your ticket from one page.', actionLabel: 'Ask the helpdesk', actionLink: '/me/tickets' }, 'self.ticket.create', 'Helpdesk'),
  tip({ id: 'tip-menu', imageUrl: '/banners/whats-new.jpg', kind: 'WhatsNew', chip: 'New in Nuts and Bolts', title: 'The menu now floats over your page', body: 'Open it from the tab on the rail. Your page stays where it was, and the menu closes once you pick a page.' }),
];
