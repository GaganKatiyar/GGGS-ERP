import nx from '@nx/eslint-plugin';

export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  {
    ignores: ['**/dist', '**/out-tsc', '**/vitest.config.*.timestamp*'],
  },
  {
    files: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx'],
    rules: {
      /*
       * Module boundaries.
       *
       * This is the main reason the front end is an Nx workspace (ADR-10). With twenty feature
       * areas coming, "did anyone import payroll from the employee feature" has to be a build
       * failure, not something a reviewer is expected to notice.
       *
       * Two axes:
       *   type:  feature -> ui/data-access/util, and never the other way round, so a shared
       *          component can never reach back into a feature.
       *   scope: features depend on core and shared, never on each other. Two features that need
       *          the same thing promote it to shared rather than importing across.
       */
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: true,
          allow: ['^.*/eslint(\\.base)?\\.config\\.[cm]?[jt]s$'],
          depConstraints: [
            {
              sourceTag: 'type:app',
              onlyDependOnLibsWithTags: [
                'type:feature',
                'type:ui',
                'type:data-access',
                'type:util',
              ],
            },
            {
              sourceTag: 'type:feature',
              onlyDependOnLibsWithTags: [
                'type:feature',
                'type:ui',
                'type:data-access',
                'type:util',
              ],
            },
            {
              sourceTag: 'type:ui',
              onlyDependOnLibsWithTags: [
                'type:ui',
                'type:data-access',
                'type:util',
              ],
            },
            {
              sourceTag: 'type:data-access',
              onlyDependOnLibsWithTags: ['type:data-access', 'type:util'],
            },
            {
              // Utilities sit at the bottom: they may not reach up into anything.
              sourceTag: 'type:util',
              onlyDependOnLibsWithTags: ['type:util'],
            },
            {
              // The application is the composition root: it wires features together, which is
              // exactly why nothing else is allowed to.
              sourceTag: 'scope:app',
              onlyDependOnLibsWithTags: [
                'scope:app',
                'scope:core',
                'scope:shared',
                'scope:dashboard',
                'scope:organization',
                'scope:employees',
                'scope:admin',
                'scope:notifications',
                'scope:leave',
                'scope:workflow',
                'scope:attendance',
                'scope:reports',
                'scope:recruitment',
                'scope:onboarding',
                'scope:performance',
                'scope:learning',
                'scope:payroll',
                'scope:spend',
                'scope:assets',
              ],
            },
            {
              sourceTag: 'scope:core',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared'],
            },
            {
              sourceTag: 'scope:shared',
              onlyDependOnLibsWithTags: ['scope:shared'],
            },
            {
              // A feature may use core and shared, but not another feature.
              sourceTag: 'scope:dashboard',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:dashboard',
              ],
            },
            {
              sourceTag: 'scope:organization',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:organization',
              ],
            },
            {
              // Employees depends on organization's *types* only through shared (see
              // libs/shared/models) - hiring needs a department id, not the organization feature's
              // screens, so this stays a one-way boundary rather than a feature-to-feature import.
              sourceTag: 'scope:employees',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:employees',
              ],
            },
            {
              sourceTag: 'scope:admin',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:admin',
              ],
            },
            {
              sourceTag: 'scope:notifications',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:notifications',
              ],
            },
            {
              sourceTag: 'scope:leave',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:leave'],
            },
            {
              sourceTag: 'scope:workflow',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:workflow',
              ],
            },
            {
              sourceTag: 'scope:attendance',
              onlyDependOnLibsWithTags: [
                'scope:core',
                'scope:shared',
                'scope:attendance',
              ],
            },
            {
              sourceTag: 'scope:reports',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:reports'],
            },
            {
              sourceTag: 'scope:recruitment',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:recruitment'],
            },
            {
              sourceTag: 'scope:onboarding',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:onboarding'],
            },
            {
              sourceTag: 'scope:performance',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:performance'],
            },
            {
              sourceTag: 'scope:learning',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:learning'],
            },
            {
              sourceTag: 'scope:payroll',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:payroll'],
            },
            {
              sourceTag: 'scope:spend',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:spend'],
            },
            {
              sourceTag: 'scope:assets',
              onlyDependOnLibsWithTags: ['scope:core', 'scope:shared', 'scope:assets'],
            },
          ],
        },
      ],
    },
  },
  {
    files: [
      '**/*.ts',
      '**/*.tsx',
      '**/*.cts',
      '**/*.mts',
      '**/*.js',
      '**/*.jsx',
      '**/*.cjs',
      '**/*.mjs',
    ],
    rules: {},
  },
];
