import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, csrf } from "./api";
import { money, time } from "./orderHelpers";
import type { Account, Catalog, Order } from "./types";
import { OrderPanel } from "./OrderPanel";
import { Admin } from "./Admin";

type Screen = "tables" | "active" | "kitchen" | "history" | "order" | "admin";
type Tap = { tableId: string; menuItemId: string; unitId: string };
const emptyCatalog: Catalog = {
  tables: [],
  categories: [],
  subcategories: [],
  items: [],
};

export function App() {
  const [account, setAccount] = useState<Account | null>(null);
  const [starting, setStarting] = useState(true);
  const [screen, setScreen] = useState<Screen>("tables");
  const [catalog, setCatalog] = useState<Catalog>(emptyCatalog);
  const [orders, setOrders] = useState<Order[]>([]);
  const [order, setOrder] = useState<Order | null>(null);
  const [tableId, setTableId] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState(0);
  const [tapFailed, setTapFailed] = useState(false);
  const queue = useRef<Tap[]>([]);
  const draining = useRef(false);
  const [undelivered, setUndelivered] = useState(false);
  const [unpaid, setUnpaid] = useState(false);
  const [history, setHistory] = useState<{ orders: Order[]; total: number }>({
    orders: [],
    total: 0,
  });
  const [page, setPage] = useState(1);
  const locked = busy || pending > 0;

  const refresh = useCallback(async () => {
    const [tables, categories, subcategories, items, active] =
      await Promise.all([
        api<Catalog["tables"]>("/tables"),
        api<Catalog["categories"]>("/catalog/categories"),
        api<Catalog["subcategories"]>("/catalog/subcategories"),
        api<Catalog["items"]>("/catalog/items"),
        api<Order[]>("/orders"),
      ]);
    setCatalog({ tables, categories, subcategories, items });
    setOrders(active);
  }, []);
  useEffect(() => {
    void csrf()
      .then(() => api<Account>("/auth/me"))
      .then(setAccount)
      .catch((error) => {
        if (!(error instanceof ApiError && error.status === 401))
          setError(error.message);
      })
      .finally(() => setStarting(false));
    const expired = () => {
      setAccount(null);
      setError("Přihlášení vypršelo. Přihlaste se znovu.");
    };
    window.addEventListener("session-expired", expired);
    return () => window.removeEventListener("session-expired", expired);
  }, []);
  useEffect(() => {
    if (!account) return;
    void refresh().catch((error) => setError(error.message));
    const saved = sessionStorage.getItem(`taps:${account.id}`);
    if (saved) {
      try {
        queue.current = JSON.parse(saved);
        setPending(queue.current.length);
        setTapFailed(queue.current.length > 0);
      } catch {
        sessionStorage.removeItem(`taps:${account.id}`);
      }
    }
  }, [account, refresh]);
  useEffect(() => {
    if (!account || locked || screen === "admin") return;
    const timer = window.setInterval(() => {
      void refresh().catch((error) => setError(error.message));
      if (screen === "order" && order)
        void api<Order>(`/orders/${order.id}`)
          .then(setOrder)
          .catch((error) => setError(error.message));
    }, 10000);
    return () => window.clearInterval(timer);
  }, [account, locked, screen, order?.id, refresh]);
  useEffect(() => {
    if (account && screen === "history")
      void api<{ orders: Order[]; total: number }>(
        `/orders/history?page=${page}`,
      )
        .then(setHistory)
        .catch((error) => setError(error.message));
  }, [account, screen, page]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError("");
    try {
      await action();
    } catch (error) {
      setError(
        error instanceof Error ? error.message : "Akci se nepodařilo dokončit.",
      );
    } finally {
      setBusy(false);
    }
  }
  function persist() {
    if (account)
      sessionStorage.setItem(
        `taps:${account.id}`,
        JSON.stringify(queue.current),
      );
    setPending(queue.current.length);
  }
  async function drain() {
    if (draining.current) return;
    draining.current = true;
    setTapFailed(false);
    setError("");
    try {
      while (queue.current.length) {
        const tap = queue.current[0];
        const result = await api<Order>(`/tables/${tap.tableId}/units`, {
          menuItemId: tap.menuItemId,
          unitId: tap.unitId,
        });
        setTableId(tap.tableId);
        setOrder(result);
        setScreen("order");
        queue.current.shift();
        persist();
      }
      void refresh().catch((error) => setError(error.message));
    } catch (error) {
      setTapFailed(true);
      setError(error instanceof Error ? error.message : "Přidání selhalo.");
    } finally {
      draining.current = false;
    }
  }
  function add(menuItemId: string) {
    queue.current.push({ tableId, menuItemId, unitId: crypto.randomUUID() });
    persist();
    void drain();
  }
  async function openTable(id: string) {
    await run(async () => {
      const next = await api<Order | null>(`/tables/${id}/order`);
      setOrder(next);
      setTableId(id);
      setScreen("order");
    });
  }
  async function change(
    action: string,
    ids: string[] | null,
    confirmPaidRemoval = false,
  ) {
    if (!order) return;
    await run(async () => {
      try {
        setOrder(
          await api<Order>(`/orders/${order.id}/${action}`, {
            concurrencyToken: order.concurrencyToken,
            unitIds: ids,
            all: ids === null,
            confirmPaidRemoval,
          }),
        );
      } catch (error) {
        if (error instanceof ApiError && error.status === 409)
          setOrder(await api<Order>(`/orders/${order.id}`));
        throw error;
      }
      await refresh();
    });
  }
  if (starting)
    return (
      <main>
        <p role="status">Načítání…</p>
      </main>
    );
  if (!account)
    return (
      <main className="login">
        <div className="brand-mark">O</div>
        <p className="eyebrow">RESTAURACE · OBSLUHA</p>
        <h1>Objednávky</h1>
        <p>Přihlaste se ke svému pracovnímu účtu.</p>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            const data = new FormData(event.currentTarget);
            void run(async () => {
              await csrf();
              const user = await api<Account>("/auth/login", {
                username: data.get("username"),
                password: data.get("password"),
              });
              await csrf();
              setAccount(user);
              setScreen(user.role === "Administrator" ? "admin" : "tables");
            });
          }}
        >
          <label>
            Uživatelské jméno
            <input name="username" autoComplete="username" required />
          </label>
          <label>
            Heslo
            <input
              name="password"
              type="password"
              autoComplete="current-password"
              required
            />
          </label>
          <button className="primary" disabled={busy}>
            Přihlásit se
          </button>
        </form>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
      </main>
    );

  const availableCategories = catalog.categories.filter(
    (category) => category.enabled,
  );
  const currentCategory =
    availableCategories.find((category) => category.id === categoryId) ||
    availableCategories[0];
  const items = catalog.items.filter(
    (item) =>
      item.enabled &&
      item.categoryId === currentCategory?.id &&
      (!item.subcategoryId ||
        catalog.subcategories.some(
          (sub) => sub.id === item.subcategoryId && sub.enabled,
        )),
  );
  const tableName = (id: string) =>
    catalog.tables.find((table) => table.id === id)?.name || "Stůl";
  const displayOrders =
    screen === "history"
      ? history.orders
      : orders.filter(
          (order) =>
            (screen !== "kitchen" || order.undeliveredCount > 0) &&
            (!undelivered || order.undeliveredCount > 0) &&
            (!unpaid || order.unpaidCount > 0),
        );
  const chromeClass =
    account.role === "Operational" ? "operational-chrome" : undefined;
  const logoutButton = (
    <button
      disabled={locked}
      onClick={() =>
        void run(async () => {
          await api("/auth/logout", {});
          setAccount(null);
          setOrder(null);
          setCatalog(emptyCatalog);
        })
      }
    >
      Odhlásit
    </button>
  );
  const navigation = (
    <nav aria-label="Hlavní navigace">
      {(
        [
          ["tables", "Stoly"],
          ["active", "Objednávky"],
          ["kitchen", "Kuchyně"],
          ["history", "Historie"],
          ...(account.role === "Administrator" ? [["admin", "Správa"]] : []),
        ] as [Screen, string][]
      ).map(([key, label]) => (
        <button
          key={key}
          aria-current={screen === key ? "page" : undefined}
          disabled={locked}
          onClick={() => {
            setScreen(key);
            setError("");
          }}
        >
          {label}
        </button>
      ))}
    </nav>
  );
  return (
    <>
      <header className={chromeClass}>
        <div>
          <span className="eyebrow">RESTAURACE</span>
          <strong>Objednávky</strong>
        </div>
        <div className="account">
          <span>{account.username}</span>
          {logoutButton}
        </div>
      </header>
      <div className={chromeClass}>{navigation}</div>
      <main>
        {error && (
          <div role="alert" className="error">
            {error}
          </div>
        )}
        {busy && <p role="status">Ukládání…</p>}
        {pending > 0 && (
          <div className="notice" role="status">
            Čeká na přidání: {pending} ks.{" "}
            {tapFailed && (
              <>
                <button onClick={() => void drain()}>Opakovat přidání</button>
                <button
                  onClick={() => {
                    if (
                      window.confirm(
                        "Zrušit čekající přidání? Nejprve obnovte objednávku a ověřte, které kusy server již uložil.",
                      )
                    ) {
                      const pendingTable = queue.current[0]?.tableId || tableId;
                      queue.current = [];
                      persist();
                      setTapFailed(false);
                      void openTable(pendingTable);
                    }
                  }}
                >
                  Zrušit čekající
                </button>
              </>
            )}
          </div>
        )}
        {screen === "tables" && (
          <>
            <p className="eyebrow">OBSLUHA</p>
            <h1>Vyberte stůl</h1>
            <p className="muted">
              Otevřete účet nebo začněte novou objednávku.
            </p>
            <div className="tile-grid">
              {catalog.tables
                .filter((table) => table.enabled)
                .map((table) => {
                  const active = orders.find(
                    (order) => order.tableId === table.id,
                  );
                  return (
                    <button
                      className={`table-tile ${active ? "occupied" : ""}`}
                      key={table.id}
                      disabled={locked}
                      onClick={() => void openTable(table.id)}
                    >
                      <span className="badge">
                        {active ? "Otevřený účet" : "Volný stůl"}
                      </span>
                      <strong>{table.name}</strong>
                      <span>
                        {active
                          ? `${money(active.unpaid)} nezaplaceno`
                          : "Začít objednávku"}
                      </span>
                      {active && (
                        <small>{active.undeliveredCount} nevydaných</small>
                      )}
                    </button>
                  );
                })}
            </div>
            {!catalog.tables.some((table) => table.enabled) && (
              <p>Žádné dostupné stoly. Požádejte správce o nastavení.</p>
            )}
          </>
        )}
        {screen === "order" && (
          <>
            <div className="section-heading">
              <h1>{tableName(tableId)}</h1>
              <button disabled={locked} onClick={() => void openTable(tableId)}>
                Obnovit stůl
              </button>
            </div>
            <div className="ordering-layout">
              <section aria-label="Jídelní lístek">
                <h2>Jídelní lístek</h2>
                {order?.state === "Closed" ? (
                  <p>
                    Účet je uzavřený.{" "}
                    <button
                      disabled={locked}
                      onClick={() => void openTable(tableId)}
                    >
                      Nová objednávka
                    </button>
                  </p>
                ) : (
                  <>
                    <div className="tabs">
                      {availableCategories.map((category) => (
                        <button
                          key={category.id}
                          aria-pressed={category.id === currentCategory?.id}
                          onClick={() => setCategoryId(category.id)}
                        >
                          {category.name}
                        </button>
                      ))}
                    </div>
                    {[
                      null,
                      ...catalog.subcategories.filter(
                        (sub) =>
                          sub.enabled && sub.categoryId === currentCategory?.id,
                      ),
                    ].map((sub) => {
                      const group = items.filter(
                        (item) => item.subcategoryId === (sub?.id || null),
                      );
                      return (
                        group.length > 0 && (
                          <section key={sub?.id || "direct"}>
                            <h3
                              className={
                                sub ? undefined : "direct-category-heading"
                              }
                            >
                              {sub?.name || currentCategory?.name}
                            </h3>
                            <div className="menu-grid">
                              {group.map((item) => (
                                <button
                                  className="menu-item"
                                  key={item.id}
                                  disabled={busy || tapFailed}
                                  onClick={() => add(item.id)}
                                >
                                  <strong>{item.name}</strong>
                                  <span>{money(item.price)}</span>
                                  <small>
                                    {order?.units.filter(
                                      (unit) =>
                                        unit.menuItemId === item.id &&
                                        !unit.removedAt,
                                    ).length || 0}{" "}
                                    ks na účtu · +1
                                  </small>
                                </button>
                              ))}
                            </div>
                          </section>
                        )
                      );
                    })}
                    {!items.length && (
                      <p>V této kategorii nejsou dostupné položky.</p>
                    )}
                  </>
                )}
              </section>
              {order ? (
                <OrderPanel order={order} busy={locked} change={change} />
              ) : (
                <section className="order-panel">
                  <h2>Aktuální objednávka</h2>
                  <p>Zatím prázdná. Klepnutím na položku přidáte jeden kus.</p>
                  <strong>{money(0)}</strong>
                </section>
              )}
            </div>
          </>
        )}
        {["active", "kitchen", "history"].includes(screen) && (
          <>
            <h1>
              {screen === "kitchen"
                ? "Kuchyně"
                : screen === "history"
                  ? "Historie objednávek"
                  : "Aktivní objednávky"}
            </h1>
            {screen !== "history" && (
              <div className="filters">
                <label className="check">
                  <input
                    type="checkbox"
                    checked={undelivered}
                    onChange={(event) => setUndelivered(event.target.checked)}
                  />
                  Nevydané
                </label>
                <label className="check">
                  <input
                    type="checkbox"
                    checked={unpaid}
                    onChange={(event) => setUnpaid(event.target.checked)}
                  />
                  Nezaplacené
                </label>
              </div>
            )}
            <div className="tile-grid">
              {displayOrders.map((entry) => (
                <article className="order-card" key={entry.id}>
                  <div className="section-heading">
                    <h2>{tableName(entry.tableId)}</h2>
                    <span>{money(entry.unpaid)} nezaplaceno</span>
                  </div>
                  <p className="muted">{time(entry.openedAt)}</p>
                  {screen === "kitchen" && (
                    <ul>
                      {Object.values(
                        entry.units
                          .filter(
                            (unit) => !unit.removedAt && !unit.processedAt,
                          )
                          .reduce<
                            Record<
                              string,
                              { name: string; count: number; addedAt: string }
                            >
                          >((groups, unit) => {
                            const key = JSON.stringify([
                              unit.menuItemId,
                              unit.itemName,
                            ]);
                            groups[key] ??= {
                              name: unit.itemName,
                              count: 0,
                              addedAt: unit.addedAt,
                            };
                            groups[key].count++;
                            return groups;
                          }, {}),
                      ).map((group) => (
                        <li key={group.name}>
                          <strong>
                            {group.count}× {group.name}
                          </strong>
                          <br />
                          <small>{time(group.addedAt)}</small>
                        </li>
                      ))}
                    </ul>
                  )}
                  <p>
                    {entry.undeliveredCount} nevydaných · Celkem{" "}
                    {money(entry.total)}
                  </p>
                  <button
                    disabled={locked}
                    onClick={() =>
                      void run(async () => {
                        setOrder(await api<Order>(`/orders/${entry.id}`));
                        setTableId(entry.tableId);
                        setScreen("order");
                      })
                    }
                  >
                    {screen === "history" ? "Zobrazit účet" : "Otevřít účet"}
                  </button>
                </article>
              ))}
            </div>
            {!displayOrders.length && <p>Žádné objednávky k zobrazení.</p>}
            {screen === "history" && (
              <div className="actions">
                <button disabled={page === 1} onClick={() => setPage(page - 1)}>
                  Předchozí
                </button>
                <span>
                  Strana {page} · {history.total} účtů
                </span>
                <button
                  disabled={page * 25 >= history.total}
                  onClick={() => setPage(page + 1)}
                >
                  Další
                </button>
              </div>
            )}
          </>
        )}
        {screen === "admin" && account.role === "Administrator" && (
          <Admin catalog={catalog} refresh={refresh} />
        )}
      </main>
      <footer>
        {account.role === "Operational" && (
          <details className="mobile-menu">
            <summary>Menu</summary>
            {navigation}
            <div className="account">
              <span>{account.username}</span>
              {logoutButton}
            </div>
          </details>
        )}
        Objednávky · {account.role === "Administrator" ? "Správce" : "Obsluha"}
        {pending > 0 ? ` · Odesílání ${pending} ks…` : " · Připojeno"}
      </footer>
    </>
  );
}
