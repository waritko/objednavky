import test from "node:test";
import assert from "node:assert/strict";
import { checkoutCsv } from "../src/checkoutCsv.ts";

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
    '\uFEFFproduct_id,product_name,quantity\r\n"coffee-id","Káva, ""velká""",2\r\n"tea-id","Čaj\nzelený",1\r\n',
  );
});

test("partial checkout exports only the paid subset and subsequent checkout exports the remainder", () => {
  const before = { units: [unit("1"), unit("2"), unit("3")] };
  const partial = {
    units: [paid(before.units[0]), before.units[1], before.units[2]],
  };
  assert.equal(
    checkoutCsv(before, partial),
    '\uFEFFproduct_id,product_name,quantity\r\n"coffee-id","Káva, ""velká""",1\r\n',
  );
  assert.equal(
    checkoutCsv(partial, { units: partial.units.map(paid) }),
    '\uFEFFproduct_id,product_name,quantity\r\n"coffee-id","Káva, ""velká""",2\r\n',
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
  assert.ok(csv.includes('"other-id","Káva, ""velká""",1\r\n'));
  assert.ok(csv.includes('"coffee-id","New name",1\r\n'));
});
