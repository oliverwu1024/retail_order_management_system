import type { Config } from 'tailwindcss'

// ─────────────────────────────────────────────────────────────────────────────
//  Tailwind 3.4 configuration for Retail OMS.
//
//  WHY THIS THEME SHAPE?
//  ---------------------
//  We follow the shadcn/ui CSS-variable convention: semantic color tokens
//  (background, foreground, primary, muted, destructive, etc.) defined as
//  CSS custom properties in src/index.css, then mapped here so they're
//  usable as Tailwind utilities (bg-background, text-foreground, ...).
//
//  Two big wins from this pattern:
//    1. Dark mode swap is a single CSS variable change — no Tailwind
//       darkMode: 'class' utility duplication everywhere.
//    2. shadcn/ui components copy-paste in without any class rewrites —
//       they reference these exact token names.
//
//  WHY container.center / 2xl: 1400px?
//  -----------------------------------
//  shadcn's canonical container settings. Matches what Admin shells need
//  (max content width with auto-centered horizontal padding).
// ─────────────────────────────────────────────────────────────────────────────

export default {
  // Scan every .ts / .tsx file under src/ for Tailwind class names so
  // unused classes are pruned from the production bundle.
  content: ['./index.html', './src/**/*.{ts,tsx}'],

  // Dark mode by class name on <html> or <body>. Lets us flip themes by
  // toggling a single class — no per-component dark: utilities to maintain.
  darkMode: ['class'],

  theme: {
    container: {
      center: true,
      padding: '2rem',
      screens: {
        '2xl': '1400px',
      },
    },
    extend: {
      colors: {
        // Map each Tailwind color token to the CSS variable defined in
        // index.css. hsl(var(--token)) lets us add alpha at the utility
        // level (bg-primary/50 still works because Tailwind injects the
        // alpha into the hsl() call).
        border: 'hsl(var(--border))',
        input: 'hsl(var(--input))',
        ring: 'hsl(var(--ring))',
        background: 'hsl(var(--background))',
        foreground: 'hsl(var(--foreground))',
        primary: {
          DEFAULT: 'hsl(var(--primary))',
          foreground: 'hsl(var(--primary-foreground))',
        },
        secondary: {
          DEFAULT: 'hsl(var(--secondary))',
          foreground: 'hsl(var(--secondary-foreground))',
        },
        destructive: {
          DEFAULT: 'hsl(var(--destructive))',
          foreground: 'hsl(var(--destructive-foreground))',
        },
        muted: {
          DEFAULT: 'hsl(var(--muted))',
          foreground: 'hsl(var(--muted-foreground))',
        },
        accent: {
          DEFAULT: 'hsl(var(--accent))',
          foreground: 'hsl(var(--accent-foreground))',
        },
        card: {
          DEFAULT: 'hsl(var(--card))',
          foreground: 'hsl(var(--card-foreground))',
        },
      },
      borderRadius: {
        // Single radius scale driven by --radius — keeps the design coherent.
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 2px)',
        sm: 'calc(var(--radius) - 4px)',
      },
      fontFamily: {
        // Use the OS-default UI stack as the body font for now. When the
        // brand picks a typeface, swap "Inter, ..." in front of system-ui.
        sans: [
          'system-ui',
          '-apple-system',
          'BlinkMacSystemFont',
          'Segoe UI',
          'Roboto',
          'Helvetica Neue',
          'Arial',
          'sans-serif',
        ],
      },
    },
  },
  plugins: [],
} satisfies Config
