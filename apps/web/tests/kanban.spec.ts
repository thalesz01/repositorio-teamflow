import { expect, test } from "@playwright/test";

test("cadastro, edição, movimentação por teclado, rejeição e fila", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`ui-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar equipe", exact: true }).click();
  await page.getByLabel("Nova equipe", { exact: true }).fill("Equipe UI");
  await page.getByRole("button", { name: "Criar equipe", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar projeto", exact: true }).click();
  await page.getByLabel("Novo projeto", { exact: true }).fill("Projeto UI");
  await page.getByRole("button", { name: "Criar projeto", exact: true }).click();
  await page.getByRole("heading", { name: "Quadro de tarefas" }).waitFor();
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const editor = page.getByRole("region", { name: "Nova tarefa", exact: true });
  await editor.getByLabel("Título", { exact: true }).fill("Corrigir frete");
  await editor.getByLabel("Descrição", { exact: true }).fill("A regra de arredondamento está incorreta.");
  await editor.getByLabel("Critérios de aceitação", { exact: true }).fill("Frete deve usar duas casas decimais.");
  await editor.getByRole("button", { name: "Criar tarefa", exact: true }).click();
  await expect(page.getByRole("region", { name: "Entrada", exact: true }).getByRole("button", { name: "Corrigir frete", exact: true })).toBeVisible();
  const detail = page.getByRole("region", { name: "Detalhe da tarefa", exact: true });
  await detail.getByLabel("Título", { exact: true }).fill("Corrigir cálculo do frete");
  await detail.getByRole("button", { name: "Salvar alterações", exact: true }).click();

  const move = async (source: string, target: string) => {
    const lane = page.getByRole("region", { name: source, exact: true });
    const selector = lane.getByRole("combobox", { name: "Mover Corrigir cálculo do frete", exact: true });
    await selector.selectOption(target);
    await selector.press("Tab");
    await page.keyboard.press("Enter");
  };
  await expect(page.getByRole("region", { name: "Entrada", exact: true }).getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();
  await move("Entrada", "analysis");
  await expect(page.getByRole("region", { name: "Em análise", exact: true }).getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();
  await move("Em análise", "ready");
  const readyLane = page.getByRole("region", { name: "Pronta", exact: true });
  await expect(readyLane.getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();

  // Another client changes the version; the UI must reload instead of moving a stale card.
  const organizations = await (await page.request.get("/api/v1/organizations/")).json();
  const organization = organizations.items.find((item: { name: string }) => item.name === "Equipe UI");
  const projects = await (await page.request.get(`/api/v1/organizations/${organization.id}/projects/`)).json();
  const taskPath = `/api/v1/organizations/${organization.id}/projects/${projects.items[0].id}/tasks`;
  const tasks = await (await page.request.get(taskPath + "/")).json();
  const task = tasks.items[0];
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  const changed = await page.request.put(`${taskPath}/${task.id}`, {
    headers: { "X-CSRF-Token": csrf.token },
    data: { title: task.title, description: task.description + " Atualizada por colega.", acceptanceCriteria: task.acceptanceCriteria,
      assigneeId: task.assigneeId, dependencyIds: task.dependencyIds, version: task.version },
  });
  expect(changed.status()).toBe(200);
  await move("Pronta", "execution");
  await expect(page.getByRole("region", { name: "Quadro de tarefas", exact: true }).getByRole("alert")).toContainText("alterada por outra pessoa");
  await expect(readyLane.getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Em execução", exact: true }).getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toHaveCount(0);
  await move("Pronta", "execution");
  await expect(page.getByRole("region", { name: "Em execução", exact: true }).getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();
  await expect(page.getByText("Na fila · tentativa 0 de 3", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Ver progresso", exact: true }).click();
  const progress = page.getByRole("region", { name: "Progresso da execução", exact: true });
  await expect(progress.getByText("Atualização ao vivo", { exact: true })).toBeVisible();
  await expect(progress.getByText("Custo estimado: US$ 0.000000", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Ver progresso", exact: true }).click();
  await expect(progress).toHaveCount(0);
  // Review UI transport fixture only. PostgreSQL API tests verify approval/CI/merge policies independently.
  const executions = await (await page.request.get(`${taskPath}/${task.id}/executions/`)).json();
  const executionPath = `${taskPath}/${task.id}/executions/${executions.items[0].id}`;
  const snapshot = await (await page.request.get(executionPath + "/progress")).json();
  const simulatedProgress = { ...snapshot, execution: { ...snapshot.execution, state: "succeeded" } };
  await page.route(`**${executionPath}/progress`, route => route.fulfill({ json: simulatedProgress }));
  await page.route(`**${executionPath}/events`, route => route.fulfill({ contentType: "text/event-stream", body: `event: state\ndata: ${JSON.stringify(simulatedProgress)}\n\n` }));
  await page.route(`**${executionPath}/publication`, route => route.fulfill({ json: {
    state: "published", attempts: 1, failureCode: null, pullRequest: { number: 7, url: "https://github.com/fixture/repo/pull/7",
      baseCommit: "a".repeat(40), headCommit: "b".repeat(40), branch: "teamflow/fixture" },
  } }));
  const reviewState = { state: "review_required", headCommit: "b".repeat(40), ciPassed: true, approvedBy: null, mergeCommit: null };
  await page.route(`**${executionPath}/review`, route => route.fulfill({ json: reviewState }));
  await page.route(`**${executionPath}/review/approve`, route => route.fulfill({ json: { ...reviewState, state: "awaiting_merge" } }));
  await page.route(`**${executionPath}/review/refresh`, route => route.fulfill({ json: { ...reviewState, state: "merged", mergeCommit: "d".repeat(40) } }));
  await page.getByRole("button", { name: "Ver progresso", exact: true }).click();
  const reviewPanel = page.getByRole("region", { name: "Revisão do PR", exact: true });
  await expect(reviewPanel).toContainText("Aguardando revisão independente");
  await reviewPanel.getByRole("button", { name: "Aprovar commit revisado" }).click();
  await expect(reviewPanel).toContainText("Aprovada, aguardando merge");
  await reviewPanel.getByRole("button", { name: "Consultar revisão e merge" }).click();
  await expect(reviewPanel).toContainText("Merge confirmado");
  await expect(reviewPanel.getByRole("button", { name: "Aprovar commit revisado" })).toBeDisabled();
  await page.getByRole("button", { name: "Ver progresso", exact: true }).click();
  // A newer successful execution must expose review without opening progress.
  const historyPath = `${taskPath}/${task.id}/executions/`;
  await page.route(`**${historyPath}?*`, route => route.fulfill({ json: {
    ...executions, total: 2, items: [
      { ...executions.items[0], state: "succeeded" },
      { ...executions.items[0], id: crypto.randomUUID(), state: "failed", failureCode: "tool_failed", createdAt: "2026-01-01T00:00:00Z" },
    ],
  } }));
  const directReview = page.getByRole("region", { name: "Revisão da execução mais recente", exact: true });
  await expect(directReview).toBeVisible({ timeout: 10000 });
  await expect(directReview.getByRole("link", { name: "Abrir PR #7" })).toBeVisible();
  await expect(directReview.getByRole("button", { name: "Aprovar commit revisado" })).toBeVisible();
  await expect(progress).toHaveCount(0);
  await expect(page.getByText(/^Execução anterior/)).toBeVisible();
  const currentTasks = await (await page.request.get(taskPath + "/")).json();
  await page.route(`**${taskPath}/?*`, route => route.fulfill({ json: {
    ...currentTasks, items: currentTasks.items.map((item: { id: string }) => item.id === task.id
      ? { ...item, stage: "review", allowedTransitions: [] } : item),
  } }));
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  const reviewLane = page.getByRole("region", { name: "Em revisão", exact: true });
  await expect(reviewLane.getByRole("button", { name: "Revisar alterações", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Fechar detalhe", exact: true }).click();
  await reviewLane.getByRole("button", { name: "Revisar alterações", exact: true }).click();
  await expect(directReview).toBeVisible();
  await page.unrouteAll({ behavior: "wait" });
  await page.getByRole("button", { name: "Cancelar execução", exact: true }).click();
  await expect(page.getByRole("region", { name: "Cancelada", exact: true }).getByRole("button", { name: "Corrigir cálculo do frete", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Sair", exact: true }).click();
  await expect(page.getByRole("button", { name: "Entrar", exact: true })).toBeVisible();
});

test("formulário acessível cabe em tela estreita", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/");
  await expect(page.getByLabel("E-mail", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
