import { expect, test } from "@playwright/test";

test("configuração da pasta não exige perfil Codex por projeto", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`integrations-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar equipe", exact: true }).click();
  await page.getByLabel("Nova equipe", { exact: true }).fill("Equipe integrações UI");
  await page.getByRole("button", { name: "Criar equipe", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar projeto", exact: true }).click();
  await page.getByLabel("Novo projeto", { exact: true }).fill("Projeto integrações UI");
  await page.getByRole("button", { name: "Criar projeto", exact: true }).click();
  await page.getByRole("navigation", { name: "Navegação do projeto" }).getByRole("button", { name: "Configurações", exact: true }).click();
  const panel = page.getByRole("region", { name: "Integrações do projeto", exact: true });
  await expect(panel.getByRole("heading", { name: "Codex da máquina", exact: true })).toBeVisible();
  await expect(panel.getByLabel("Nome do perfil", { exact: true })).toHaveCount(0);
  await expect(panel.locator('input[type="password"]')).toHaveCount(0);
  await expect(panel.getByRole("button", { name: "Salvar configuração", exact: true })).toBeDisabled();
  await panel.getByLabel("Caminho do repositório", { exact: true }).fill("C:\\fixtures\\repository");
  const response = page.waitForResponse(response => /\/repository\/local\/inspect$/.test(new URL(response.url()).pathname) && response.request().method() === "POST");
  await panel.getByRole("button", { name: "Verificar pasta", exact: true }).click();
  expect((await response).status()).toBe(503);
  await expect(panel.getByRole("alert")).toContainText("administrador precisa configurar");
  await expect(panel.getByRole("button", { name: "Salvar configuração", exact: true })).toBeDisabled();
});