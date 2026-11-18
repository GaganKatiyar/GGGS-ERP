import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/** The frontend workspace, found from wherever the test runs by its nx.json. */
function workspaceRoot(): string {
  let folder = process.cwd();
  while (!existsSync(join(folder, 'nx.json')) && dirname(folder) !== folder) {
    folder = dirname(folder);
  }
  return folder;
}

const ROOT = workspaceRoot();

function sources(folder: string): string[] {
  return readdirSync(folder).flatMap((name) => {
    const path = join(folder, name);
    if (statSync(path).isDirectory()) {
      return name === 'node_modules' || name.startsWith('.') ? [] : sources(path);
    }
    return /\.(ts|html)$/.test(name) && !name.endsWith('.spec.ts') ? [path] : [];
  });
}

/**
 * Times read the same everywhere: 12-hour, "1:50 PM". A 24-hour pattern, Angular's 'short' and 'medium'
 * (which print "11/17/26" and seconds) and the browser's own locale (which turns into 24-hour on an
 * en-GB PC) are each a way back to inconsistent times. Use TIME / DATE_TIME, clockTime, dateTime or
 * momentTime from @gggs/shared/ui instead.
 */
const FORBIDDEN: readonly [RegExp, string][] = [
  [/date\s*:\s*'[^']*\bH{1,2}:mm/, "a 24-hour date pipe ('HH:mm'): use 'h:mm a' or DATE_TIME"],
  [/date\s*:\s*'(short|medium|long|full|shortTime|mediumTime)'/, "date: 'short'/'medium': use DATE_TIME"],
  [/toLocaleTimeString\(/, 'toLocaleTimeString(): use momentTime() or clockTime()'],
  [/toLocaleString\([^)]*hour/, 'toLocaleString() with a time: use dateTime()'],
];

describe('times across the app', () => {
  it('are shown in one 12-hour style', () => {
    const offences: string[] = [];
    const files = ['libs', 'apps'].flatMap((name) => sources(join(ROOT, name)));
    expect(files.length, 'the scan must find the app’s source').toBeGreaterThan(200);
    {
      for (const file of files) {
        readFileSync(file, 'utf8')
          .split('\n')
          .forEach((line, index) => {
            for (const [pattern, advice] of FORBIDDEN) {
              if (pattern.test(line)) {
                offences.push(`${relative(ROOT, file)}:${index + 1} - ${advice}`);
              }
            }
          });
      }
    }

    expect(offences).toEqual([]);
  });
});
