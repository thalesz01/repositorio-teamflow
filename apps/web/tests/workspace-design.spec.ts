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
  const createdTask = await created.json();
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
  // Failed-task fixture: no AI execution or real task mutation.
  await page.route(`**/projects/${projects.items[0].id}/tasks/?*`, route => route.fulfill({ json: {
    items: [{ ...createdTask, stage: "failed", folderPath: "C:\\projetos\\checkout", baseBranch: "main", allowedTransitions: ["ready", "cancelled"] }], total: 1, page: 1, pageSize: 20,
  } }));
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  await expect(page.getByRole("button", { name: "Tentar de novo", exact: true })).toBeVisible();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await card.click();
  const taskDialog = page.getByRole("dialog", { name: "Melhorar checkout", exact: true });
  await expect(taskDialog).toBeVisible();
  await expect(taskDialog.getByText("A última execução não foi concluída", { exact: true })).toBeVisible();
  await expect(taskDialog.getByRole("heading", { name: "Pasta de trabalho" })).toBeVisible();
  await expect(taskDialog.getByRole("button", { name: "Executar novamente", exact: true })).toBeVisible();
  const bounds = await taskDialog.boundingBox();
  expect(bounds).not.toBeNull();
  expect(Math.abs(bounds!.x + bounds!.width / 2 - 720)).toBeLessThan(2);
  expect(bounds!.y).toBeGreaterThan(20);
  await taskDialog.getByRole("button", { name: "Ver execuções", exact: true }).click();
  await expect(taskDialog.getByRole("tabpanel", { name: /Execuções/ })).toBeVisible();
  await taskDialog.getByRole("tab", { name: /Execuções/ }).press("ArrowLeft");
  await expect(taskDialog.getByRole("tab", { name: "Detalhes", exact: true })).toBeFocused();
  await page.emulateMedia({ colorScheme: "dark" });
  await page.screenshot({ path: "test-results/task-modal-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await expect(taskDialog.getByRole("button", { name: "Fechar", exact: true })).toBeInViewport();
  await expect(taskDialog.getByRole("button", { name: "Salvar alterações", exact: true })).toBeInViewport();
  expect(await taskDialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  const retryTask = taskDialog.getByRole("button", { name: "Executar novamente", exact: true });
  await retryTask.scrollIntoViewIfNeeded();
  expect(await retryTask.evaluate(element => { const rect = element.getBoundingClientRect(); return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)); })).toBe(true);
  await page.screenshot({ path: "test-results/task-modal-mobile.png", fullPage: true });
  await taskDialog.getByRole("button", { name: "Fechar", exact: true }).press("Escape");
  await expect(taskDialog).toHaveCount(0);
  await expect(card).toBeFocused();
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  // Machine options are a transport fixture; no provider inference is performed.
  await page.route("**/profiles/agents", route => route.fulfill({ json: [
    { id: "codex", name: "Codex", installed: true, authenticated: true, enabled: true, models: [{ id: "default", efforts: [] }, { id: "fixture-model", efforts: ["low", "medium", "high"] }] },
    { id: "claude", name: "Claude Code", installed: true, authenticated: false, enabled: true, models: [{ id: "default", efforts: [] }, { id: "sonnet", efforts: ["low", "medium", "high"] }] },
  ] }));
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const editor = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  await expect(editor.getByLabel("Título", { exact: true })).toBeFocused();
  await expect(editor.getByLabel("Título", { exact: true })).toHaveValue("");
  await editor.getByLabel("Título", { exact: true }).fill("Melhorar cálculo do frete");
  await editor.getByLabel("O que fazer", { exact: true }).fill("Corrigir o arredondamento e considerar o desconto informado.");
  await expect(editor.getByRole("heading", { name: "O que vamos fazer?" })).toBeVisible();
  await page.setViewportSize({ width: 1066, height: 740 });
  const effort = editor.getByRole("combobox", { name: "Esforço de raciocínio", exact: true });
  await effort.scrollIntoViewIfNeeded();
  await expect(effort).toBeInViewport();
  expect(await effort.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2) === element;
  })).toBe(true);
  await editor.getByRole("combobox", { name: "Modelo", exact: true }).selectOption("fixture-model");
  await editor.getByRole("combobox", { name: "Esforço de raciocínio", exact: true }).selectOption("medium");
  await expect(editor.getByRole("combobox", { name: "Esforço de raciocínio", exact: true })).toHaveValue("medium");
  await editor.getByRole("combobox", { name: "IA", exact: true }).selectOption("claude");
  await expect(editor.getByRole("combobox", { name: "Modelo", exact: true })).toHaveValue("default");
  await expect(editor.getByRole("combobox", { name: "Esforço de raciocínio", exact: true })).toHaveValue("");
  await expect(editor.getByRole("button", { name: "Criar e executar", exact: true })).toBeDisabled();
  await expect(editor.getByText("Login necessário", { exact: true })).toBeVisible();
  await editor.getByRole("combobox", { name: "IA", exact: true }).selectOption("codex");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await expect(editor.getByRole("heading", { name: "Onde vamos trabalhar?" })).toBeVisible();
  await page.screenshot({ path: "test-results/new-task-desktop.png", fullPage: true });
  await editor.getByLabel("Resultado esperado (opcional)").fill("Preservar o desconto informado");
  await editor.getByRole("radio", { name: /Analisar o projeto/ }).check();
  await expect(editor.getByLabel("Resultado esperado (opcional)")).toHaveCount(0);
  const kindBounds = await editor.getByRole("radiogroup", { name: "Tipo de tarefa" }).boundingBox();
  const questionBounds = await editor.getByLabel("Assunto", { exact: true }).boundingBox();
  const settingsBounds = await editor.getByRole("heading", { name: "Qual repositório?", exact: true }).boundingBox();
  expect(kindBounds!.y + kindBounds!.height).toBeLessThan(questionBounds!.y);
  expect(kindBounds!.y + kindBounds!.height).toBeLessThan(settingsBounds!.y);
  await page.screenshot({ path: "test-results/new-analysis-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await effort.scrollIntoViewIfNeeded();
  await expect(editor.getByRole("button", { name: "Criar e analisar", exact: true })).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: "test-results/new-analysis-mobile.png", fullPage: true });
  await editor.getByRole("radio", { name: /Alterar o código/ }).scrollIntoViewIfNeeded();
  await editor.getByRole("radio", { name: /Alterar o código/ }).check();
  await expect(editor.getByLabel("Resultado esperado (opcional)")).toHaveValue("Preservar o desconto informado");
  await page.setViewportSize({ width: 375, height: 812 });
  await effort.scrollIntoViewIfNeeded();
  await expect(effort).toBeInViewport();
  expect(await effort.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2) === element;
  })).toBe(true);
  const saveButton = editor.getByRole("button", { name: "Criar tarefa", exact: true });
  await expect(saveButton).toBeInViewport();
  expect(await saveButton.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return element.contains(document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2));
  })).toBe(true);
  await page.screenshot({ path: "test-results/new-task-mobile.png", fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await editor.getByRole("button", { name: "Cancelar", exact: true }).press("Escape");
  await expect(editor).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Nova tarefa", exact: true })).toBeFocused();
});
