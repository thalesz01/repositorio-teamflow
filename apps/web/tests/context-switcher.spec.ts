import { expect, test } from "@playwright/test";

test("criação com foco, cancelamento e projetos restritos à equipe selecionada", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`context-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  const addTeam = page.getByRole("button", { name: "Adicionar equipe", exact: true });
  await addTeam.click();
  const teamDialog = page.getByRole("dialog", { name: "Criar equipe", exact: true });
  await expect(teamDialog.getByLabel("Nova equipe", { exact: true })).toBeFocused();
  await teamDialog.getByLabel("Nova equipe", { exact: true }).fill("Não deve ser criada");
  await teamDialog.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(teamDialog).toHaveCount(0);
  await expect(addTeam).toBeFocused();
  await expect(page.getByRole("combobox", { name: "Equipe", exact: true }).locator("option")).toHaveCount(1);
  const createTeam = async (name: string) => {
    await addTeam.click();
    await teamDialog.getByLabel("Nova equipe", { exact: true }).fill(name);
    await teamDialog.getByRole("button", { name: "Criar equipe", exact: true }).click();
    await expect(page.getByRole("combobox", { name: "Equipe", exact: true })).toHaveValue(await page.getByRole("combobox", { name: "Equipe", exact: true }).locator("option", { hasText: name }).getAttribute("value") ?? "");
    await expect(teamDialog).toHaveCount(0);
  };
  const createProject = async (name: string, team: string) => {
    await page.getByRole("button", { name: "Adicionar projeto", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Criar projeto", exact: true });
    await expect(dialog).toContainText(team);
    await expect(dialog.getByLabel("Novo projeto", { exact: true })).toBeFocused();
    await dialog.getByLabel("Novo projeto", { exact: true }).fill(name);
    await dialog.getByRole("button", { name: "Criar projeto", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Quadro de tarefas", exact: true })).toBeVisible();
  };
  await createTeam("Equipe Alfa");
  await createProject("Projeto Alfa", "Equipe Alfa");
  await createTeam("Equipe Beta");
  await expect(page.getByRole("combobox", { name: "Projeto", exact: true }).locator("option", { hasText: "Projeto Alfa" })).toHaveCount(0);
  await createProject("Projeto Beta", "Equipe Beta");
  await page.getByRole("combobox", { name: "Equipe", exact: true }).selectOption({ label: "Equipe Alfa" });
  await expect(page.getByRole("combobox", { name: "Projeto", exact: true }).locator("option", { hasText: "Projeto Alfa" })).toHaveCount(1);
  await expect(page.getByRole("combobox", { name: "Projeto", exact: true }).locator("option", { hasText: "Projeto Beta" })).toHaveCount(0);
  await page.setViewportSize({ width: 375, height: 812 });
  await addTeam.click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await teamDialog.getByLabel("Nova equipe", { exact: true }).press("Escape");
  await expect(addTeam).toBeFocused();
  await page.route("**/api/v1/organizations/", route => route.request().method() === "POST"
    ? route.fulfill({ status: 503, json: { title: "Não foi possível criar a equipe." } }) : route.continue());
  await addTeam.click();
  await teamDialog.getByLabel("Nova equipe", { exact: true }).fill("Rascunho preservado");
  await teamDialog.getByRole("button", { name: "Criar equipe", exact: true }).click();
  await expect(teamDialog.getByRole("alert")).toContainText("Não foi possível criar a equipe");
  await expect(teamDialog.getByLabel("Nova equipe", { exact: true })).toHaveValue("Rascunho preservado");
  await expect(teamDialog.getByRole("button", { name: "Criar equipe", exact: true })).toBeEnabled();
});
