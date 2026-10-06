export interface Account {
  id: string;
  username: string;
  role: "Administrator" | "Operational";
  enabled: boolean;
}
export interface Table {
  id: string;
  name: string;
  sortOrder: number;
  enabled: boolean;
}
export interface Category extends Table {
  code: string;
}
export interface Subcategory extends Table {
  categoryId: string;
}
export interface Item extends Category {
  categoryId: string;
  subcategoryId: string | null;
  priceBeforeVat: number;
  vatRate: number;
  price: number;
}
export interface Unit {
  id: string;
  menuItemId: string;
  itemName: string;
  categoryName: string;
  subcategoryName: string | null;
  unitPrice: number;
  addedAt: string;
  addedByAccountId: string;
  processedAt: string | null;
  processedByAccountId: string | null;
  paidAt: string | null;
  paidByAccountId: string | null;
  removedAt: string | null;
  removedByAccountId: string | null;
}
export interface Order {
  note: string | null;
  lineNotes: Record<string, string>;
  id: string;
  tableId: string;
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
  id: string;
  unitId: string | null;
  username: string;
  action: string;
  occurredAt: string;
  detailsJson: string;
}
