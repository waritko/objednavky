import type { Unit } from "./types.ts";
export const money = (value: number) =>
  new Intl.NumberFormat("cs-CZ", { style: "currency", currency: "CZK" }).format(
    value,
  );
export const time = (value: string | null) =>
  value ? new Date(value).toLocaleString("cs-CZ") : "—";
export function groupUnits(units: Unit[]) {
  const groups = new Map<string, Unit[]>();
  for (const unit of units) {
    const key = JSON.stringify([
      unit.menuItemId,
      unit.itemName,
      unit.unitPrice,
      !!unit.paidAt,
      !!unit.processedAt,
      !!unit.removedAt,
    ]);
    groups.set(key, [...(groups.get(key) || []), unit]);
  }
  return [...groups.values()];
}
