import { expect, test } from "@playwright/test";
import { signUp } from "./session";

test("memória candidata, validação, arquivamento e restauração", async ({ page }) => {
  await signUp(page, "memory");
  await page.getByRole("navigation", { name: "Navegação principal" }).getByRole("button", { name: "Memória", exact: true }).click();
  const form = page.getByRole("form", { name: "Editar memória" });
  await form.getByLabel("Chave da fonte").fill("docs/rounding.md");
  await form.getByLabel("Título da memória").fill("Arredondamento financeiro");
  await form.getByLabel("Conteúdo da memória").fill("Valores monetários usam duas casas decimais.");
  await form.getByRole("button", { name: "Criar fonte candidata" }).click();
  await expect(form.getByLabel("Status da memória")).toHaveValue("candidate");
  await form.getByLabel("Status da memória").selectOption("validated");
  await form.getByRole("button", { name: "Salvar versão da memória" }).click();
  await expect(page.getByText("Validada", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Arquivar", exact: true }).click();
  await expect(page.getByRole("button", { name: /^Arredondamento financeiro/ })).toHaveCount(0);
  await page.getByLabel("Mostrar arquivadas").check();
  await page.getByRole("button", { name: "Restaurar como candidata" }).click();
  await expect(page.getByRole("button", { name: "Restaurar como candidata" })).toHaveCount(0);
  await page.getByLabel("Mostrar arquivadas").uncheck();
  await page.getByRole("button", { name: /^Arredondamento financeiro/ }).click();
  await expect(form.getByLabel("Status da memória")).toHaveValue("candidate");
  await expect(form.getByLabel("Conteúdo da memória")).toHaveValue("Valores monetários usam duas casas decimais.");
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
