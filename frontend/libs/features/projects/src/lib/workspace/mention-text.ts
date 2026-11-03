import type { ActivityEntry, MentionablePerson } from '@gggs/shared/models';

/** The @name being typed at the caret, if any. An @ only counts at the start of a word. */
export function mentionAt(text: string, caret: number): { readonly start: number; readonly query: string } | null {
  const before = text.slice(0, caret);
  const start = before.lastIndexOf('@');
  if (start < 0 || (start > 0 && !/\s/.test(before[start - 1]))) {
    return null;
  }

  const query = before.slice(start + 1);
  return /[\r\n]/.test(query) || query.length > 40 ? null : { start, query };
}

/** People whose name matches what has been typed so far. A name already typed in full needs no suggesting. */
export function matchingPeople(people: readonly MentionablePerson[], query: string): readonly MentionablePerson[] {
  const wanted = query.trim().toLowerCase();
  return people.filter((person) => person.name.toLowerCase().includes(wanted) && person.name.toLowerCase() !== wanted).slice(0, 6);
}

/** Puts the chosen name in place of what was typed, and says where the caret goes. */
export function insertMention(text: string, caret: number, start: number, name: string): { readonly text: string; readonly caret: number } {
  const inserted = `@${name} `;
  return { text: text.slice(0, start) + inserted + text.slice(caret), caret: start + inserted.length };
}

/**
 * Who a comment names, read from its text, so taking a name out of the text also takes it out of who is told.
 * Longer names go first, so @Asha Rao is not also read as @Asha.
 */
export function mentionedIn(text: string, people: readonly MentionablePerson[]): readonly string[] {
  let rest = text;
  const found: string[] = [];
  for (const person of [...people].sort((first, second) => second.name.length - first.name.length)) {
    const mention = `@${person.name}`;
    let index = rest.indexOf(mention);
    let named = false;
    while (index >= 0) {
      const after = rest[index + mention.length];
      if (after === undefined || !/[\p{L}\p{N}]/u.test(after)) {
        named = true;
        rest = rest.slice(0, index) + rest.slice(index + mention.length);
        index = rest.indexOf(mention);
      } else {
        index = rest.indexOf(mention, index + 1);
      }
    }

    if (named) {
      found.push(person.employeeId);
    }
  }

  return found;
}

const FIELD_NAMES: Readonly<Record<string, string>> = {
  type: 'type',
  title: 'title',
  priority: 'priority',
  startDate: 'start date',
  dueDate: 'due date',
  estimate: 'estimate',
  points: 'story points',
};

/** A line of a task's history, as a sentence that follows the person's name. */
export function describeActivity(entry: ActivityEntry): string {
  const { field, oldValue, newValue } = entry;
  switch (field) {
    case 'created':
      return 'created this task';
    case 'imported':
      return 'imported this task from a file';
    case 'column':
      return `moved it from ${oldValue ?? 'nowhere'} to ${newValue ?? 'nowhere'}`;
    case 'assignee':
      return newValue ? `assigned it to ${newValue}` : `unassigned ${oldValue ?? 'it'}`;
    case 'sprint':
      return newValue ? `planned it into ${newValue}` : `took it out of ${oldValue ?? 'its sprint'}`;
    default: {
      const name = FIELD_NAMES[field] ?? field;
      if (!newValue) {
        return `cleared the ${name}`;
      }

      return oldValue ? `changed the ${name} from ${oldValue} to ${newValue}` : `set the ${name} to ${newValue}`;
    }
  }
}
