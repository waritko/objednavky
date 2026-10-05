import { useEffect, useState } from "react";
import { api } from "./api";
import { groupUnits, money, time } from "./orderHelpers";
import type { Audit, Order } from "./types";

export function OrderPanel({
  order,
  busy,
  change,
}: {
  order: Order;
  busy: boolean;
  change: (
    action: string,
    ids: string[] | null,
    confirm?: boolean,
  ) => Promise<void>;
}) {
  const [selected, setSelected] = useState<string[]>([]);
  const [audit, setAudit] = useState<Audit[] | null>(null);
  const [auditError, setAuditError] = useState("");
  useEffect(() => {
    setSelected([]);
    setAudit(null);
  }, [order.id, order.concurrencyToken]);
  const closed = order.state === "Closed";
  const units = order.units.filter(
    (unit) => selected.includes(unit.id) && !unit.removedAt,
  );
  const payable = units.filter((unit) => !unit.paidAt);
  const amount =
    payable.reduce((sum, unit) => sum + Math.round(unit.unitPrice * 100), 0) /
    100;
  async function act(action: string, all = false) {
    const target = all ? order.units.filter((unit) => !unit.removedAt) : units;
    if (
      action === "paid" &&
      !window.confirm(
        `Označit jako zaplacené: ${money(all ? order.unpaid : amount)}? Platba probíhá mimo aplikaci.`,
      )
    )
      return;
    const paidRemoval =
      action === "removed" && target.some((unit) => unit.paidAt);
    if (
      action === "removed" &&
      !window.confirm(
        paidRemoval
          ? "Odebrat zaplacené položky? Záznam platby zůstane zachován. Vrácení platby se neprovádí."
          : "Odebrat vybrané položky z objednávky?",
      )
    )
      return;
    await change(
      action,
      all ? null : target.map((unit) => unit.id),
      paidRemoval,
    );
  }
  return (
    <section className="order-panel" aria-label="Aktuální objednávka">
      <div className="section-heading">
        <h2>{closed ? "Uzavřená objednávka" : "Aktuální objednávka"}</h2>
        <span className="badge">
          {order.units.filter((unit) => !unit.removedAt).length} ks
        </span>
      </div>
      <p className="muted">
        Otevřeno {time(order.openedAt)}
        {closed && <> · Uzavřeno {time(order.closedAt)}</>}
      </p>
      <div className="totals">
        <span>
          Celkem <strong>{money(order.total)}</strong>
        </span>
        <span>
          Nezaplaceno <strong>{money(order.unpaid)}</strong>
        </span>
      </div>
      <p>
        {order.undeliveredCount} nevydaných · {order.unpaidCount} nezaplacených
      </p>
      <div className="unit-list">
        {groupUnits(order.units).map((group) => {
          const unit = group[0];
          const count = group.filter((unit) =>
            selected.includes(unit.id),
          ).length;
          const choose = (quantity: number) =>
            setSelected((previous) => [
              ...previous.filter((id) => !group.some((unit) => unit.id === id)),
              ...group.slice(0, quantity).map((unit) => unit.id),
            ]);
          return (
            <article
              key={unit.id}
              className={`unit ${unit.removedAt ? "removed" : ""}`}
            >
              <div className="section-heading">
                <strong>
                  {group.length}× {unit.itemName}
                </strong>
                <span>{money(unit.unitPrice * group.length)}</span>
              </div>
              <p className="muted">
                {unit.removedAt
                  ? "Odebráno"
                  : `${unit.processedAt ? "Vydáno" : "Nevydáno"} · ${unit.paidAt ? "Zaplaceno" : "Nezaplaceno"}`}{" "}
                · {time(unit.addedAt)}
              </p>
              {!closed && !unit.removedAt && (
                <div className="quantity" aria-label={`Výběr ${unit.itemName}`}>
                  <button
                    disabled={busy || count === 0}
                    onClick={() => choose(count - 1)}
                    aria-label={`Ubrat výběr ${unit.itemName}`}
                  >
                    −
                  </button>
                  <span>
                    Vybráno {count} / {group.length}
                  </span>
                  <button
                    disabled={busy || count === group.length}
                    onClick={() => choose(count + 1)}
                    aria-label={`Vybrat další ${unit.itemName}`}
                  >
                    +
                  </button>
                </div>
              )}
              <details>
                <summary>Jednotlivé kusy a časy</summary>
                {group.map((unit, index) => (
                  <div key={unit.id} className="unit-detail">
                    {!closed && !unit.removedAt ? (
                      <label className="check">
                        <input
                          type="checkbox"
                          checked={selected.includes(unit.id)}
                          disabled={busy}
                          onChange={(event) =>
                            setSelected((previous) =>
                              event.target.checked
                                ? [...previous, unit.id]
                                : previous.filter((id) => id !== unit.id),
                            )
                          }
                        />
                        Kus {index + 1}
                      </label>
                    ) : (
                      <strong>Kus {index + 1}</strong>
                    )}
                    <small>
                      Přidáno {time(unit.addedAt)}
                      <br />
                      Vydáno {time(unit.processedAt)}
                      <br />
                      Zaplaceno {time(unit.paidAt)}
                      {unit.removedAt && (
                        <>
                          <br />
                          Odebráno {time(unit.removedAt)}
                        </>
                      )}
                    </small>
                  </div>
                ))}
              </details>
            </article>
          );
        })}
      </div>
      {!closed && (
        <div className="action-panel">
          <p aria-live="polite">
            Vybráno {units.length} ks · K úhradě {money(amount)}
          </p>
          <div className="actions">
            <button
              disabled={busy || !units.some((unit) => !unit.processedAt)}
              onClick={() => void act("processed")}
            >
              Vydat vybrané
            </button>
            <button
              className="primary"
              disabled={busy || !payable.length}
              onClick={() => void act("paid")}
            >
              Zaplatit vybrané
            </button>
            <button
              className="danger"
              disabled={busy || !units.length}
              onClick={() => void act("removed")}
            >
              Odebrat vybrané
            </button>
          </div>
          <div className="actions">
            <button
              disabled={busy || !order.undeliveredCount}
              onClick={() => void act("processed", true)}
            >
              Vydat vše
            </button>
            <button
              disabled={busy || !order.unpaidCount}
              onClick={() => void act("paid", true)}
            >
              Zaplatit vše
            </button>
          </div>
        </div>
      )}
      <button
        onClick={() => {
          void api<Audit[]>(`/orders/${order.id}/audit`)
            .then(setAudit)
            .catch((error) => setAuditError(error.message));
        }}
      >
        Zobrazit historii změn
      </button>
      {auditError && <p role="alert">{auditError}</p>}
      {audit && (
        <ol className="audit">
          {audit.map((entry) => (
            <li key={entry.id}>
              {(
                {
                  Added: "Přidáno",
                  paid: "Zaplaceno",
                  processed: "Vydáno",
                  removed: "Odebráno",
                  Closed: "Uzavřeno",
                } as Record<string, string>
              )[entry.action] || entry.action}{" "}
              · {entry.username} · {time(entry.occurredAt)}
              {entry.unitId && (
                <span>
                  {" "}
                  ·{" "}
                  {
                    order.units.find((unit) => unit.id === entry.unitId)
                      ?.itemName
                  }
                </span>
              )}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
