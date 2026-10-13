import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  timeout: 60000,
  use: { baseURL: process.env.E2E_URL ?? "http://localhost:13000", trace: "retain-on-failure", channel: process.env.E2E_BROWSER_CHANNEL || undefined },
  reporter: "list",
});
