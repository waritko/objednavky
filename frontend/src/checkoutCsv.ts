import type { Item, Order } from "./types";

function csvField(value: string): string {
  return `"${value.replaceAll('"', '""')}"`;
}

export function checkoutCsv(
  before: Order,
  after: Order,
  items: Pick<Item, "id" | "code">[],
): string | null {
  const codes = new Map(items.map((item) => [item.id, item.code]));
  const unpaidIds = new Set(
    before.units
      .filter((unit) => !unit.paidAt && !unit.removedAt)
      .map((unit) => unit.id),
  );
  const products = new Map<
    string,
    { code: string; name: string; quantity: number }
  >();
  for (const unit of after.units) {
    if (!unpaidIds.has(unit.id) || !unit.paidAt || unit.removedAt) continue;
    const key = JSON.stringify([unit.menuItemId, unit.itemName]);
    const product = products.get(key);
    if (product) product.quantity++;
    else {
      const code = codes.get(unit.menuItemId);
      if (code === undefined)
        throw new Error("Chybí kód položky pro export platby.");
      products.set(key, {
        code,
        name: unit.itemName,
        quantity: 1,
      });
    }
  }
  if (!products.size) return null;
  return (
    "\uFEFFproduct_code,product_name,quantity\r\n" +
    [...products.values()]
      .map(
        (product) =>
          `${csvField(product.code)},${csvField(product.name)},${product.quantity}\r\n`,
      )
      .join("")
  );
}

export function checkoutFilename(tableName: string, date = new Date()): string {
  const name = tableName.replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_").trim();
  const pad = (value: number, width = 2) => String(value).padStart(width, "0");
  const timestamp = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}_${pad(date.getHours())}-${pad(date.getMinutes())}-${pad(date.getSeconds())}-${pad(date.getMilliseconds(), 3)}`;
  return `platba-${name || "Stůl"}-${timestamp}.csv`;
}

export function downloadCheckoutCsv(
  before: Order,
  after: Order,
  items: Pick<Item, "id" | "code">[],
  tableName: string,
): void {
  const csv = checkoutCsv(before, after, items);
  if (!csv) return;
  const url = URL.createObjectURL(
    new Blob([csv], { type: "text/csv;charset=utf-8" }),
  );
  const link = document.createElement("a");
  link.href = url;
  link.download = checkoutFilename(tableName);
  document.body.append(link);
  try {
    link.click();
  } finally {
    link.remove();
    // Allow the browser to start reading the download before releasing the URL.
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
