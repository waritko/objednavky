import { useEffect, useState } from "react";
import { api } from "./api";
import { money } from "./orderHelpers";

interface SalesReport {
  timeZone: string;
  days: {
    date: string;
    quantity: number;
    total: number;
    items: {
      menuItemId: string;
      itemName: string;
      quantity: number;
      total: number;
    }[];
  }[];
}

export function Sales() {
  const [report, setReport] = useState<SalesReport | null>(null);
  const [date, setDate] = useState("");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  async function load() {
    setBusy(true);
    setError("");
    try {
      const next = await api<SalesReport>("/orders/sales");
      setReport(next);
      setDate((previous) =>
        next.days.some((day) => day.date === previous)
          ? previous
          : next.days[0].date,
      );
    } catch (error) {
      setReport(null);
      setError(
        error instanceof Error ? error.message : "Načtení prodejů selhalo.",
      );
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    void load();
  }, []);
  const day = report?.days.find((day) => day.date === date);
  const label = (date: string) =>
    new Intl.DateTimeFormat("cs-CZ", {
      dateStyle: "full",
      timeZone: "UTC",
    }).format(new Date(`${date}T12:00:00Z`));
  return (
    <>
      <p className="eyebrow">ADMINISTRACE</p>
      <div className="section-heading">
        <h1>Denní prodeje</h1>
        <button disabled={busy} onClick={() => void load()}>
          Obnovit prodeje
        </button>
      </div>
      <p className="muted">
        Zaplacené položky za posledních 7 dní včetně dneška. Den se určuje podle
        času platby v Praze. Zahrnuje i zaplacené položky později odebrané bez
        vrácení platby.
      </p>
      {busy && <p role="status">Načítání prodejů…</p>}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {report && (
        <div className="tabs" aria-label="Den prodeje">
          {report.days.map((day, index) => (
            <button
              key={day.date}
              aria-pressed={date === day.date}
              onClick={() => setDate(day.date)}
            >
              {index === 0 ? "Dnes · " : ""}
              {label(day.date)}
            </button>
          ))}
        </div>
      )}
      {day && (
        <section className="admin-panel" aria-label="Prodané položky">
          <h2>{label(day.date)}</h2>
          <p>
            {day.quantity} ks · Celkem {money(day.total)}
          </p>
          {day.items.length === 0 ? (
            <p>V tento den nebyly zaplaceny žádné položky.</p>
          ) : (
            <div className="table-scroll">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Položka</th>
                    <th scope="col">Počet</th>
                    <th scope="col">Zaplaceno</th>
                  </tr>
                </thead>
                <tbody>
                  {day.items.map((item) => (
                    <tr key={`${item.menuItemId}:${item.itemName}`}>
                      <td>{item.itemName}</td>
                      <td>{item.quantity} ks</td>
                      <td>{money(item.total)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <th scope="row">Celkem</th>
                    <td>{day.quantity} ks</td>
                    <td>{money(day.total)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </section>
      )}
    </>
  );
}
