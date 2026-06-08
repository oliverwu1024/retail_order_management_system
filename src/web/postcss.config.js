// PostCSS pipeline. Tailwind 3.x is a PostCSS plugin (4.x flips this
// architecture, which is why we pinned 3.4 — REQUIREMENTS task 0.3.2).
// autoprefixer adds vendor prefixes (e.g., -webkit-) for CSS properties
// that still need them in browsers we target.
export default {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
}
