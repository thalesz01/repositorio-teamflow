import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, type Page } from "@playwright/test";

/** Registers a throwaway account and waits for the board: there is no team or project to create any more. */
export async function signUp(page: Page, prefix: string) {
  await page.goto("/");
  await page.getByRole("button", { name: "Criar uma conta" }).click();
  await page.getByLabel("E-mail", { exact: true }).fill(`${prefix}-${crypto.randomUUID()}@example.test`);
  await page.getByLabel("Senha", { exact: true }).fill("Local-Test-Password42!");
  await page.getByRole("button", { name: "Cadastrar", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Quadro", exact: true })).toBeVisible();
}

/** Folder the API under test may browse (Local__BrowseRoots__0); the stack in docs/development.md starts it with the same path. */
export const folderRoot = process.env.E2E_FOLDER_ROOT ?? path.join(tmpdir(), "teamflow-e2e-folders");

/** Creates a small Git repository inside the browsable root and returns its absolute path. */
export function createRepository() {
  mkdirSync(folderRoot, { recursive: true });
  const repository = mkdtempSync(path.join(folderRoot, "app-"));
  const git = (...args: string[]) => execFileSync("git", ["-c", "user.name=E2E", "-c", "user.email=e2e@example.test", ...args], { cwd: repository });
  git("init", "-b", "main");
  git("commit", "--allow-empty", "-m", "Base");
  return repository;
}
