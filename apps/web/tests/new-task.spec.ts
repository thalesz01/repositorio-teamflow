import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("nova tarefa escolhe a pasta, a branch e envia tudo junto sem equipe nem projeto", async ({ page }) => {
  await signUp(page, "folders");
  // UI transport fixture; API integration tests verify directory authority and traversal independently.
  const root = "C:\\projetos";
  const repository = root + "\\app";
  let available = false;
  await page.route("**/repository/local/directories*", route => {
    if (!available) { available = true; return route.fulfill({ status: 503, json: { code: "integration_not_configured" } }); }
    const current = new URL(route.request().url()).searchParams.get("path");
    return route.fulfill({ json: { currentPath: current, parentPath: current === repository ? root : null,
      roots: [root], directories: current === root ? [{ name: "app", path: repository }] : [],
      isRepository: current === repository, canConnect: current === repository, truncated: false } });
  });
  await page.route("**/repository/local/inspect", route => {
    const input = route.request().postDataJSON() as { path: string };
    return route.fulfill({ json: { path: input.path, hasGit: input.path === repository, branches: input.path === repository ? ["main", "feature/local"] : [], currentBranch: input.path === repository ? "main" : null } });
  });
  const created: Record<string, unknown>[] = [];
  await page.route("**/tasks/", route => {
    if (route.request().method() !== "POST") return route.fallback();
    created.push(route.request().postDataJSON());
    return route.fulfill({ status: 409, json: { code: "repository_not_authorized", title: "Fixture" } });
  });

  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  await dialog.getByLabel("Título", { exact: true }).fill("Corrigir frete");
  await dialog.getByLabel("O que fazer", { exact: true }).fill("Arredondar em duas casas.");
  const choose = dialog.getByRole("button", { name: "Escolher pasta", exact: true });
  await choose.click();
  const picker = page.getByRole("dialog", { name: "Escolher a pasta do projeto" });
  await expect(picker.getByRole("alert")).toContainText("Nenhuma pasta liberada neste servidor");
  await picker.getByRole("button", { name: "Tentar novamente", exact: true }).click();
  await expect(picker.getByRole("button", { name: "Usar esta pasta" })).toBeDisabled();
  // Escape closes only the picker; the task being written stays open and nothing is lost.
  await picker.getByRole("button", { name: "Fechar", exact: true }).press("Escape");
  await expect(picker).toHaveCount(0);
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel("Título", { exact: true })).toHaveValue("Corrigir frete");
  await expect(choose).toBeFocused();
  await choose.click();
  await picker.getByRole("button", { name: root, exact: false }).click();
  await picker.getByRole("button", { name: "app", exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta" }).click();
  await expect(dialog.getByLabel("Pasta do projeto", { exact: true })).toHaveValue(repository);
  const branches = dialog.getByRole("combobox", { name: "Branch de partida", exact: true });
  await expect(branches).toHaveValue("main");
  await branches.selectOption("feature/local");
  await dialog.getByRole("button", { name: "Criar tarefa", exact: true }).click();
  await expect(dialog.getByRole("alert")).toContainText("Esta pasta não está liberada no servidor");
  expect(created).toHaveLength(1);
  expect(created[0]).toMatchObject({ title: "Corrigir frete", folderPath: repository, baseBranch: "feature/local", stage: "ready", dependencyIds: [] });
  expect(created[0].acceptanceCriteria).toEqual(["A tarefa descrita foi concluída e os testes passam."]);
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
