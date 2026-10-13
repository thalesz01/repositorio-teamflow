import { expect, test } from "@playwright/test";
import { parseUnifiedDiff } from "../features/review/unified-diff";

const diff = `Title: Fixture de revisão
Tests: tests_passed

diff --git a/Program.cs b/Program.cs
index 1111111..2222222 100644
--- a/Program.cs
+++ b/Program.cs
@@ -10,3 +10,3 @@
 // Contexto preservado
-var name = "old";
+var name = "<script>window.diffInjected=true</script>";
 return name;
@@ -20,0 +21,1 @@
+// Nova validação
diff --git a/README.md b/README.md
new file mode 100644
--- /dev/null
+++ b/README.md
@@ -0,0 +1,2 @@
+# CRUD simples
+diff --git é texto do arquivo
diff --git a/gone.txt b/gone.txt
deleted file mode 100644
--- a/gone.txt
+++ /dev/null
@@ -1 +0,0 @@
-Removido
diff --git a/image.png b/image.png
new file mode 100644
Binary files /dev/null and b/image.png differ
`;

test("parser preserva linhas, contexto, remoções, binários e caminhos Git", () => {
  const files = parseUnifiedDiff(diff + String.raw`diff --git a/old.txt b/b/new.txt
similarity index 100%
rename from old.txt
rename to b/new.txt
diff --git "a/a\303\247\303\243o.txt" "b/a\303\247\303\243o.txt"
--- "a/a\303\247\303\243o.txt"
+++ "b/a\303\247\303\243o.txt"
@@ -1 +1 @@
-old
+new
\ No newline at end of file
`);
  expect(files).toHaveLength(6);
  expect(files[0]).toMatchObject({ path: "Program.cs", additions: 2, deletions: 1 });
  expect(files[0].lines.find(line => line.kind === "removed")).toMatchObject({ before: 11 });
  expect(files[0].lines.find(line => line.kind === "removed")?.after).toBeUndefined();
  expect(files[0].lines.at(-1)).toMatchObject({ kind: "added", after: 21 });
  expect(files[1]).toMatchObject({ path: "README.md", status: "added", additions: 2 });
  expect(files[2]).toMatchObject({ path: "gone.txt", status: "removed", deletions: 1 });
  expect(files[3]).toMatchObject({ path: "image.png", binary: true });
  expect(files[4]).toMatchObject({ path: "b/new.txt", status: "renamed" });
  expect(files[5].path).toBe("ação.txt");
  expect(files[5].lines.at(-1)?.kind).toBe("meta");
});

test("página exibe diff seguro, navega por arquivos e cabe no celular", async ({ page }) => {
  const [organizationId, projectId, taskId, executionId] = Array.from({ length: 4 }, () => crypto.randomUUID());
  const project = `/api/v1/organizations/${organizationId}/projects/${projectId}`;
  const task = `${project}/tasks/${taskId}`;
  const execution = `${task}/executions/${executionId}`;
  await page.route(`**${task}`, route => route.fulfill({ json: { id: taskId, organizationId, projectId, title: "Criar CRUD simples", description: "Cadastrar e editar itens.", acceptanceCriteria: ["CRUD deve compilar"], assigneeId: null, dependencyIds: [], stage: "review", version: crypto.randomUUID(), createdAt: new Date().toISOString(), allowedTransitions: [] } }));
  await page.route(`**${execution}/publication`, route => route.fulfill({ json: { state: "published", attempts: 1, failureCode: null, pullRequest: { number: 1, url: `${project}/repository/local/reviews/${executionId}`, baseCommit: "a".repeat(40), headCommit: "b".repeat(40), branch: `teamflow/${executionId.replaceAll("-", "")}` } } }));
  await page.route(`**${project}/repository/local/reviews/${executionId}`, route => route.fulfill({ contentType: "text/plain", body: diff }));
  await page.route(`**${project}/profiles/?*`, route => route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 100 } }));
  await page.route(`**${execution}/review`, route => route.fulfill({ json: { state: "review_required", headCommit: "b".repeat(40), ciPassed: true, approvedBy: null, mergeCommit: null } }));
  await page.goto(`/reviews/${organizationId}/${projectId}/${taskId}/${executionId}`);
  await expect(page.getByRole("heading", { name: "Criar CRUD simples" })).toBeVisible();
  const table = page.getByRole("table", { name: "Diff de Program.cs" });
  await expect(table).toContainText("<script>window.diffInjected=true</script>");
  expect(await page.evaluate(() => "diffInjected" in window)).toBe(false);
  await page.getByLabel("Somente alterações").check();
  await expect(table).not.toContainText("Contexto preservado");
  await page.getByRole("button", { name: "Próximo arquivo" }).click();
  await expect(page.getByRole("table", { name: "Diff de README.md" })).toBeVisible();
  await page.getByLabel("Filtrar arquivos").fill("gone");
  const files = page.getByRole("navigation", { name: "Arquivos alterados" });
  await expect(files.getByRole("button")).toHaveCount(1);
  await files.getByRole("button").click();
  await expect(page.getByRole("table", { name: "Diff de gone.txt" })).toContainText("Removido");
  await page.getByLabel("Filtrar arquivos").fill("image");
  await files.getByRole("button").click();
  await expect(page.getByText("Arquivo binário alterado.", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "Aprovar commit revisado" })).toBeVisible();
  await page.getByLabel("Filtrar arquivos").fill("");
  await files.getByRole("button", { name: /Program.cs/ }).click();
  await page.getByLabel("Somente alterações").uncheck();
  await page.screenshot({ path: "test-results/diff-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 375, height: 812 });
  await page.screenshot({ path: "test-results/diff-mobile.png", fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
  await expect(page.getByRole("heading", { name: "Criar CRUD simples" })).toBeVisible();
});

test("revisão sem acesso exibe erro sem revelar conteúdo", async ({ page }) => {
  const ids = Array.from({ length: 4 }, () => crypto.randomUUID());
  await page.route("**/api/v1/organizations/**", route => route.fulfill({ status: 403, json: { code: "forbidden", title: "Sem acesso a este projeto." } }));
  await page.goto(`/reviews/${ids.join("/")}`);
  await expect(page.getByRole("alert").filter({ hasText: "Não foi possível abrir a revisão" })).toContainText("Sem acesso a este projeto");
  await expect(page.getByRole("table")).toHaveCount(0);
});
