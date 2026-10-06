import test from "node:test";
import assert from "node:assert/strict";
import {
  checkoutCsv as createCsv,
  checkoutFilename,
} from "../src/checkoutCsv.ts";

const items = [
  { id: "coffee-id", code: "001" },
  { id: "tea-id", code: "TEA" },
  { id: "other-id", code: "002" },
];
const checkoutCsv = (before, after) => createCsv(before, after, items);

const unit = (id, overrides = {}) => ({
  id,
  menuItemId: "coffee-id",
  itemName: 'Káva, "velká"',
  paidAt: null,
  removedAt: null,
  ...overrides,
});
const paid = (unit) => ({ ...unit, paidAt: "2026-10-06T12:00:00Z" });

test("full checkout groups newly paid quantities and excludes prior payments and removals", () => {
  const before = {
    units: [
      unit("1"),
      unit("2"),
      unit("3", { menuItemId: "tea-id", itemName: "Čaj\nzelený" }),
      unit("4", { paidAt: "earlier" }),
      unit("5", { removedAt: "earlier" }),
    ],
  };
  const after = { units: before.units.map(paid), state: "Closed" };
  assert.equal(
    checkoutCsv(before, after),
    '\uFEFFproduct_code,product_name,quantity\r\n"001","Káva, ""velká""",2\r\n"TEA","Čaj\nzelený",1\r\n',
  );
});

test("partial checkout exports only the paid subset and subsequent checkout exports the remainder", () => {
  const before = { units: [unit("1"), unit("2"), unit("3")] };
  const partial = {
    units: [paid(before.units[0]), before.units[1], before.units[2]],
  };
  assert.equal(
    checkoutCsv(before, partial),
    '\uFEFFproduct_code,product_name,quantity\r\n"001","Káva, ""velká""",1\r\n',
  );
  assert.equal(
    checkoutCsv(partial, { units: partial.units.map(paid) }),
    '\uFEFFproduct_code,product_name,quantity\r\n"001","Káva, ""velká""",2\r\n',
  );
});

test("unchanged payment state produces no file", () => {
  const order = { units: [unit("1"), unit("2", { paidAt: "earlier" })] };
  assert.equal(checkoutCsv(order, order), null);
});

test("distinct products and saved names remain distinguishable", () => {
  const before = {
    units: [
      unit("1"),
      unit("2", { menuItemId: "other-id" }),
      unit("3", { itemName: "New name" }),
    ],
  };
  const csv = checkoutCsv(before, { units: before.units.map(paid) });
  assert.ok(csv.includes('"002","Káva, ""velká""",1\r\n'));
  assert.ok(csv.includes('"001","New name",1\r\n'));
});

test("payment filename includes table name and local date/time", () => {
  assert.equal(
    checkoutFilename("Stůl 12", new Date(2026, 9, 6, 14, 5, 9, 123)),
    "platba-Stůl 12-2026-10-06_14-05-09-123.csv",
  );
  assert.equal(
    checkoutFilename(' Stůl / 12: "A" ', new Date(2026, 0, 2, 3, 4, 5, 6)),
    "platba-Stůl _ 12_ _A_-2026-01-02_03-04-05-006.csv",
  );
});
