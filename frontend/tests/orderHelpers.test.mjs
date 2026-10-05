import test from "node:test";
import assert from "node:assert/strict";
import { groupUnits } from "../src/orderHelpers.ts";

test("quantity selection separates payment, delivery, removal and price snapshots", () => {
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
  ];
  const groups = groupUnits(units);
  assert.equal(groups.length, 6);
  assert.deepEqual(
    groups[0].map((unit) => unit.id),
    ["1", "2"],
  );
  assert.equal(groups.flat().length, units.length);
});
