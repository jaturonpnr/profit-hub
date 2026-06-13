/** @type {import('tailwindcss').Config} */
module.exports = {
  // Component templates are inline strings in .ts files, so .ts MUST be scanned.
  content: ['./src/**/*.{html,ts}'],
  theme: {
    extend: {
      colors: {
        // Brand violet/indigo scale — buttons, active nav, links, focus.
        brand: {
          50: 'var(--brand-50)',
          100: 'var(--brand-100)',
          200: 'var(--brand-200)',
          300: 'var(--brand-300)',
          400: 'var(--brand-400)',
          500: 'var(--brand-500)',
          600: 'var(--brand-600)',
          700: 'var(--brand-700)',
          800: 'var(--brand-800)',
          900: 'var(--brand-900)',
          950: 'var(--brand-950)',
          DEFAULT: 'var(--brand-500)',
        },
        // Layered dark surfaces.
        bg: 'var(--bg)',
        surface: {
          DEFAULT: 'var(--surface)',
          raised: 'var(--surface-raised)',
        },
        border: {
          DEFAULT: 'var(--border)',
          subtle: 'var(--border-subtle)',
        },
        // Text.
        text: {
          DEFAULT: 'var(--text)',
          muted: 'var(--text-muted)',
          faint: 'var(--text-faint)',
        },
        // Semantic P/L — RESERVED for value coloring only.
        profit: 'var(--profit)',
        loss: 'var(--loss)',
        amber: 'var(--amber)',
      },
      borderRadius: {
        DEFAULT: 'var(--radius)',
        lg: 'var(--radius)',
        md: 'calc(var(--radius) - 4px)',
        sm: 'calc(var(--radius) - 6px)',
      },
      boxShadow: {
        card: 'var(--shadow-card)',
        raised: 'var(--shadow-raised)',
        glow: 'var(--shadow-glow)',
      },
      fontFamily: {
        sans: ['"Inter Variable"', 'Inter', '-apple-system', 'BlinkMacSystemFont', 'sans-serif'],
      },
      keyframes: {
        // Opacity-only: a transform here would leave a lingering containing
        // block (animation-fill-mode: both), trapping `position: fixed`
        // descendants like modals. Keep it transform-free.
        'fade-in': {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
      },
      animation: {
        'fade-in': 'fade-in 0.35s ease-out both',
      },
    },
  },
  plugins: [],
};
