import path from "node:path";
import { expect, test } from "@playwright/test";
import { createRepository, folderRoot, signUp } from "./session";

test("registrar tarefas em sequência e executar todas a partir de uma", async ({ page }) => {
  const repository = createRepository();
  await signUp(page, "sequence");

  // First step: an ordinary task with its folder.
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  const creator = page.getByRole("dialog", { name: "Nova tarefa", exact: true });
  await expect(creator.getByLabel("Continuação de (opcional)")).toHaveCount(0);
  await creator.getByLabel("Título", { exact: true }).fill("Criar a tabela de fretes");
  await creator.getByLabel("O que fazer", { exact: true }).fill("Criar a tabela e a migração.");
  await creator.getByRole("button", { name: "Escolher pasta" }).click();
  const picker = page.getByRole("dialog", { name: "Escolher a pasta do projeto" });
  await picker.getByRole("button", { name: folderRoot, exact: false }).click();
  await picker.getByRole("button", { name: path.basename(repository), exact: false }).click();
  await picker.getByRole("button", { name: "Usar esta pasta" }).click();
  await expect(creator.getByLabel("Pasta do projeto")).toHaveValue(repository);
  await creator.getByRole("button", { name: "Criar tarefa", exact: true }).click();
  await expect(creator).toHaveCount(0);

  // Second step: continues the first one and inherits its folder.
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  await creator.getByLabel("Título", { exact: true }).fill("Usar a tabela no cálculo");
  await creator.getByLabel("O que fazer", { exact: true }).fill("Ler os fretes da tabela criada na etapa anterior.");
  await creator.getByLabel("Continuação de (opcional)").selectOption({ label: "Criar a tabela de fretes" });
  await expect(creator.getByLabel("Pasta do projeto")).toHaveValue(repository);
  await expect(creator.getByRole("button", { name: /Trocar pasta|Escolher pasta/ })).toBeDisabled();
  await expect(creator.getByRole("button", { name: "Criar e executar sequência", exact: true })).toBeVisible();
  await creator.getByRole("radio", { name: /Analisar o projeto/ }).check();
  await expect(creator.getByLabel("Continuação de (opcional)")).toHaveCount(0);
  await creator.getByRole("radio", { name: /Alterar o código/ }).check();
  await creator.getByRole("button", { name: "Criar tarefa", exact: true }).click();
  await expect(creator).toHaveCount(0);

  const created = page.getByRole("region", { name: "Criadas", exact: true });
  const first = created.locator("article", { hasText: "Criar a tabela de fretes" });
  const second = created.locator("article", { hasText: "Usar a tabela no cálculo" });
  await expect(first.getByText("Etapa 1 de 2")).toBeVisible();
  await expect(second.getByText("Etapa 2 de 2")).toBeVisible();

  // A task that already has a next step cannot be continued twice.
  await page.getByRole("button", { name: "Nova tarefa", exact: true }).click();
  await expect(creator.getByLabel("Continuação de (opcional)").getByRole("option", { name: "Criar a tabela de fretes" })).toHaveCount(0);
  await creator.getByRole("button", { name: "Cancelar", exact: true }).click();

  // Running one step runs the sequence: the first goes to the queue and the second waits for it.
  await first.getByRole("button", { name: "Executar sequência", exact: true }).click();
  await expect(page.getByRole("region", { name: "Executando", exact: true }).locator("article", { hasText: "Criar a tabela de fretes" })).toBeVisible();
  await expect(second.getByText("Na fila da sequência")).toBeVisible();
  await expect(second.getByRole("button", { name: "Executar sequência", exact: true })).toHaveCount(0);

  await second.getByRole("button", { name: "Parar sequência", exact: true }).click();
  await expect(second.getByText("Na fila da sequência")).toHaveCount(0);
  await expect(second.getByRole("button", { name: "Executar sequência", exact: true })).toBeVisible();
});
