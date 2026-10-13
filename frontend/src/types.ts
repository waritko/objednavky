export interface Account {
  id: number;
  username: string;
  role: "Administrator" | "Operational";
  enabled: boolean;
}
export interface Table {
  id: number;
  name: string;
  sortOrder: number;
  enabled: boolean;
}
export interface Category extends Table {
  code: string;
}
export interface Subcategory extends Table {
  categoryId: number;
}
export interface Item extends Category {
  categoryId: number;
  subcategoryId: number | null;
  priceBeforeVat: number;
  vatRate: number;
  price: number;
}
export interface Unit {
  id: number;
  menuItemId: number;
  itemName: string;
  categoryName: string;
  subcategoryName: string | null;
  unitPrice: number;
  addedAt: string;
  addedByAccountId: number;
  processedAt: string | null;
  processedByAccountId: number | null;
  paidAt: string | null;
  paidByAccountId: number | null;
  removedAt: string | null;
  removedByAccountId: number | null;
}
export interface Order {
  note: string | null;
  lineNotes: Record<string, string>;
  id: number;
  tableId: number;
  state: "Active" | "Closed";
  concurrencyToken: string;
  openedAt: string;
  closedAt: string | null;
  total: number;
  paid: number;
  unpaid: number;
  undeliveredCount: number;
  unpaidCount: number;
  units: Unit[];
}
export interface Catalog {
  tables: Table[];
  categories: Category[];
  subcategories: Subcategory[];
  items: Item[];
}
export interface Audit {
  id: number;
  unitId: number | null;
  username: string;
  action: string;
  occurredAt: string;
  detailsJson: string;
}
