import { heroui } from '@heroui/theme';

const bakaRoots = ':where(#bk-app, #bk-overlay-root, .bk-marker, .bk-cover-overlay-host)';
const heroUI = heroui();

// HeroUI injects :root / [data-theme] styles even without Tailwind preflight.
// A background on <html> stops the host's body background from filling the
// viewport on short pages. Keep theme colors and variables on our own roots.
const scopedHeroUI = {
  ...heroUI,
  handler(api) {
    heroUI.handler({
      ...api,
      addBase(styles) {
        api.addBase(Object.fromEntries(Object.entries(styles).map(([selector, declarations]) => [
          selector.split(',').map((part) => `${bakaRoots}${part.trim().replace(/^:root\b/, '')}`).join(', '),
          declarations,
        ])));
      },
      addUtilities(styles, ...options) {
        // The plugin also emits .light / .dark theme variables as utilities.
        // Host theme classes must not receive these declarations either.
        api.addUtilities(Object.fromEntries(Object.entries(styles).map(([selector, declarations]) => [
          selector === '.light' || selector === '.dark' ? `${bakaRoots}${selector}` : selector,
          declarations,
        ])), ...options);
      },
    });
  },
};

/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './src/**/*.{js,ts,jsx,tsx}',
    './node_modules/@heroui/theme/dist/**/*.{js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {},
  },
  darkMode: 'class',
  plugins: [scopedHeroUI],
};
