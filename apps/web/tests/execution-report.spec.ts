import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("relatório abre sem download, mantém texto copiável e fecha somente o modal interno", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await signUp(page, "report");
  const organizations = await (await page.request.get("/api/v1/organizations/")).json();
  const projects = await (await page.request.get(`/api/v1/organizations/${organizations.items[0].id}/projects/`)).json();
  const path = `/api/v1/organizations/${organizations.items[0].id}/projects/${projects.items[0].id}/tasks`;
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  const response = await page.request.post(path + "/", { headers: { "X-CSRF-Token": csrf.token }, data: {
    title: "Diagnosticar teste", description: "Teste do modal", acceptanceCriteria: ["Compilar"], dependencyIds: [], assigneeId: null,
  } });
  expect(response.status()).toBe(201);
  const task = await response.json();
  const execution = { id: "00000000-0000-0000-0000-000000000001", taskId: task.id, state: "failed", attempt: 1, maxAttempts: 3,
    createdAt: new Date().toISOString(), failureCode: "tool_failed", cancellationRequested: false };
  const executionPath = `${path}/${task.id}/executions/${execution.id}`;
  await page.route(`**${path}/${task.id}/executions/?*`, route => route.fulfill({ json: { items: [execution], total: 1, page: 1, pageSize: 20 } }));
  const events = [{ eventId: "1001", executionId: execution.id, sequence: 1, timestamp: execution.createdAt, attempt: 1, tool: "test", succeeded: false, hash: null }];
  const progress = { execution, estimatedCostUsd: null, inputTokens: 15200, cachedInputTokens: 11648, outputTokens: 120, events, nextCursor: "1001" };
  await page.route(`**${executionPath}/progress*`, route => route.fulfill({ json: progress }));
  await page.route(`**${executionPath}/events`, route => route.fulfill({ contentType: "text/event-stream", body: `event: state\ndata: ${JSON.stringify(progress)}\n\n` }));
  const report = { executionId: execution.id, failure: { code: "invalid_model_response", message: "A IA retornou uma resposta fora do formato esperado." }, steps: [{ tool: "test", succeeded: false, diagnostic: "error CS1002; <script>unsafe()</script>" }] };
  await page.route(`**${executionPath}/report`, route => route.fulfill({ json: report }));
  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  await page.getByRole("button", { name: "Abrir tarefa Diagnosticar teste", exact: true }).click();
  await page.getByRole("tab", { name: /Execuções/ }).click();
  await page.getByRole("tabpanel", { name: /Execuções/ }).getByRole("button", { name: "Ver progresso", exact: true }).click();
  await expect(page.getByText("Tokens: 15.200 de entrada (11.648 em cache) · 120 de saída", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Relatório da execução", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Relatório da execução", exact: true });
  await expect(dialog.getByText("A IA retornou uma resposta fora do formato esperado.", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("textbox", { name: "Conteúdo do relatório" })).toHaveValue(JSON.stringify(report, null, 2));
  await dialog.getByRole("button", { name: "Copiar relatório", exact: true }).click();
  await expect(dialog.getByRole("status")).toHaveText("Relatório copiado.");
  expect((await page.evaluate(() => navigator.clipboard.readText())).replaceAll("\r\n", "\n")).toBe(JSON.stringify(report, null, 2));
  await page.setViewportSize({ width: 375, height: 812 });
  await expect(dialog.getByRole("button", { name: "Copiado", exact: true })).toBeInViewport();
  await dialog.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole("dialog", { name: "Diagnosticar teste", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Relatório da execução", exact: true })).toBeFocused();
});
