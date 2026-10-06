import type { Order } from "./types";

function csvField(value: string): string {
  return `"${value.replaceAll('"', '""')}"`;
}

export function checkoutCsv(before: Order, after: Order): string | null {
  const unpaidIds = new Set(
    before.units
      .filter((unit) => !unit.paidAt && !unit.removedAt)
      .map((unit) => unit.id),
  );
  const products = new Map<
    string,
    { id: string; name: string; quantity: number }
  >();
  for (const unit of after.units) {
    if (!unpaidIds.has(unit.id) || !unit.paidAt || unit.removedAt) continue;
    const key = JSON.stringify([unit.menuItemId, unit.itemName]);
    const product = products.get(key);
    if (product) product.quantity++;
    else
      products.set(key, {
        id: unit.menuItemId,
        name: unit.itemName,
        quantity: 1,
      });
  }
  if (!products.size) return null;
  return (
    "\uFEFFproduct_id,product_name,quantity\r\n" +
    [...products.values()]
      .map(
        (product) =>
          `${csvField(product.id)},${csvField(product.name)},${product.quantity}\r\n`,
      )
      .join("")
  );
}

export function downloadCheckoutCsv(before: Order, after: Order): void {
  const csv = checkoutCsv(before, after);
  if (!csv) return;
  const url = URL.createObjectURL(
    new Blob([csv], { type: "text/csv;charset=utf-8" }),
  );
  const link = document.createElement("a");
  link.href = url;
  link.download = `checkout-${after.id}-${after.concurrencyToken}.csv`;
  document.body.append(link);
  try {
    link.click();
  } finally {
    link.remove();
    // Allow the browser to start reading the download before releasing the URL.
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
