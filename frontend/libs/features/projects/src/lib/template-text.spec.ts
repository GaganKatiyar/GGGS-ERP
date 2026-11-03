import { describe, expect, it } from 'vitest';
import { activitiesToText, columnsToText, phasesToText, textToActivities, textToColumns, textToPhases } from './template-text';

describe('template text', () => {
  it('reads phases a line at a time, dropping blank lines', () => {
    expect(textToPhases(' Discovery \n\nBuild\n')).toEqual(['Discovery', 'Build']);
  });

  it('reads columns with their categories, however they are typed', () => {
    expect(textToColumns('To do | Todo\nDoing | in progress\nDone | DONE')).toEqual([
      { name: 'To do', category: 'Todo' },
      { name: 'Doing', category: 'InProgress' },
      { name: 'Done', category: 'Done' },
    ]);
  });

  it('treats a column with no known category as work in progress', () => {
    expect(textToColumns('Review\nQA | testing')).toEqual([
      { name: 'Review', category: 'InProgress' },
      { name: 'QA', category: 'InProgress' },
    ]);
  });

  it('reads activities and their tasks', () => {
    expect(textToActivities('Meetings: Stand-up, Client meetings\nAdmin')).toEqual([
      { name: 'Meetings', tasks: ['Stand-up', 'Client meetings'] },
      { name: 'Admin', tasks: [] },
    ]);
  });

  it('gets back what it wrote', () => {
    const columns = [{ name: 'New', category: 'Backlog' }, { name: 'Done', category: 'Done' }] as const;
    const activities = [{ name: 'Meetings', tasks: ['Stand-up', 'Reviews'] }];

    expect(textToPhases(phasesToText(['A', 'B']))).toEqual(['A', 'B']);
    expect(textToColumns(columnsToText(columns))).toEqual(columns);
    expect(textToActivities(activitiesToText(activities))).toEqual(activities);
  });
});
