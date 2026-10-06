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
    const key = unit.menuItemId;
    groups.set(key, [...(groups.get(key) || []), unit]);
  }
  return [...groups.values()];
}
