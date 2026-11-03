import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LEGAL, type LegalSection } from './legal-details';
import { LegalPageLayout } from './legal-page-layout';

const SECTIONS: readonly LegalSection[] = [
  {
    title: 'About these terms',
    paragraphs: [
      `Nuts and Bolts ("the service") is owned and operated by ${LEGAL.company}, a company incorporated in India ("GGGS", "we" or "us"). These terms are an agreement between us and you, the person using the service.`,
      'By signing in or using the service, you confirm that you are 18 or older and that you accept these terms and our Privacy Policy. If your organisation has a contract with us, that contract applies to the organisation and takes priority over these terms if they disagree.',
    ],
  },
  {
    title: 'Using the service',
    paragraphs: [
      'You may use the service only as your organisation allows and our contract with it permits. We give you a limited, personal right to use it, which you cannot transfer and which we can withdraw as these terms describe.',
      'The service and everything in it, other than your organisation\'s own data, belong to us or our licensors. You may not copy, sell, or try to take apart or rebuild any part of it.',
    ],
  },
  {
    title: 'Your account',
    paragraphs: ['You are responsible for what happens under your account. Please:'],
    items: [
      'keep your password private, and do not share your account;',
      'use a device you trust, and sign out when you finish on a shared one;',
      'tell your administrator straight away if you think someone else has used your account.',
    ],
  },
  {
    title: 'What you must not do',
    paragraphs: ['You must use the service only for lawful purposes. You must not:'],
    items: [
      'look at, or try to look at, data you have not been given access to;',
      'upload files containing viruses or anything else designed to cause damage;',
      'interfere with the service, its servers or networks, or with other people\'s use of it;',
      'upload anything that you have no right to share, or that is unlawful;',
      'use the service to break any law, or encourage anyone else to.',
    ],
  },
  {
    title: 'Security testing',
    paragraphs: [
      'You must not carry out security testing on the live service, including vulnerability scans, penetration tests and denial-of-service tests, directly or through anyone else, without our written permission. If you think you have found a security problem, please tell us and do not test it further.',
    ],
  },
  {
    title: 'AI assistants',
    paragraphs: [
      'If your organisation turns them on, AI assistants can help you find information and draft work. Their answers can be wrong, so check anything important before you rely on it, especially on pay, legal or employment matters. Decisions about people remain the responsibility of the people making them.',
    ],
  },
  {
    title: 'Changes and suspension',
    paragraphs: [
      'We may improve or change the service, and will tell customers about changes that significantly affect them, as their contract provides.',
      'We may suspend or end your access if you break these terms, or if we need to in order to protect the service or other people. Your organisation can also change or end your access at any time.',
    ],
  },
  {
    title: 'Responsibility',
    paragraphs: [
      'The service is provided on the terms of our contract with your organisation. As an individual user, your rights and any claims about the service are through your organisation, except where the law gives you rights that cannot be excluded. Payroll, tax and statutory figures should be checked by your organisation before they are relied on.',
    ],
  },
  {
    title: 'Privacy',
    paragraphs: ['Our Privacy Policy explains how personal data is handled. It forms part of these terms.'],
  },
  {
    title: 'Changes to these terms',
    paragraphs: ['We may update these terms from time to time, and will change the date at the top of this page. If you keep using the service after a change takes effect, the new terms apply to you.'],
  },
  {
    title: 'Governing law',
    paragraphs: [`These terms are governed by the laws of India. The courts in ${LEGAL.courtCity}, India have exclusive jurisdiction over any dispute about them.`],
  },
  {
    title: 'Contact',
    paragraphs: [`${LEGAL.company}, ${LEGAL.registeredAddress}. Email: ${LEGAL.privacyEmail}.`],
  },
];

@Component({
  selector: 'gggs-terms-page',
  imports: [LegalPageLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<gggs-legal-page-layout heading="Terms of Use" [sections]="sections" />`,
})
export class TermsPage {
  protected readonly sections = SECTIONS;
}
