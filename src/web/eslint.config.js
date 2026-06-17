import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import prettier from 'eslint-config-prettier'
import { defineConfig, globalIgnores } from 'eslint/config'

// Flat-config ESLint. `eslint-config-prettier` is loaded LAST so it disables
// any stylistic rules from earlier configs that would conflict with Prettier
// — i.e., let Prettier own formatting, let ESLint own correctness.
export default defineConfig([
  // `e2e` (Playwright specs) run under Playwright's own runner/tsconfig and use
  // node + Playwright globals, not the browser app config — keep them out of the
  // app lint pass. `playwright-report`/`coverage` are generated artifacts.
  globalIgnores([
    'dist',
    'coverage',
    'playwright-report',
    'test-results',
    'e2e',
    'src/lib/api/schema.d.ts',
  ]),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
      prettier,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  // shadcn-style UI primitives export both the component AND a cva variants
  // function from the same file. That breaks Vite's fast-refresh rule, but
  // splitting the variants into a separate file is the canonical shadcn
  // friction point — keeping them together matches the upstream docs and
  // makes copy-paste from the shadcn site work without edits. Disable the
  // rule for the UI primitive folder only.
  {
    files: ['src/components/ui/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
])
