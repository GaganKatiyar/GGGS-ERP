/*
 * The Help & Wiki's shapes.
 *
 * Text in a guide may mark the name of something on the screen - a button, a field, a menu item - as
 * [[Apply for leave]]; it is shown in a highlighted chip so people can match it to what they see.
 */

/** Who a help page is mainly for. Used to filter the Help & Wiki; it never hides a page. */
export type HelpAudience = 'everyone' | 'employee' | 'manager' | 'admin';

/** The full guide to one screen. */
export interface HelpGuide {
  /** What the screen is for, in a sentence or two. */
  readonly lead: string;
  /** What to have ready, or check, before starting. */
  readonly before?: readonly string[];
  /** The usual task on this screen, one step at a time. */
  readonly steps: readonly string[];
  readonly tip?: string;
  /** Words on the screen and what they mean. */
  readonly terms?: readonly (readonly [string, string])[];
  /** Common questions and their answers. */
  readonly faqs?: readonly (readonly [string, string])[];
  /** Other help pages (routes such as `/me/leave`, or general page ids). */
  readonly related?: readonly string[];
  /** Extra words people might search with (for example "holiday" for leave). */
  readonly keywords?: readonly string[];
}

/** A page of help that is not about one screen (getting started, using help). */
export interface GeneralHelpPage {
  readonly id: string;
  readonly title: string;
  readonly summary: string;
  readonly guide: HelpGuide;
}

/** A ready question the assistant offers, with its answer. */
export interface HelpQuestion {
  readonly question: string;
  /** Words and phrases that point to this question. A phrase scores more than a single word. */
  readonly keys: readonly string[];
  /** The answer, a paragraph per entry; [[...]] marks names on the screen. */
  readonly answer: readonly string[];
  /** The help page the answer comes from: a route or a general page id. */
  readonly topic: string;
  /** The real answer also depends on the organisation's own rules (its policies). */
  readonly dependsOnPolicy?: boolean;
}

/** Everything the Help & Wiki says, loaded only when help is first opened. */
export interface HelpContent {
  /** One plain sentence per screen, by route. Every screen in the menu should have one. */
  readonly summaries: Readonly<Record<string, string>>;
  /** Full guides, by route. */
  readonly guides: Readonly<Record<string, HelpGuide>>;
  readonly general: readonly GeneralHelpPage[];
  readonly questions: readonly HelpQuestion[];
  /** Popular tasks shown at the top of the Help & Wiki: a route or general page id, and a label. */
  readonly popular: readonly (readonly [string, string])[];
}

/** One page in the Help & Wiki, built from the menu and the content. */
export interface HelpTopic {
  /** Stable id: the route without its first slash, slashes as dots (`me.leave`); or a general page id. */
  readonly id: string;
  readonly title: string;
  /** Where it sits: the menu group, or "Getting started" for general pages. */
  readonly group: string;
  /** The screen it explains; undefined for a general page. */
  readonly route?: string;
  /** How to get there through the menu ("My Time & Leave › My leave"). */
  readonly path?: string;
  readonly audience: HelpAudience;
  readonly summary: string;
  readonly guide?: HelpGuide;
}

/** A piece of guide text: plain words, or the name of something on the screen. */
export interface HelpTextPart {
  readonly text: string;
  readonly ui: boolean;
}
