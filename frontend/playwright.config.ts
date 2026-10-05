import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  workers: 1,
  timeout: 60000,
  use: {
    baseURL: process.env.E2E_PUBLISHED
      ? "http://127.0.0.1:5080"
      : "http://127.0.0.1:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  reporter: [
    ["list"],
    ["junit", { outputFile: "../artifacts/e2e-results.xml" }],
  ],
  projects: [
    {
      name: "phone-390",
      use: {
        viewport: { width: 390, height: 844 },
        isMobile: true,
        hasTouch: true,
      },
    },
    {
      name: "phone-360",
      use: {
        viewport: { width: 360, height: 800 },
        isMobile: true,
        hasTouch: true,
      },
    },
  ],
  webServer: [
    {
      command: "node tests/server.mjs",
      url: "http://127.0.0.1:5080/health",
      timeout: 60000,
    },
    ...(!process.env.E2E_PUBLISHED
      ? [
          {
            command: "npm run dev -- --port 5173 --strictPort",
            url: "http://127.0.0.1:5173",
            timeout: 30000,
          },
        ]
      : []),
  ],
});
