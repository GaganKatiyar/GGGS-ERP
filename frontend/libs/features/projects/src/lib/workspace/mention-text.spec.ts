import type { ActivityEntry } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { describeActivity, insertMention, matchingPeople, mentionAt, mentionedIn } from './mention-text';

const asha = { employeeId: 'a', name: 'Asha Rao' };
const ash = { employeeId: 'b', name: 'Asha' };
const ravi = { employeeId: 'c', name: 'Ravi Kumar' };
const entry = (field: string, oldValue?: string, newValue?: string): ActivityEntry => ({ id: '1', field, oldValue, newValue, at: '2026-11-03T10:00:00Z' });

describe('mentions', () => {
  it('finds the name being typed, only at the start of a word', () => {
    expect(mentionAt('Hello @As', 9)).toEqual({ start: 6, query: 'As' });
    expect(mentionAt('@', 1)).toEqual({ start: 0, query: '' });
    expect(mentionAt('a@b.com', 7)).toBeNull();
    expect(mentionAt('no one here', 5)).toBeNull();
    expect(mentionAt('@Asha\nnext', 10)).toBeNull();
  });

  it('matches anywhere in the name, ignoring case', () => {
    expect(matchingPeople([asha, ravi], 'rao')).toEqual([asha]);
    expect(matchingPeople([asha, ravi], '')).toEqual([asha, ravi]);
  });

  it('stops suggesting once the name is complete', () => {
    expect(matchingPeople([asha, ravi], 'Asha Rao ')).toEqual([]);
  });

  it('puts the name in place of what was typed', () => {
    expect(insertMention('Hi @As there', 6, 3, 'Asha Rao')).toEqual({ text: 'Hi @Asha Rao  there', caret: 13 });
  });

  it('reads who is named from the text', () => {
    expect(mentionedIn('@Ravi Kumar please look, and @Asha Rao too', [asha, ravi])).toEqual(['c', 'a']);
    expect(mentionedIn('@Ravi Kumar, thanks.', [ravi])).toEqual(['c']);
    expect(mentionedIn('No one here', [asha])).toEqual([]);
  });

  it('does not read a longer name as a shorter one', () => {
    expect(mentionedIn('@Asha Rao hello', [ash, asha])).toEqual(['a']);
    expect(mentionedIn('@Asha Rao and @Asha', [ash, asha])).toEqual(['a', 'b']);
    expect(mentionedIn('@Ashapura', [ash])).toEqual([]);
  });

  it('counts someone once, however often they are named', () => {
    expect(mentionedIn('@Asha Rao, @Asha Rao!', [asha])).toEqual(['a']);
  });
});

describe('history', () => {
  it('reads as a sentence', () => {
    expect(describeActivity(entry('created'))).toBe('created this task');
    expect(describeActivity(entry('imported'))).toBe('imported this task from a file');
    expect(describeActivity(entry('column', 'To Do', 'Done'))).toBe('moved it from To Do to Done');
    expect(describeActivity(entry('assignee', undefined, 'Asha Rao'))).toBe('assigned it to Asha Rao');
    expect(describeActivity(entry('assignee', 'Asha Rao'))).toBe('unassigned Asha Rao');
    expect(describeActivity(entry('sprint', undefined, 'Sprint 4'))).toBe('planned it into Sprint 4');
    expect(describeActivity(entry('sprint', 'Sprint 4'))).toBe('took it out of Sprint 4');
    expect(describeActivity(entry('priority', 'Low', 'High'))).toBe('changed the priority from Low to High');
    expect(describeActivity(entry('dueDate', undefined, '2026-12-01'))).toBe('set the due date to 2026-12-01');
    expect(describeActivity(entry('dueDate', '2026-12-01'))).toBe('cleared the due date');
  });
});
