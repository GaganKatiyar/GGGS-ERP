import { describe, expect, it } from 'vitest';
import { NAVIGATION, filterNavigation, isBuilt, type NavItem } from '../navigation';
import { HELP_CONTENT } from './help-content';
import { answerFor, buildTopics, findTopic, helpText, questionsFor, searchTopics, topicForUrl, topicIdFor } from './help-engine';

const routesOf = (items: readonly NavItem[]): string[] => items.flatMap((item) => [...(item.route ? [item.route] : []), ...routesOf(item.children ?? [])]);
const everything = filterNavigation(NAVIGATION, isBuilt);
const allTopics = buildTopics(everything, HELP_CONTENT);

/** What someone with only self-service permissions sees. */
const employeeNav = filterNavigation(NAVIGATION, (item) => isBuilt(item) && (item.permissions === undefined || item.permissions.some((code) => code.startsWith('self.'))));
const employeeTopics = buildTopics(employeeNav, HELP_CONTENT);

describe('help content', () => {
  it('describes every screen in the menu in plain words', () => {
    const missing = routesOf(everything).filter((route) => !HELP_CONTENT.summaries[route]);
    expect(missing).toEqual([]);
  });

  it('only has guides and descriptions for screens that exist', () => {
    const routes = new Set(routesOf(everything));
    expect(Object.keys(HELP_CONTENT.guides).filter((route) => !routes.has(route))).toEqual([]);
    expect(Object.keys(HELP_CONTENT.summaries).filter((route) => !routes.has(route))).toEqual([]);
  });

  it('points every related link, ready question and popular task at a real help page', () => {
    const known = (key: string) => !!findTopic(key, allTopics);
    const related = [...Object.values(HELP_CONTENT.guides), ...HELP_CONTENT.general.map((page) => page.guide)].flatMap((guide) => guide.related ?? []);
    expect(related.filter((key) => !known(key))).toEqual([]);
    expect(HELP_CONTENT.questions.map((question) => question.topic).filter((key) => !known(key))).toEqual([]);
    expect(HELP_CONTENT.popular.map(([key]) => key).filter((key) => !known(key))).toEqual([]);
  });

  it('closes every [[ ]] it opens', () => {
    const texts = [...Object.values(HELP_CONTENT.guides), ...HELP_CONTENT.general.map((page) => page.guide)].flatMap((guide) => [guide.lead, ...guide.steps, ...(guide.before ?? []), guide.tip ?? '']);
    const broken = [...texts, ...HELP_CONTENT.questions.flatMap((question) => question.answer)].filter((text) => (text.match(/\[\[/g) ?? []).length !== (text.match(/\]\]/g) ?? []).length);
    expect(broken).toEqual([]);
  });
});

describe('help pages', () => {
  it('gives a screen an id made from its route', () => {
    expect(topicIdFor('/me/leave')).toBe('me.leave');
    expect(topicIdFor('/payroll/runs')).toBe('payroll.runs');
  });

  it('puts the general pages first, then one page per screen', () => {
    expect(allTopics[0].id).toBe('getting-started');
    expect(allTopics.filter((topic) => topic.route).length).toBe(routesOf(everything).length);
  });

  it('only gives people help for the screens in their menu', () => {
    const routes = employeeTopics.map((topic) => topic.route).filter(Boolean);
    expect(routes).toContain('/me/leave');
    expect(routes).not.toContain('/payroll/runs');
    expect(routes).toContain('/help');
  });

  it('finds the help page for a screen, preferring the longest route', () => {
    expect(topicForUrl('/payroll/runs/42?tab=people', allTopics)?.route).toBe('/payroll/runs');
    expect(topicForUrl('/payroll', allTopics)?.route).toBe('/payroll');
    expect(topicForUrl('/somewhere-else', allTopics)).toBeUndefined();
  });

  it('marks the names of things on the screen', () => {
    expect(helpText('Press [[Apply for leave]] now.')).toEqual([
      { text: 'Press ', ui: false },
      { text: 'Apply for leave', ui: true },
      { text: ' now.', ui: false },
    ]);
  });
});

describe('help search', () => {
  it('puts the screen whose name matches first', () => {
    expect(searchTopics('apply leave', allTopics)[0].route).toBe('/me/leave');
    expect(searchTopics('payslip', employeeTopics)[0].route).toBe('/me/payslips');
  });

  it('finds a screen by the words people use for it', () => {
    expect(searchTopics('holiday', employeeTopics).map((topic) => topic.route)).toContain('/me/leave');
  });

  it('returns nothing for words that appear nowhere', () => {
    expect(searchTopics('zebra crossing', allTopics)).toEqual([]);
  });
});

describe('the help assistant', () => {
  it('answers a question asked in other words', () => {
    const answer = answerFor('where can i download my salary slip', HELP_CONTENT.questions, employeeTopics);
    expect(answer.kind).toBe('question');
    expect(answer.kind === 'question' && answer.question.topic).toBe('/me/payslips');
  });

  it('never answers about a screen the person cannot open', () => {
    expect(questionsFor(HELP_CONTENT.questions, employeeTopics).map((question) => question.topic)).not.toContain('/payroll/runs');
    const answer = answerFor('how do I run payroll', HELP_CONTENT.questions, employeeTopics);
    expect(answer.kind === 'question' && answer.question.topic === '/payroll/runs').toBe(false);
  });

  it('says it does not know rather than guess', () => {
    expect(answerFor('book a meeting room for friday', HELP_CONTENT.questions, employeeTopics).kind).toBe('none');
  });

  it('flags answers that also depend on the company\'s own rules', () => {
    const answer = answerFor('how many leave days do I get', HELP_CONTENT.questions, employeeTopics);
    expect(answer.kind === 'question' && answer.question.dependsOnPolicy).toBe(true);
  });
});
