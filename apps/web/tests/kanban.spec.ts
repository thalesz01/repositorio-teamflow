import path from "node:path";
import { expect, test } from "@playwright/test";
import { createRepository, folderRoot, signUp } from "./session";

const diff = "diff --git a/add.sh b/add.sh\nindex 1111111..2222222 100644\n--- a/add.sh\n+++ b/add.sh\n@@ -1,2 +1,2 @@\n #!/bin/sh\n-echo $(($1 - $2))\n+echo $(($1 + $2))\n";

test("criar, executar, corrigir com comentários e concluir", async ({ page }) => {
  const repository = createRepository();
  await signUp(page, "ui");
  // The machine check is stubbed ready: the real Codex login is not part of the browser test.
  await page.route("**/profiles/machine", route => route.request().method() === "GET"
    ? route.fulfill({ json: { installed: true, authenticated: true, enabled: true } }) : route.fallback());

  // 1. Criar: the folder travels with the task.
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const creator = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  await expect(creator.getByLabel("Título", { exact: true })).toBeFocused();
  await creator.getByLabel("Título", { exact: true }).fill("Corrigir cálculo do frete");
  await creator.getByLabel("O que fazer", { exact: true }).fill("A regra de arredondamento está incorreta.");
  await creator.getByRole("button", { name: "Escolher pasta" }).click();
  const picker = page.getByRole("dialog", { name: "Escolher a pasta do projeto" });
  await picker.getByRole("button", { name: folderRoot, exact: false }).click();
  await picker.getByRole("button", { name: path.basename(repository), exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta" }).click();
  await expect(creator.getByLabel("Pasta do projeto")).toHaveValue(repository);
  await creator.getByRole("button", { name: "Criar tarefa", exact: true }).click();
  await expect(creator).toHaveCount(0);
  const card = (lane: string) => page.getByRole("region", { name: lane, exact: true }).getByRole("button", { name: "Abrir tarefa Corrigir cálculo do frete", exact: true });
  await expect(card("Criadas")).toBeVisible();
  await expect(page.getByRole("region", { name: "Criadas", exact: true }).getByText(path.basename(repository))).toBeVisible();

  // 2. Executar: the task waits in the queue until a runner claims it.
  await page.getByRole("region", { name: "Criadas", exact: true }).getByRole("button", { name: "Executar", exact: true }).click();
  await expect(card("Executando")).toBeVisible();
  await page.getByRole("region", { name: "Executando", exact: true }).getByRole("button", { name: "Ver progresso" }).click();
  await expect(page.getByText("Na fila", { exact: true })).toBeVisible();
  await expect(page.getByText("Tentativa 0 de 3", { exact: true })).toBeVisible();
  await page.getByRole("tabpanel", { name: "Execuções" }).getByRole("button", { name: "Ver progresso", exact: true }).click();
  const progress = page.getByRole("region", { name: "Progresso da execução", exact: true });
  await expect(progress.getByText("Atualização ao vivo", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Fechar detalhe", exact: true }).click();

  // The runner is not part of the browser test: the finished execution, its review and the diff are fixtures.
  const organizations = await (await page.request.get("/api/v1/organizations/")).json();
  const organization = organizations.items[0];
  const projects = await (await page.request.get(`/api/v1/organizations/${organization.id}/projects/`)).json();
  const projectPath = `/api/v1/organizations/${organization.id}/projects/${projects.items[0].id}`;
  const tasks = await (await page.request.get(`${projectPath}/tasks/`)).json();
  const task = tasks.items[0];
  const executions = await (await page.request.get(`${projectPath}/tasks/${task.id}/executions/`)).json();
  const execution = executions.items[0];
  const executionPath = `${projectPath}/tasks/${task.id}/executions/${execution.id}`;
  await page.route(`**${projectPath}/tasks/?*`, route => route.fulfill({ json: { ...tasks, items: [{ ...task, stage: "review", allowedTransitions: [] }] } }));
  await page.route(`**${projectPath}/tasks/${task.id}`, route => route.fulfill({ json: { ...task, stage: "review", allowedTransitions: [] } }));
  await page.route(`**${executionPath}/publication`, route => route.fulfill({ json: { state: "published", attempts: 1, failureCode: null, pullRequest: {
    number: 7, url: `${projectPath}/repository/local/reviews/${execution.id}`, baseCommit: "a".repeat(40), headCommit: "b".repeat(40), branch: "teamflow/fixture" } } }));
  await page.route(`**${executionPath}/progress`, route => route.fulfill({ json: { execution, estimatedCostUsd: null, inputTokens: null, outputTokens: null, events: [], nextCursor: null, agentSummary: "Feito, mas faltou criar o arquivo de rotas." } }));
  await page.route(`**${projectPath}/repository/local/reviews/${execution.id}`, route => route.fulfill({ contentType: "text/plain", body: diff }));
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  await expect(card("Corrigir")).toBeVisible();

  // 3. Corrigir: a comment on a line becomes the instruction of a new execution.
  const corrections: { feedback: string; parentExecutionId: string }[] = [];
  await page.route(`**${projectPath}/tasks/${task.id}/executions/`, route => {
    if (route.request().method() !== "POST") return route.fallback();
    corrections.push(route.request().postDataJSON());
    return route.fulfill({ status: 201, json: { ...execution, id: crypto.randomUUID(), state: "queued", parentExecutionId: execution.id } });
  });
  await page.getByRole("region", { name: "Corrigir", exact: true }).getByRole("button", { name: "Revisar e concluir" }).click();
  const review = page.getByRole("dialog", { name: /^Revisão/ });
  const table = review.getByRole("table", { name: "Diff de add.sh" });
  await expect(table).toContainText("echo $(($1 + $2))");
  await expect(review.getByRole("region", { name: "Resumo do agente" })).toContainText("faltou criar o arquivo de rotas");
  await expect(review.getByRole("button", { name: /^Corrigir/ })).toBeDisabled();
  await table.getByRole("button", { name: "Comentar a linha 2 de add.sh" }).click();
  await review.getByLabel("Comentário para a linha 2").fill("Use uma variável com nome claro.");
  await review.getByRole("button", { name: "Adicionar comentário" }).click();
  await expect(review.getByText("Use uma variável com nome claro.")).toBeVisible();
  await expect(review.getByRole("status").filter({ hasText: "1 comentário" })).toBeVisible();
  await review.getByRole("button", { name: "Corrigir (1)" }).click();
  await expect(review).toHaveCount(0);
  expect(corrections).toHaveLength(1);
  expect(corrections[0].parentExecutionId).toBe(execution.id);
  expect(corrections[0].feedback).toContain("add.sh, linha 2");
  expect(corrections[0].feedback).toContain("Use uma variável com nome claro.");

  // 4. Concluir: approve and merge in one command.
  const completions: string[] = [];
  await page.route(`**${executionPath}/review/complete`, route => {
    completions.push(route.request().method());
    return route.fulfill({ json: { state: "merged", headCommit: "b".repeat(40), ciPassed: true, approvedBy: null, mergeCommit: "d".repeat(40), mergeAvailable: false } });
  });
  await page.getByRole("region", { name: "Corrigir", exact: true }).getByRole("button", { name: "Revisar e concluir" }).click();
  await review.getByRole("button", { name: "Concluir", exact: true }).click();
  await expect(review).toHaveCount(0);
  expect(completions).toEqual(["POST"]);

  await page.unrouteAll({ behavior: "wait" });
  await page.getByRole("button", { name: "Sair", exact: true }).click();
  await expect(page.getByRole("button", { name: "Entrar", exact: true })).toBeVisible();
});

test("formulário acessível cabe em tela estreita", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/");
  await expect(page.getByLabel("E-mail", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
