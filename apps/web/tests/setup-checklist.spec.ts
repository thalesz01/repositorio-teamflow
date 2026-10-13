import { expect, test } from "@playwright/test";

test("primeiros passos mostram o que falta e somem quando o ambiente está pronto", async ({ page }) => {
  let ready = false;
  await page.route("**/repository/", route => ready
    ? route.fulfill({ json: { id: crypto.randomUUID(), provider: "local", repositoryId: 0, remoteUrl: "C:\projetos\app", baseBranch: "main", baseCommit: "a".repeat(40), version: crypto.randomUUID(), revokedAt: null } })
    : route.fulfill({ status: 404, json: { code: "not_found", title: "Sem repositório." } }));
  await page.route("**/profiles/machine", route => route.request().method() === "GET"
    ? route.fulfill({ json: { installed: true, authenticated: ready, enabled: true } }) : route.fallback());
  await page.route("**/runners/status", route => route.fulfill({ json: ready
    ? { configured: true, online: true, lastSeenAt: new Date().toISOString() } : { configured: false, online: false, lastSeenAt: null } }));
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`setup-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar equipe", exact: true }).click();
  await page.getByLabel("Nova equipe", { exact: true }).fill("Equipe setup");
  await page.getByRole("button", { name: "Criar equipe", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar projeto", exact: true }).click();
  await page.getByLabel("Novo projeto", { exact: true }).fill("Projeto setup");
  await page.getByRole("button", { name: "Criar projeto", exact: true }).click();

  const steps = page.getByRole("region", { name: "Primeiros passos", exact: true });
  await expect(steps).toBeVisible();
  const list = steps.getByRole("listitem");
  await expect(list).toHaveCount(3);
  await expect(list.nth(0)).toContainText("Escolha onde o código fica no computador");
  await expect(list.nth(1)).toContainText("codex login");
  await expect(list.nth(2)).toContainText("Ainda não configurado");
  await expect(steps.locator("code").first()).toContainText("Setup-Runner.ps1 -OrganizationId");
  await expect(steps.locator("code").first()).toContainText("-ProjectId");

  // "Configurar pasta" leads to the one place that saves it; the board keeps working meanwhile.
  await steps.getByRole("button", { name: "Configurar pasta" }).click();
  await expect(page.getByRole("heading", { name: "Configurações do projeto", exact: true })).toBeVisible();
  await page.getByRole("navigation", { name: "Navegação do projeto" }).getByRole("button", { name: "Quadro", exact: true }).click();

  ready = true;
  await steps.getByRole("button", { name: "Verificar de novo" }).click();
  await expect(page.getByText("Ambiente pronto", { exact: true })).toBeVisible();
  await expect(steps).toHaveCount(0);
});
