import nx from '@nx/eslint-plugin';
import baseConfig from '../../../eslint.config.mjs';

export default [
  ...nx.configs['flat/angular'],
  ...nx.configs['flat/angular-template'],
  ...baseConfig,
  {
    files: ['**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'gggs',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'gggs',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    rules: {
      // The shared pickers are form controls (ControlValueAccessor), so a label around one is
      // associated with it just as with a native input.
      '@angular-eslint/template/label-has-associated-control': [
        'error',
        { controlComponents: ['gggs-org-select', 'gggs-lookup-select', 'gggs-employee-select'] },
      ],
    },
  },
];
