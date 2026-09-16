/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        // Same green the docx/README/roadmap diagrams already use
        // (COLORS.headingGreen in docgen/build.js, #28a745-ish "done" green) —
        // one visual identity across the whole project, not a new palette
        // invented just for the frontend.
        atlas: {
          50: "#f0f9f2",
          100: "#d4edda",
          200: "#a8d9b6",
          500: "#28a745",
          600: "#1e7e34",
          700: "#155724",
        },
      },
    },
  },
  plugins: [],
};
