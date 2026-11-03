import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LEGAL, type LegalSection } from './legal-details';
import { LegalPageLayout } from './legal-page-layout';

/*
 * Written from what the app really does, so it must change when the app does: a new outside service,
 * a new cookie or a new kind of personal data needs a line here.
 */
const SECTIONS: readonly LegalSection[] = [
  {
    title: 'Who we are',
    paragraphs: [
      `Nuts and Bolts is a work application for human resources, payroll, projects and related tasks. It is provided by ${LEGAL.company}, registered at ${LEGAL.registeredAddress} ("GGGS", "we" or "us"). Our website is ${LEGAL.website}.`,
      'This policy explains what personal data is held in Nuts and Bolts, how it is used and protected, and what rights you have.',
    ],
  },
  {
    title: 'Who is responsible for your data',
    paragraphs: ['Your role with us decides who to contact.'],
    items: [
      'If you use Nuts and Bolts through your employer, or you applied for a job there, your employer decides what data is held and why. Your employer is responsible for it (the "Data Fiduciary" under India\'s Digital Personal Data Protection Act, 2023), and we process it on their instructions as their service provider (the "Data Processor"). Their own privacy notice also applies to you.',
      'We are responsible for the small amount of data we use to run our own business with a customer, such as the names and contact details of the people we deal with for contracts, billing and support.',
    ],
  },
  {
    title: 'What personal data is held',
    paragraphs: ['Your employer chooses which of these to use. Depending on that, Nuts and Bolts may hold:'],
    items: [
      'Identity and contact details: name, photo, date of birth, gender, marital status, nationality, blood group, work and personal email, phone numbers and addresses.',
      'Family and emergency contacts, which may include details of your children or other dependants.',
      'Work details: employee code, department, job, manager, location, employment history, education and past experience.',
      'Government and bank details: PAN, UAN, ESIC number, national ID and bank account details.',
      'Pay: salary, payslips, loans, expenses, benefits, tax and statutory records.',
      'Time: attendance, leave, timesheets, and the IP address recorded with an attendance punch.',
      'Performance and development: goals, reviews, feedback, learning, recognition and promotions.',
      'Documents you or your employer upload, and the time and IP address recorded when you sign one.',
      'If you applied for a job: your application, CV, interview notes and assessments.',
      'Questions you ask an AI assistant and its answers, if your employer turns the assistants on.',
      'Account and activity records: your sign-in details and a log of who did what and when.',
    ],
  },
  {
    title: 'How the data is used',
    paragraphs: ['We use this data only to provide the service to your employer:'],
    items: [
      'to run the features your employer uses, such as payroll, leave and approvals;',
      'to send you notifications by email, in the app or by text message, as your employer has set up and your preferences allow;',
      'to keep the service secure, and to investigate faults and misuse;',
      'to meet our legal obligations.',
    ],
    after: ['We do not sell personal data, and we do not use it for advertising or marketing.'],
  },
  {
    title: 'Outside services',
    paragraphs: ['Some features send data to another company. Most only do so if your employer has turned them on:'],
    items: [
      'AI assistants use the Anthropic API. What you ask, and the records needed to answer you, are sent to it.',
      'Email and text messages may go through services your employer connects: its own mail server, SendGrid, Microsoft 365, Twilio, MSG91 or WhatsApp.',
      'Messages may be posted to Slack or Microsoft Teams, or to another system by a webhook, if your employer sets up an automation.',
      'Your employer can have work accounts created for you in Microsoft 365 or Google Workspace, and can let you sign in through its own single sign-on provider.',
      'We use infrastructure providers to host the service and store files, and they are bound by contract to protect the data.',
    ],
    after: ['Your employer can also connect its own systems to Nuts and Bolts using API keys it controls.'],
  },
  {
    title: 'Cookies and browser storage',
    paragraphs: ['We use only what the service needs in order to work. There are no advertising or analytics cookies.'],
    items: [
      'A session cookie keeps you signed in. It cannot be read by scripts on the page, and it stops working after 30 minutes without activity or 12 hours at most, unless your employer has set other limits.',
      'A short-lived cookie is used while you sign in through single sign-on.',
      'Your browser stores your own preferences on your device: light or dark theme, colours, menu style and your organisation code.',
    ],
  },
  {
    title: 'How the data is protected',
    items: [
      'Government identity numbers, bank account numbers and the secrets used to connect outside services are encrypted when stored.',
      'Passwords are stored only in a form that cannot be turned back into the password. A temporary password or password reset link is never stored in a usable form in our email records.',
      'Each organisation\'s data is kept separate from every other organisation\'s, and this is enforced in the database itself.',
      'People see only what their role allows, and changes are recorded in an audit log.',
    ],
    after: ['No system connected to the internet can be guaranteed completely secure. Please keep your password private, and tell your employer straight away if you think someone else has used your account.'],
  },
  {
    title: 'How long data is kept',
    paragraphs: [
      'Your employer decides how long employee records are kept, based on its contract with us and the law it must follow. AI assistant conversations are deleted after 30 days without activity by default.',
      'When a customer stops using the service, its data is returned or deleted as its contract with us says, unless the law requires us to keep it.',
    ],
  },
  {
    title: 'Where data is stored',
    paragraphs: [
      `Data is hosted in ${LEGAL.hostingLocation}. Some outside services in section 5 may process data in other countries. We transfer personal data out of India only as the law allows, and not to any country the Government of India has restricted.`,
    ],
  },
  {
    title: 'Your rights',
    paragraphs: ['If you are in India, the Digital Personal Data Protection Act, 2023 gives you the right to:'],
    items: [
      'get a summary of your personal data and how it is processed, and know who it has been shared with;',
      'have it corrected, completed, updated or erased, unless it must be kept for a stated purpose or by law;',
      'withdraw your consent where processing relies on it, without affecting what was done before;',
      'have a complaint dealt with;',
      'nominate someone to use these rights for you if you die or cannot act.',
    ],
    after: [
      'You can see and correct many of your own details under My profile. For employee data, please go to your employer first, because they are responsible for it. We will help them respond.',
      `For data we are responsible for ourselves, write to ${LEGAL.privacyEmail}. People in other countries may have similar rights under their local law.`,
    ],
  },
  {
    title: 'Children',
    paragraphs: [
      'Nuts and Bolts is for adults at work and is not meant for children to use. Children\'s details may appear only as family or dependant details entered by a parent or guardian who works for one of our customers. That customer is responsible for that data, including any consent the law requires.',
    ],
  },
  {
    title: 'If something goes wrong',
    paragraphs: [
      'If we learn of a personal data breach, we will tell the affected customer without delay and help them meet their duties, including telling the people affected and the Data Protection Board of India. For data we are responsible for ourselves, we will do this directly, as the law requires.',
    ],
  },
  {
    title: 'Questions and complaints',
    paragraphs: [
      `For questions about this policy, write to ${LEGAL.privacyEmail}.`,
      `Our Grievance Officer is ${LEGAL.grievanceOfficer}, at ${LEGAL.grievanceEmail}. If you are not satisfied with how a complaint was handled, you may approach the Data Protection Board of India.`,
      `By post: ${LEGAL.company}, ${LEGAL.registeredAddress}.`,
    ],
  },
  {
    title: 'Changes to this policy',
    paragraphs: ['We may update this policy as the service or the law changes. We will change the date at the top of this page, and tell customers before any significant change takes effect.'],
  },
];

@Component({
  selector: 'gggs-privacy-page',
  imports: [LegalPageLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<gggs-legal-page-layout heading="Privacy Policy" [sections]="sections" />`,
})
export class PrivacyPage {
  protected readonly sections = SECTIONS;
}
