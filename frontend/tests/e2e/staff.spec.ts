import { test, expect } from "@playwright/test";
import { readFile } from "node:fs/promises";

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
  await expect(
    page.getByRole("button", { name: "Prodeje", exact: true }),
  ).toHaveCount(0);
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
  const disclosure = page.getByRole("button", { name: /^Objednané položky/ });
  await expect(disclosure).toHaveAttribute("aria-expanded", "false");
  await expect(page.locator(".order-panel")).toBeHidden();
  await item.tap();
  await item.tap();
  await item.tap();
  const order = page.getByRole("region", { name: "Aktuální objednávka" });
  await expect(disclosure).toContainText("3 ks · 168,00");
  await expect(page.locator(".order-panel")).toBeHidden();
  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(disclosure).toBeHidden();
  await expect(order).toBeVisible();
  await page.setViewportSize(phoneViewport);
  await expect(page.locator(".order-panel")).toBeHidden();
  await disclosure.tap();
  await expect(disclosure).toHaveAttribute("aria-expanded", "true");
  await expect(order).toBeVisible();
  await disclosure.tap();
  await expect(page.locator(".order-panel")).toBeHidden();
  await expect(item).toBeVisible();
  await disclosure.tap();
  await expect(
    order.getByText(`3× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  const line = order.locator("article:not(.removed)");
  await expect(line.locator(".quantity")).toBeHidden();
  await expect(
    line.getByRole("button", {
      name: `Odebrat řádek 3× Káva ${suffix}`,
      exact: true,
    }),
  ).toBeVisible();
  expect((await line.boundingBox())!.height).toBeLessThan(152);
  // Cancel the whole grouped line directly, then add it again for service.
  await item.tap();
  await expect(
    order.getByText(`4× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await line
    .getByRole("button", {
      name: `Odebrat řádek 4× Káva ${suffix}`,
      exact: true,
    })
    .tap();
  await expect(
    order.getByRole("heading", { name: "Uzavřená objednávka" }),
  ).toBeVisible();
  await expect(order.locator(".cancel-line")).toHaveCount(0);
  await expect(order.locator(".removed")).toContainText(`4× Káva ${suffix}`);
  await page
    .getByRole("button", { name: "Nová objednávka", exact: true })
    .tap();
  await item.tap();
  await item.tap();
  await item.tap();
  await expect(
    order.getByText(`3× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  // Notes use small buttons until editing and share one line across quantities.
  await order
    .getByRole("button", {
      name: "+ Poznámka: Poznámka k objednávce",
      exact: true,
    })
    .tap();
  await order
    .getByLabel("Poznámka k objednávce", { exact: true })
    .fill("Narozeniny u stolu");
  await order.getByRole("button", { name: "Uložit", exact: true }).tap();
  await expect(order.locator("textarea")).toHaveCount(0);
  await expect(
    order.getByText("Narozeniny u stolu", { exact: true }),
  ).toBeVisible();
  await line
    .getByRole("button", {
      name: `+ Poznámka: Poznámka k položce Káva ${suffix}`,
      exact: true,
    })
    .tap();
  await line
    .getByLabel(`Poznámka k položce Káva ${suffix}`, { exact: true })
    .fill("Bez cukru");
  await line.getByRole("button", { name: "Uložit", exact: true }).tap();
  await expect(line.locator("textarea")).toHaveCount(0);
  await expect(line.getByText("Bez cukru", { exact: true })).toBeVisible();
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
  await expect(disclosure).toHaveAttribute("aria-expanded", "false");
  await disclosure.tap();
  await expect(
    order.getByText(`3× Káva ${suffix}`, { exact: true }),
  ).toBeVisible();
  await order.locator("article summary").tap();
  await order
    .getByRole("button", { name: `Vybrat další Káva ${suffix}`, exact: true })
    .tap();
  await order.getByRole("button", { name: "Vydat vybrané", exact: true }).tap();
  await expect(
    order.getByText("2 nevydaných · 3 nezaplacených", { exact: true }),
  ).toBeVisible();
  await expect(order.locator("article")).toHaveCount(1);
  await expect(line.getByText("Bez cukru", { exact: true })).toBeVisible();
  const delivered = line;
  if (
    !(await delivered
      .locator("details")
      .evaluate((details) => details.hasAttribute("open")))
  )
    await delivered.locator("summary").tap();
  await delivered
    .getByRole("button", { name: `Vybrat další Káva ${suffix}`, exact: true })
    .tap();
  const downloads: string[] = [];
  page.on("download", (download) =>
    downloads.push(download.suggestedFilename()),
  );
  await page.route("**/orders/*/paid", (route) =>
    route.fulfill({
      status: 400,
      contentType: "application/json",
      body: JSON.stringify({ message: "Platba selhala." }),
    }),
  );
  await order
    .getByRole("button", { name: "Zaplatit vybrané", exact: true })
    .tap();
  await expect(page.getByRole("alert")).toContainText("Platba selhala.");
  expect(downloads).toEqual([]);
  await page.unroute("**/orders/*/paid");
  const partialDownloadPromise = page.waitForEvent("download");
  await order
    .getByRole("button", { name: "Zaplatit vybrané", exact: true })
    .tap();
  const partialDownload = await partialDownloadPromise;
  expect(partialDownload.suggestedFilename()).toMatch(
    new RegExp(
      `^platba-Stůl ${suffix}-\\d{4}-\\d{2}-\\d{2}_\\d{2}-\\d{2}-\\d{2}-\\d{3}\\.csv$`,
    ),
  );
  const partialCsv = await readFile((await partialDownload.path())!, "utf8");
  expect(partialCsv).toMatch(
    new RegExp(
      `^\\uFEFFproduct_code,product_name,quantity\\r\\n"${suffix}","Káva ${suffix}",1\\r\\n$`,
    ),
  );
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
  const kitchenCard = page
    .locator(".order-card")
    .filter({ hasText: `Stůl ${suffix}` });
  await expect(
    kitchenCard.getByText("Narozeniny u stolu", { exact: true }),
  ).toBeVisible();
  await expect(
    kitchenCard.getByText("Bez cukru", { exact: true }),
  ).toBeVisible();
  await page
    .locator(".order-card")
    .filter({ hasText: `Stůl ${suffix}` })
    .getByRole("button", { name: "Otevřít účet" })
    .tap();
  await disclosure.tap();
  await order.getByRole("button", { name: "Vydat vše", exact: true }).tap();
  await expect(
    order.getByText("0 nevydaných · 2 nezaplacených", { exact: true }),
  ).toBeVisible();
  const fullDownloadPromise = page.waitForEvent("download");
  await order.getByRole("button", { name: "Zaplatit vše", exact: true }).tap();
  const fullDownload = await fullDownloadPromise;
  const fullCsv = await readFile((await fullDownload.path())!, "utf8");
  expect(fullCsv).toBe(partialCsv.replace(/,1\r\n$/, ",2\r\n"));
  expect(fullDownload.suggestedFilename()).not.toBe(
    partialDownload.suggestedFilename(),
  );
  expect(downloads).toHaveLength(2);
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
    .filter({ hasText: "Celkem 168,00" })
    .getByRole("button", { name: "Zobrazit účet" })
    .tap();
  await disclosure.tap();
  await expect(order.getByRole("button", { name: "Zaplatit vše" })).toHaveCount(
    0,
  );
  await expect(
    order.getByText("Narozeniny u stolu", { exact: true }),
  ).toBeVisible();
  await expect(order.getByText("Bez cukru", { exact: true })).toBeVisible();
  await expect(order.locator(".note-toggle")).toHaveCount(0);
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
  page.removeAllListeners("dialog");
  page.once("dialog", (dialog) => dialog.dismiss());
  await order
    .getByRole("button", {
      name: `Odebrat řádek 1× Káva ${suffix}`,
      exact: true,
    })
    .tap();
  await expect(
    order.getByRole("heading", { name: "Aktuální objednávka" }),
  ).toBeVisible();
  page.once("dialog", async (dialog) => {
    expect(dialog.message()).toContain("Záznam platby zůstane");
    await dialog.accept();
  });
  await order
    .getByRole("button", {
      name: `Odebrat řádek 1× Káva ${suffix}`,
      exact: true,
    })
    .tap();
  await expect(
    order.getByRole("heading", { name: "Uzavřená objednávka" }),
  ).toBeVisible();
  await expect(order.locator(".removed")).toContainText("Odebráno");
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.getByRole("button", { name: "Odhlásit", exact: true }).click();
  await page.getByLabel("Uživatelské jméno", { exact: true }).fill("admin");
  await page
    .getByLabel("Heslo", { exact: true })
    .fill("Browser-test-password-123");
  await page.getByRole("button", { name: "Přihlásit se", exact: true }).click();
  await page.getByRole("button", { name: "Prodeje", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Denní prodeje" }),
  ).toBeVisible();
  const dayButtons = page.locator('[aria-label="Den prodeje"] button');
  await expect(dayButtons).toHaveCount(7);
  const soldRow = page.getByRole("row").filter({ hasText: `Káva ${suffix}` });
  await expect(soldRow).toContainText("4 ks");
  await expect(soldRow).toContainText("224,00");
  await dayButtons.last().click();
  await expect(
    page.getByText("V tento den nebyly zaplaceny žádné položky."),
  ).toBeVisible();
  await dayButtons.first().click();
  await page.setViewportSize(phoneViewport);
  await expect(soldRow).toBeVisible();
  await page
    .getByRole("button", { name: "Obnovit prodeje", exact: true })
    .click();
  await expect(soldRow).toContainText("4 ks");
  expect(errors).toEqual([]);
});
