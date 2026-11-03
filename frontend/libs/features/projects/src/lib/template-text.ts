import type { BoardColumnCategory, ProjectTemplateActivity, ProjectTemplateColumn } from '@gggs/shared/models';

/**
 * A template's lists as editable text, one item to a line, so an administrator can change them without a
 * builder: phases as plain lines, columns as "Name | Category", activities as "Activity: task, task".
 */
const lines = (text: string): string[] =>
  text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

export const phasesToText = (phases: readonly string[]): string => phases.join('\n');

export const textToPhases = (text: string): readonly string[] => lines(text);

export const columnsToText = (columns: readonly ProjectTemplateColumn[]): string => columns.map((column) => `${column.name} | ${column.category}`).join('\n');

const CATEGORIES: readonly BoardColumnCategory[] = ['Backlog', 'Todo', 'InProgress', 'Done'];

/** Columns from text. A line with no category, or one it does not recognise, is work in progress: a safe middle of the board. */
export const textToColumns = (text: string): readonly ProjectTemplateColumn[] =>
  lines(text).map((line) => {
    const [name, category] = line.split('|').map((part) => part.trim());
    const known = CATEGORIES.find((candidate) => candidate.toLowerCase() === (category ?? '').replace(/\s+/g, '').toLowerCase());
    return { name, category: known ?? 'InProgress' };
  });

export const activitiesToText = (activities: readonly ProjectTemplateActivity[]): string => activities.map((activity) => `${activity.name}: ${activity.tasks.join(', ')}`).join('\n');

export const textToActivities = (text: string): readonly ProjectTemplateActivity[] =>
  lines(text).map((line) => {
    const colon = line.indexOf(':');
    const name = colon < 0 ? line : line.slice(0, colon).trim();
    const tasks = colon < 0 ? [] : line.slice(colon + 1).split(',').map((task) => task.trim()).filter((task) => task.length > 0);
    return { name, tasks };
  });
