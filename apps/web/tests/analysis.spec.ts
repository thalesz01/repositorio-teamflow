import path from "node:path";
import { expect, test } from "@playwright/test";
import { createRepository, folderRoot, signUp } from "./session";

test("criar uma tarefa de análise, escolher o repositório e ver a resposta", async ({ page }) => {
  const repository = createRepository();
  await signUp(page, "analysis");
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  // The kind switches the wording and hides what only matters for a change.
  await expect(dialog.getByLabel("Resultado esperado (opcional)")).toBeVisible();
  await dialog.getByRole("radio", { name: /Analisar o projeto/ }).check();
  await expect(dialog.getByLabel("Resultado esperado (opcional)")).toHaveCount(0);
  await expect(dialog.getByText("Somente leitura: nada na pasta é alterado.")).toBeVisible();
  await dialog.getByLabel("Assunto", { exact: true }).fill("Regra de desconto em POST /orders");
  await dialog.getByLabel("Pergunta", { exact: true }).fill("Qual é a regra de desconto da rota POST /orders e onde ela é aplicada?");
  await dialog.getByRole("button", { name: "Escolher pasta" }).click();
  const picker = page.getByRole("dialog", { name: "Escolher a pasta do projeto" });
  await picker.getByRole("button", { name: folderRoot, exact: false }).click();
  await picker.getByRole("button", { name: path.basename(repository), exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta" }).click();
  await dialog.getByRole("button", { name: "Criar análise", exact: true }).click();
  await expect(dialog).toHaveCount(0);

  const card = page.getByRole("region", { name: "Criadas", exact: true }).getByRole("article").filter({ hasText: "Regra de desconto em POST /orders" });
  await expect(card.getByText("Análise", { exact: true })).toBeVisible();
  await expect(card.getByRole("button", { name: "Analisar", exact: true })).toBeVisible();

  // The agent is not part of the browser test: the finished analysis and its answer are fixtures.
  const organization = (await (await page.request.get("/api/v1/organizations/")).json()).items[0];
  const project = (await (await page.request.get(`/api/v1/organizations/${organization.id}/projects/`)).json()).items[0];
  const projectPath = `/api/v1/organizations/${organization.id}/projects/${project.id}`;
  const tasks = await (await page.request.get(`${projectPath}/tasks/`)).json();
  const task = tasks.items[0];
  expect(task.kind).toBe("analysis");
  const execution = { id: crypto.randomUUID(), taskId: task.id, state: "succeeded", attempt: 1, maxAttempts: 3, maxDurationSeconds: 900, maxSteps: 50, maxCostUsd: 5,
    createdAt: new Date().toISOString(), cancellationRequested: false, failureCode: null, parentExecutionId: null, skill: null };
  await page.route(`**${projectPath}/tasks/?*`, route => route.fulfill({ json: { ...tasks, items: [{ ...task, stage: "done", allowedTransitions: [] }] } }));
  await page.route(`**${projectPath}/tasks/${task.id}/executions/?*`, route => route.fulfill({ json: { items: [execution], page: 1, pageSize: 1, total: 1 } }));
  await page.route(`**${projectPath}/tasks/${task.id}/executions/${execution.id}/progress`, route => route.fulfill({ json: { execution, estimatedCostUsd: null, inputTokens: null,
    outputTokens: null, events: [], nextCursor: null, agentSummary: "Descontos acima de 20% são recusados em Orders.cs, no método Accept." } }));
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  const done = page.getByRole("region", { name: "Concluídas", exact: true });
  await done.getByRole("button", { name: "Ver resposta" }).click();
  const answer = page.getByRole("dialog", { name: "Regra de desconto em POST /orders", exact: true });
  await expect(answer.getByRole("region", { name: "Pergunta" })).toContainText("rota POST /orders");
  await expect(answer.getByRole("region", { name: "Resposta" })).toContainText("Orders.cs, no método Accept");
  await expect(answer.getByRole("button", { name: "Copiar resposta" })).toBeEnabled();
  await answer.getByRole("button", { name: "Fechar resposta" }).press("Escape");
  await expect(answer).toHaveCount(0);
});
