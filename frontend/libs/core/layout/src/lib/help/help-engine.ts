import { isUnder, type NavItem } from '../navigation';
import type { HelpAudience, HelpContent, HelpGuide, HelpQuestion, HelpTextPart, HelpTopic } from './help-model';

/*
 * The Help & Wiki's rules, kept pure so they are testable without a browser: which help pages
 * someone gets (exactly the screens in their menu, plus the general pages), which page belongs to
 * the screen they are on, search, and how the assistant picks an answer.
 */

/** The id of a screen's help page: `/me/leave` → `me.leave`. */
export function topicIdFor(route: string): string {
  return route.replace(/^\/+/, '').replace(/\//g, '.').toLowerCase();
}

/** Splits guide text on [[...]] into plain words and names on the screen. */
export function helpText(text: string): HelpTextPart[] {
  const parts: HelpTextPart[] = [];
  const pattern = /\[\[(.+?)\]\]/g;
  let last = 0;
  for (let match = pattern.exec(text); match; match = pattern.exec(text)) {
    if (match.index > last) {
      parts.push({ text: text.slice(last, match.index), ui: false });
    }
    parts.push({ text: match[1], ui: true });
    last = match.index + match[0].length;
  }
  if (last < text.length) {
    parts.push({ text: text.slice(last), ui: false });
  }
  return parts;
}

/** Guide text with the [[ ]] markers removed, for search and plain display. */
export function plainText(text: string): string {
  return text.replace(/\[\[(.+?)\]\]/g, '$1');
}

/** Who a screen is mainly for, from where it sits. */
export function audienceFor(route: string, group: string): HelpAudience {
  if (route === '/dashboard' || route === '/calendar' || route === '/help') {
    return 'everyone';
  }
  if (route.startsWith('/me/') || route === '/me') {
    return 'employee';
  }
  if (route === '/approvals' || route.startsWith('/team') || group === 'My Team') {
    return 'manager';
  }
  return 'admin';
}

/**
 * The help pages for a menu: one per screen in it (so nobody reads about a screen they cannot
 * open), in menu order, after the general pages.
 */
export function buildTopics(navigation: readonly NavItem[], content: HelpContent): HelpTopic[] {
  const topics: HelpTopic[] = content.general.map((page) => ({
    id: page.id,
    title: page.title,
    group: 'Getting started',
    audience: 'everyone',
    summary: page.summary,
    guide: page.guide,
  }));
  const seen = new Set<string>();

  const add = (item: NavItem, group: string, path: string): void => {
    if (!item.route || seen.has(item.route)) {
      return;
    }
    seen.add(item.route);
    const guide = content.guides[item.route];
    topics.push({
      id: topicIdFor(item.route),
      title: item.label,
      group,
      route: item.route,
      path,
      audience: audienceFor(item.route, group),
      summary: content.summaries[item.route] ?? guide?.lead ?? `The ${item.label} screen.`,
      guide,
    });
  };

  for (const group of navigation) {
    if (group.children?.length) {
      // A menu with pages can also be a page itself (Settings has its home page).
      add(group, group.label, group.label);
      for (const page of group.children) {
        add(page, group.label, `${group.label} › ${page.label}`);
      }
    } else {
      add(group, group.label, group.label);
    }
  }

  return topics;
}

/** The help page for the screen at `url`: the longest matching route wins (`/payroll/runs/7` → Payroll runs). */
export function topicForUrl(url: string, topics: readonly HelpTopic[]): HelpTopic | undefined {
  let best: HelpTopic | undefined;
  for (const topic of topics) {
    if (topic.route && isUnder(url, topic.route) && (!best?.route || topic.route.length > best.route.length)) {
      best = topic;
    }
  }
  return best;
}

/** Lower-case words of at least two letters, without punctuation. */
export function words(text: string): string[] {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, ' ')
    .split(/\s+/)
    .filter((word) => word.length > 1);
}

const STOP_WORDS = new Set(['how', 'do', 'does', 'can', 'the', 'my', 'is', 'to', 'an', 'of', 'in', 'on', 'for', 'and', 'what', 'where', 'when', 'who', 'why', 'it', 'me', 'you', 'we', 'our', 'this', 'that', 'be', 'are', 'with', 'from', 'get', 'want', 'need', 'please', 'there']);

function guideText(guide: HelpGuide | undefined): string {
  if (!guide) {
    return '';
  }
  return [guide.lead, ...(guide.before ?? []), ...guide.steps, guide.tip ?? '', ...(guide.terms ?? []).flat(), ...(guide.faqs ?? []).flat()].map(plainText).join(' ');
}

/**
 * Help pages matching a search, best first. Title words count most, then extra keywords and the
 * summary, then the guide text; every word typed must appear somewhere.
 */
export function searchTopics(query: string, topics: readonly HelpTopic[]): HelpTopic[] {
  const terms = words(query).filter((word) => !STOP_WORDS.has(word));
  if (!terms.length) {
    return [...topics];
  }

  const scored: { topic: HelpTopic; score: number }[] = [];
  for (const topic of topics) {
    const title = topic.title.toLowerCase();
    const keywords = (topic.guide?.keywords ?? []).join(' ').toLowerCase();
    const summary = `${topic.summary} ${topic.path ?? ''}`.toLowerCase();
    const body = guideText(topic.guide).toLowerCase();
    let score = 0;
    let all = true;
    for (const term of terms) {
      const stem = term.length > 4 ? term.replace(/s$/, '') : term;
      const inTitle = title.includes(stem);
      const inKeywords = keywords.includes(stem);
      const inSummary = summary.includes(stem);
      const inBody = body.includes(stem);
      if (!inTitle && !inKeywords && !inSummary && !inBody) {
        all = false;
        break;
      }
      score += (inTitle ? 6 : 0) + (inKeywords ? 4 : 0) + (inSummary ? 2 : 0) + (inBody ? 1 : 0);
    }
    if (all) {
      // The whole phrase in the title or keywords ("apply leave") beats its words scattered about.
      const phrase = terms.join(' ');
      const phraseBonus = terms.length > 1 && (title.includes(phrase) || keywords.includes(phrase)) ? 10 : 0;
      scored.push({ topic, score: score + phraseBonus + (topic.guide ? 1 : 0) });
    }
  }

  return scored.sort((a, b) => b.score - a.score).map((entry) => entry.topic);
}

/** How strongly a question's keys match what was typed. A phrase counts more than a single word. */
export function questionScore(question: HelpQuestion, typed: string): number {
  const text = ` ${words(typed).join(' ')} `;
  let score = 0;
  for (const key of question.keys) {
    const phrase = ` ${words(key).join(' ')} `;
    if (text.includes(phrase)) {
      score += key.includes(' ') ? 3 : 2;
    }
  }
  const typedWords = new Set(words(typed).filter((word) => !STOP_WORDS.has(word)));
  for (const word of words(question.question)) {
    if (!STOP_WORDS.has(word) && typedWords.has(word)) {
      score += 1;
    }
  }
  return score;
}

/** What the assistant answers with: a ready answer, or the help page that best fits, or nothing. */
export type HelpAnswer =
  | { readonly kind: 'question'; readonly question: HelpQuestion; readonly topic: HelpTopic | undefined }
  | { readonly kind: 'topic'; readonly topic: HelpTopic }
  | { readonly kind: 'none' };

/**
 * Picks an answer among the ready questions whose page this person can see, then among their help
 * pages. Needs a clear match: a vague answer is worse than "I don't know, ask a person".
 */
export function answerFor(typed: string, questions: readonly HelpQuestion[], topics: readonly HelpTopic[]): HelpAnswer {
  const byId = new Map(topics.map((topic) => [topic.route ?? topic.id, topic]));
  let best: HelpQuestion | undefined;
  let bestScore = 0;
  for (const question of questions) {
    if (!byId.has(question.topic)) {
      continue;
    }
    const score = questionScore(question, typed);
    if (score > bestScore) {
      best = question;
      bestScore = score;
    }
  }
  if (best && bestScore >= 2) {
    return { kind: 'question', question: best, topic: byId.get(best.topic) };
  }

  const pages = searchTopics(typed, topics);
  if (pages.length && pages.length < topics.length && words(typed).some((word) => !STOP_WORDS.has(word))) {
    return { kind: 'topic', topic: pages[0] };
  }
  return { kind: 'none' };
}

/** The ready questions this person can be offered: those about a page they can see. */
export function questionsFor(questions: readonly HelpQuestion[], topics: readonly HelpTopic[]): HelpQuestion[] {
  const ids = new Set(topics.map((topic) => topic.route ?? topic.id));
  return questions.filter((question) => ids.has(question.topic));
}

/** Finds a topic by its id, its route or a general page id. */
export function findTopic(key: string | null | undefined, topics: readonly HelpTopic[]): HelpTopic | undefined {
  if (!key) {
    return undefined;
  }
  return topics.find((topic) => topic.id === key || topic.route === key);
}
