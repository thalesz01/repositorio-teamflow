import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("busca por descrição preserva tarefas, o quadro cabe no celular e o diálogo devolve o foco", async ({ page }) => {
  await signUp(page, "design");
  // A task created before the four-step flow (no folder, still in Intake) must remain visible on the board.
  const organizations = await (await page.request.get("/api/v1/organizations/")).json();
  const projects = await (await page.request.get(`/api/v1/organizations/${organizations.items[0].id}/projects/`)).json();
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  const created = await page.request.post(`/api/v1/organizations/${organizations.items[0].id}/projects/${projects.items[0].id}/tasks/`, {
    headers: { "X-CSRF-Token": csrf.token },
    data: { title: "Melhorar checkout", description: "Corrigir arredondamento financeiro.", acceptanceCriteria: ["Duas casas decimais"], assigneeId: null, dependencyIds: [] },
  });
  expect(created.status()).toBe(201);
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  const navigation = page.getByRole("navigation", { name: "Navegação principal" });
  await expect(navigation.getByRole("button", { name: "Demandas" })).toHaveCount(0);
  await navigation.getByRole("button", { name: "Memória", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Memória do projeto", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Quadro de tarefas", exact: true })).toBeHidden();
  await navigation.getByRole("button", { name: "Quadro", exact: true }).click();
  const card = page.getByRole("region", { name: "Criadas", exact: true }).getByRole("button", { name: "Abrir tarefa Melhorar checkout", exact: true });
  await expect(card).toBeVisible();
  await page.getByLabel("Buscar tarefas", { exact: true }).fill("FINANCEIRO");
  await expect(card).toBeVisible();
  await page.getByLabel("Buscar tarefas", { exact: true }).fill("inexistente");
  await expect(card).toHaveCount(0);
  await expect(page.getByRole("status").filter({ hasText: "0 de 1 no filtro" })).toBeVisible();
  await page.getByLabel("Buscar tarefas", { exact: true }).fill("");
  await expect(card).toBeVisible();
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const editor = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  await expect(editor.getByLabel("Título", { exact: true })).toBeFocused();
  await expect(editor.getByLabel("Título", { exact: true })).toHaveValue("");
  await editor.getByLabel("Título", { exact: true }).press("Escape");
  await expect(editor).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Nova tarefa", exact: true })).toBeFocused();
});
