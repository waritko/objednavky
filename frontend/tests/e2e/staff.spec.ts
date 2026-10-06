import { test, expect } from "@playwright/test";

test("administrator configures restaurant, then staff completes phone service flow", async ({
  page,
}, info) => {
  const suffix = info.project.name;
  page.on("dialog", (dialog) => dialog.accept());
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto("/");
  await page.getByLabel("Uživatelské jméno", { exact: true }).fill("admin");
  await page
    .getByLabel("Heslo", { exact: true })
    .fill("Browser-test-password-123");
  await page.getByRole("button", { name: "Přihlásit se", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Správa restaurace" }),
  ).toBeVisible();
  await expect(page.locator("header")).toBeVisible();
  await expect(page.getByRole("navigation")).toBeVisible();
  await page.getByLabel("Název", { exact: true }).fill(`Stůl ${suffix}`);
  await page.getByRole("button", { name: "Uložit", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Záznam byl uložen");
  await page.getByRole("button", { name: "Import CSV", exact: true }).click();
  await page.getByLabel("Soubor CSV").setInputFiles({
    name: "menu.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(
      `CISMAT,NAZMAT,DRUMAT2,PROCEN5,SAZDPH\n${suffix},Káva ${suffix},KAVA,50,12\ninvalid,Bad,KAVA,-1,12\n`,
    ),
  });
  await page.getByRole("button", { name: "Zobrazit náhled" }).click();
  await expect(
    page.getByText("1 platných · 1 chyb · 0 prázdných řádků"),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Importovat 1 platných řádků" })
    .click();
  await expect(page.getByRole("status")).toContainText("Import dokončen");
  await page.getByRole("button", { name: "Účty", exact: true }).click();
  await page
    .getByLabel("Uživatelské jméno", { exact: true })
    .fill(`staff-${suffix}`);
  await page
    .getByLabel("Heslo (nejméně 12 znaků)")
    .fill("Staff-browser-password-123");
  await page.getByRole("button", { name: "Uložit", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("Záznam byl uložen");
  await page.getByRole("button", { name: "Odhlásit", exact: true }).click();
  await page
    .getByLabel("Uživatelské jméno", { exact: true })
    .fill(`staff-${suffix}`);
  await page
    .getByLabel("Heslo", { exact: true })
    .fill("Staff-browser-password-123");
  await page.getByRole("button", { name: "Přihlásit se", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Vyberte stůl" }),
  ).toBeVisible();
  await expect(page.locator("header")).toBeHidden();
  await expect(page.getByRole("navigation")).toHaveCount(0);
  const phoneViewport = page.viewportSize()!;
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(page.locator("header")).toBeVisible();
  await expect(page.getByRole("navigation")).toBeVisible();
  await expect(page.locator(".mobile-menu")).toBeHidden();
  await page.setViewportSize(phoneViewport);
  await expect(
    page.getByRole("button", { name: "Správa", exact: true }),
  ).toHaveCount(0);
  await page
    .getByRole("button", { name: new RegExp(`Volný stůl Stůl ${suffix}`) })
    .tap();
  const item = page.getByRole("button", {
    name: new RegExp(`Káva ${suffix}.*56`),
  });
  await item.tap();
  await item.tap();
  await item.tap();
  const order = page.getByRole("region", { name: "Aktuální objednávka" });
  await expect(
    order.getByText(`3× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await page.locator(".mobile-menu summary").tap();
  await expect(
    page.getByRole("button", { name: "Odhlásit", exact: true }),
  ).toBeVisible();
  await expect(
    page
      .getByRole("navigation")
      .getByRole("button", { name: "Stoly", exact: true }),
  ).toBeEnabled();
  await page
    .getByRole("navigation")
    .getByRole("button", { name: "Stoly", exact: true })
    .tap();
  await page
    .getByRole("button", { name: new RegExp(`Otevřený účet Stůl ${suffix}`) })
    .tap();
  await expect(
    order.getByText(`3× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await order
    .getByRole("button", { name: `Vybrat další Káva ${suffix}`, exact: true })
    .tap();
  await order.getByRole("button", { name: "Vydat vybrané", exact: true }).tap();
  await expect(
    order.getByText("2 nevydaných · 3 nezaplacených", { exact: true }),
  ).toBeVisible();
  const delivered = order
    .locator("article")
    .filter({ has: page.getByText(/^Vydáno · Nezaplaceno/) });
  await delivered
    .getByRole("button", { name: `Vybrat další Káva ${suffix}`, exact: true })
    .tap();
  await order
    .getByRole("button", { name: "Zaplatit vybrané", exact: true })
    .tap();
  await expect(
    order.getByText("2 nevydaných · 2 nezaplacených", { exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: `../artifacts/${suffix}-partial-payment.png`,
    fullPage: true,
  });
  const width = await page.evaluate(() => ({
    content: document.documentElement.scrollWidth,
    viewport: window.innerWidth,
  }));
  expect(width.content).toBeLessThanOrEqual(width.viewport);
  await page
    .getByRole("navigation")
    .getByRole("button", { name: "Kuchyně", exact: true })
    .tap();
  await expect(
    page.getByText(`2× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await page
    .locator(".order-card")
    .filter({ hasText: `Stůl ${suffix}` })
    .getByRole("button", { name: "Otevřít účet" })
    .tap();
  await order.getByRole("button", { name: "Vydat vše", exact: true }).tap();
  await expect(
    order.getByText("0 nevydaných · 2 nezaplacených", { exact: true }),
  ).toBeVisible();
  await order.getByRole("button", { name: "Zaplatit vše", exact: true }).tap();
  await expect(
    order.getByRole("heading", { name: "Uzavřená objednávka" }),
  ).toBeVisible();
  await page
    .getByRole("navigation")
    .getByRole("button", { name: "Historie", exact: true })
    .tap();
  await page
    .locator(".order-card")
    .filter({ hasText: `Stůl ${suffix}` })
    .getByRole("button", { name: "Zobrazit účet" })
    .tap();
  await expect(order.getByRole("button", { name: "Zaplatit vše" })).toHaveCount(
    0,
  );
  await order.getByRole("button", { name: "Zobrazit historii změn" }).tap();
  await expect(order.locator(".audit")).toContainText(`staff-${suffix}`);
  await page
    .getByRole("button", { name: "Nová objednávka", exact: true })
    .tap();
  // The server commits the tap, but its response is lost. Retry must reuse the unit ID.
  await page.route("**/tables/*/units", async (route) => {
    await route.fetch();
    await route.abort("failed");
  });
  await item.tap();
  await expect(
    page.getByRole("button", { name: "Opakovat přidání" }),
  ).toBeVisible();
  await page.unroute("**/tables/*/units");
  await page.getByRole("button", { name: "Opakovat přidání" }).tap();
  await expect(
    order.getByText(`1× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await expect(
    order.getByText("1 nevydaných · 1 nezaplacených", { exact: true }),
  ).toBeVisible();
  await order.getByRole("button", { name: "Zaplatit vše", exact: true }).tap();
  await expect(
    order.getByText("1 nevydaných · 0 nezaplacených", { exact: true }),
  ).toBeVisible();
  await order
    .getByRole("button", { name: `Vybrat další Káva ${suffix}`, exact: true })
    .tap();
  page.removeAllListeners("dialog");
  page.once("dialog", (dialog) => dialog.dismiss());
  await order
    .getByRole("button", { name: "Odebrat vybrané", exact: true })
    .tap();
  await expect(
    order.getByRole("heading", { name: "Aktuální objednávka" }),
  ).toBeVisible();
  page.once("dialog", async (dialog) => {
    expect(dialog.message()).toContain("Záznam platby zůstane");
    await dialog.accept();
  });
  await order
    .getByRole("button", { name: "Odebrat vybrané", exact: true })
    .tap();
  await expect(
    order.getByRole("heading", { name: "Uzavřená objednávka" }),
  ).toBeVisible();
  await expect(order.locator(".removed")).toContainText("Odebráno");
  expect(errors).toEqual([]);
});
