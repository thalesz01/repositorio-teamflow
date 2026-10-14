import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("abrir a tarefa, escolher uma skill e executar com ela", async ({ page }) => {
  await signUp(page, "skills");
  const organization = (await (await page.request.get("/api/v1/organizations/")).json()).items[0];
  const project = (await (await page.request.get(`/api/v1/organizations/${organization.id}/projects/`)).json()).items[0];
  const csrf = await (await page.request.get("/api/v1/auth/csrf")).json();
  const created = await page.request.post(`/api/v1/organizations/${organization.id}/projects/${project.id}/tasks/`, {
    headers: { "X-CSRF-Token": csrf.token },
    data: { title: "Revisar o checkout", description: "Conferir o arredondamento.", acceptanceCriteria: ["Duas casas decimais"], assigneeId: null, dependencyIds: [] },
  });
  expect(created.status()).toBe(201);
  // The skill files themselves are covered by API tests; here only the transport and the screen are exercised.
  await page.route("**/tasks/*/skills", route => route.fulfill({ json: [
    { id: "project-claude:review", name: "code-review", description: "Revisa a mudança antes de terminar", source: "project", bytes: 120, preview: "Nomeie cada arquivo alterado." },
    { id: "user-claude:writing", name: "writing", description: "Escreve mensagens claras", source: "user", bytes: 80, preview: "Frases curtas." },
  ] }));
  const runs: { skill: string | null }[] = [];
  await page.route("**/executions/", route => {
    if (route.request().method() !== "POST") return route.fallback();
    runs.push(route.request().postDataJSON());
    return route.fulfill({ status: 201, json: { id: crypto.randomUUID(), taskId: crypto.randomUUID(), state: "queued", attempt: 0, maxAttempts: 3, maxDurationSeconds: 900, maxSteps: 50, maxCostUsd: 5,
      createdAt: new Date().toISOString(), cancellationRequested: false, failureCode: null, parentExecutionId: null, skill: "code-review" } });
  });

  await page.getByRole("button", { name: "Atualizar quadro", exact: true }).click();
  await page.getByRole("button", { name: "Abrir tarefa Revisar o checkout", exact: true }).click();
  const drawer = page.getByRole("dialog", { name: "Revisar o checkout", exact: true });
  const runner = drawer.getByRole("region", { name: "Executar com skill" });
  await expect(runner.getByRole("option", { name: "code-review · do projeto" })).toHaveCount(1);
  await expect(runner.getByRole("option", { name: "writing · pessoal" })).toHaveCount(1);
  await runner.getByRole("combobox", { name: /^Skill/ }).selectOption("project-claude:review");
  await expect(runner.getByText("Revisa a mudança antes de terminar")).toBeVisible();
  await runner.getByText("Ver o começo do texto enviado", { exact: false }).click();
  await expect(runner.getByText("Nomeie cada arquivo alterado.")).toBeVisible();
  await runner.getByRole("button", { name: "Executar com a skill" }).click();
  await expect(drawer).toHaveCount(0);
  expect(runs).toHaveLength(1);
  expect(runs[0].skill).toBe("project-claude:review");
});
