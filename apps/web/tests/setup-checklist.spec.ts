import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("primeiros passos mostram o que falta e somem quando o ambiente está pronto", async ({ page }) => {
  let ready = false;
  await page.route("**/profiles/machine", route => route.request().method() === "GET"
    ? route.fulfill({ json: { installed: true, authenticated: ready, enabled: true } }) : route.fallback());
  await page.route("**/runners/status", route => route.fulfill({ json: ready
    ? { configured: true, online: true, lastSeenAt: new Date().toISOString() } : { configured: false, online: false, lastSeenAt: null } }));
  await signUp(page, "setup");

  const steps = page.getByRole("region", { name: "Primeiros passos", exact: true });
  await expect(steps).toBeVisible();
  const list = steps.getByRole("listitem");
  await expect(list).toHaveCount(2);
  await expect(list.nth(0)).toContainText("codex login");
  await expect(list.nth(1)).toContainText("Ainda não configurado");
  await expect(steps.locator("code").first()).toContainText("Setup-Runner.ps1 -OrganizationId");
  await expect(steps.locator("code").first()).toContainText("-ProjectId");

  ready = true;
  await steps.getByRole("button", { name: "Verificar de novo" }).click();
  await expect(page.getByText("Ambiente pronto", { exact: true })).toBeVisible();
  await expect(steps).toHaveCount(0);
});
