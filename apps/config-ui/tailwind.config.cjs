/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './src/**/*.{ts,tsx,html}',
    '../server-plugin/src/Jellycheckr.Server/Configuration/configUiHost.html'
  ],
  theme: {
    extend: {
      keyframes: {
        'jc-rise': {
          '0%': { opacity: '0', transform: 'translateY(8px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' }
        }
      },
      animation: {
        'jc-rise': 'jc-rise 0.45s ease both'
      }
    }
  },
  corePlugins: {
    preflight: false
  }
};
