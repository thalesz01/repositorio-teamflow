import { expect, test } from "@playwright/test";

test("seletor navega sem conectar automaticamente e preserva caminho ao cancelar", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`folders-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar equipe", exact: true }).click();
  await page.getByLabel("Nova equipe", { exact: true }).fill("Equipe pastas UI");
  await page.getByRole("button", { name: "Criar equipe", exact: true }).click();
  await page.getByRole("button", { name: "Adicionar projeto", exact: true }).click();
  await page.getByLabel("Novo projeto", { exact: true }).fill("Projeto pastas UI");
  await page.getByRole("button", { name: "Criar projeto", exact: true }).click();
  await page.getByRole("navigation", { name: "Navegação do projeto" }).getByRole("button", { name: "Configurações", exact: true }).click();

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
  await page.route("**/repository/local/inspect", async route => {
    const input = route.request().postDataJSON() as { path: string };
    await route.fulfill({ json: { path: input.path, hasGit: false, branches: [], currentBranch: null } });
  });
  const explore = page.getByRole("button", { name: "Explorar pastas do computador", exact: true });
  await explore.click();
  const picker = page.getByRole("dialog", { name: "Escolher repositório local" });
  await expect(picker.getByRole("alert")).toContainText("Nenhuma pasta liberada para este projeto");
  await picker.getByRole("button", { name: "Tentar novamente", exact: true }).click();
  await expect(picker.getByRole("button", { name: "Usar esta pasta" })).toBeDisabled();
  await picker.getByRole("button", { name: root, exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta", exact: true }).click();
  await expect(page.getByLabel("Caminho do repositório", { exact: true })).toHaveValue(root);
  await explore.click();
  await picker.getByRole("button", { name: root, exact: false }).click();
  await picker.getByRole("button", { name: "app", exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta" }).click();
  await expect(page.getByLabel("Caminho do repositório", { exact: true })).toHaveValue(repository);
  await expect(page.getByText("Não configurada", { exact: true })).toBeVisible();
  await explore.click();
  await picker.getByRole("button", { name: "Fechar", exact: true }).press("Escape");
  await expect(picker).toHaveCount(0);
  await expect(explore).toBeFocused();
  await expect(page.getByLabel("Caminho do repositório", { exact: true })).toHaveValue(repository);
  await expect(page.getByRole("heading", { name: "Git ainda não iniciado", exact: true })).toBeVisible();
  const saves: { path: string; baseBranch: string | null; version: string | null }[] = [];
  await page.route("**/repository/local/folder", route => {
    const input = route.request().postDataJSON() as typeof saves[number]; saves.push(input);
    return route.fulfill({ json: { id: "11111111-1111-1111-1111-111111111111", provider: "local", repositoryId: 0,
      remoteUrl: input.path, baseBranch: input.baseBranch ?? "", baseCommit: input.baseBranch ? "a".repeat(40) : "",
      version: "22222222-2222-2222-2222-222222222222", revokedAt: null } });
  });
  await page.getByRole("button", { name: "Salvar configuração", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Configuração da pasta salva." })).toBeVisible();
  expect(saves[0]).toEqual({ path: repository, baseBranch: null, version: null });
  await page.route("**/repository/", route => {
    const input = saves[saves.length - 1];
    return route.fulfill({ json: { id: "11111111-1111-1111-1111-111111111111", provider: "local", repositoryId: 0,
      remoteUrl: input.path, baseBranch: input.baseBranch ?? "", baseCommit: input.baseBranch ? "a".repeat(40) : "",
      version: "22222222-2222-2222-2222-222222222222", revokedAt: null } });
  });
  await page.route("**/repository/local/inspect", route => route.fulfill({ json: { path: repository, hasGit: true, branches: ["main", "feature/local"], currentBranch: "main" } }));
  await page.getByRole("button", { name: "Verificar pasta", exact: true }).click();
  const branches = page.getByRole("combobox", { name: "Branch base", exact: true });
  await expect(branches).toHaveValue("main");
  await branches.selectOption("feature/local");
  await page.getByRole("button", { name: "Salvar configuração", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Configuração da pasta salva." })).toBeVisible();
  expect(saves[1].baseBranch).toBe("feature/local");
  expect(saves[1].version).toBe("22222222-2222-2222-2222-222222222222");
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
