import test from "node:test";
import assert from "node:assert/strict";
import { groupUnits } from "../src/orderHelpers.ts";

test("one line per item retains all payment, delivery, removal and price snapshots", () => {
  const base = {
    menuItemId: "coffee",
    itemName: "Káva",
    unitPrice: 42,
    paidAt: null,
    processedAt: null,
    removedAt: null,
  };
  const units = [
    { ...base, id: "1" },
    { ...base, id: "2" },
    { ...base, id: "3", paidAt: "2026-10-05" },
    { ...base, id: "4", processedAt: "2026-10-05" },
    { ...base, id: "5", removedAt: "2026-10-05" },
    { ...base, id: "6", unitPrice: 50 },
    { ...base, id: "7", itemName: "Nová káva" },
    { ...base, id: "8", menuItemId: "tea" },
  ];
  const groups = groupUnits(units);
  assert.equal(groups.length, 2);
  assert.deepEqual(
    groups[0].map((unit) => unit.id),
    ["1", "2", "3", "4", "5", "6", "7"],
  );
  assert.equal(groups.flat().length, units.length);
});
