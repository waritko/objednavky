import { useEffect, useState } from "react";
import { api } from "./api";
import { money } from "./orderHelpers";
import type { Account, Catalog } from "./types";

type Section =
  "tables" | "categories" | "subcategories" | "items" | "accounts" | "import";
type FormValues = Record<string, string | number | boolean | null>;
const labels: Record<Section, string> = {
  tables: "Stoly",
  categories: "Kategorie",
  subcategories: "Podkategorie",
  items: "Položky",
  accounts: "Účty",
  import: "Import CSV",
};
const paths: Record<Section, string> = {
  tables: "/tables",
  categories: "/catalog/categories",
  subcategories: "/catalog/subcategories",
  items: "/catalog/items",
  accounts: "/accounts",
  import: "/catalog/import",
};
interface Preview {
  token: string | null;
  emptyRows: number;
  errors: { row: number; message: string }[];
  rows: {
    row: number;
    code: string;
    name: string;
    categoryCode: string;
    price: number;
  }[];
}

export function Admin({
  catalog,
  refresh,
}: {
  catalog: Catalog;
  refresh: () => Promise<void>;
}) {
  const [section, setSection] = useState<Section>("tables");
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [editing, setEditing] = useState<string | null>(null);
  const [values, setValues] = useState<FormValues>({
    name: "",
    sortOrder: 0,
    enabled: true,
  });
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [assignment, setAssignment] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [encoding, setEncoding] = useState("utf-8");
  const [preview, setPreview] = useState<Preview | null>(null);
  useEffect(() => {
    if (section === "accounts")
      void api<Account[]>("/accounts")
        .then(setAccounts)
        .catch((error) => setError(error.message));
  }, [section]);
  function initial(key: Section): FormValues {
    if (key === "accounts")
      return { username: "", password: "", role: "Operational", enabled: true };
    return {
      name: "",
      code: "",
      sortOrder: 0,
      enabled: true,
      categoryId: catalog.categories[0]?.id || "",
      subcategoryId: "",
      priceBeforeVat: 0,
      vatRate: 12,
    };
  }
  function choose(key: Section) {
    setSection(key);
    setEditing(null);
    setValues(initial(key));
    setError("");
    setMessage("");
    setSelected([]);
  }
  const set = (key: string, value: string | number | boolean | null) =>
    setValues((previous) => ({
      ...previous,
      [key]: value,
      ...(key === "categoryId" ? { subcategoryId: "" } : {}),
    }));
  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await action();
    } catch (error) {
      setError(error instanceof Error ? error.message : "Uložení selhalo.");
    } finally {
      setBusy(false);
    }
  }
  async function save() {
    const body: Record<string, unknown> = { ...values };
    if (section === "items") body.subcategoryId = body.subcategoryId || null;
    if (section === "accounts" && editing && body.password === "")
      body.password = null;
    await api(
      `${paths[section]}${editing ? `/${editing}` : ""}`,
      body,
      editing ? "PUT" : "POST",
    );
    await refresh();
    if (section === "accounts") setAccounts(await api<Account[]>("/accounts"));
    setEditing(null);
    setValues(initial(section));
    setMessage("Záznam byl uložen.");
  }
  const rows =
    section === "accounts"
      ? accounts
      : section === "import"
        ? []
        : catalog[section];
  const textField = (
    key: string,
    label: string,
    maxLength: number,
    type = "text",
  ) => (
    <label>
      {label}
      <input
        name={key}
        type={type}
        value={String(values[key] ?? "")}
        maxLength={maxLength}
        required={key !== "password" || !editing}
        minLength={key === "password" ? 12 : undefined}
        autoComplete={key === "password" ? "new-password" : undefined}
        onChange={(event) => set(key, event.target.value)}
      />
    </label>
  );
  const numberField = (
    key: string,
    label: string,
    step: string,
    min?: number,
    max?: number,
  ) => (
    <label>
      {label}
      <input
        type="number"
        name={key}
        value={String(values[key] ?? 0)}
        required
        step={step}
        min={min}
        max={max}
        onChange={(event) =>
          set(key, event.target.value === "" ? "" : Number(event.target.value))
        }
      />
    </label>
  );

  return (
    <>
      <p className="eyebrow">ADMINISTRACE</p>
      <h1>Správa restaurace</h1>
      <div className="tabs" aria-label="Sekce správy">
        {Object.entries(labels).map(([key, label]) => (
          <button
            key={key}
            disabled={busy}
            aria-pressed={section === key}
            onClick={() => choose(key as Section)}
          >
            {label}
          </button>
        ))}
      </div>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {message && (
        <p className="notice" role="status">
          {message}
        </p>
      )}
      {section === "import" ? (
        <section className="admin-panel">
          <h2>Import jídelního lístku</h2>
          <p>
            Nahrajte ciselnik.csv. Nejprve zkontrolujte náhled; do katalogu se
            uloží pouze platné řádky.
          </p>
          <a href="/catalog/import/template" download>
            Stáhnout šablonu CSV
          </a>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void run(async () => {
                if (!file) throw new Error("Vyberte soubor CSV.");
                if (file.size > 2_000_000)
                  throw new Error("Soubor je příliš velký (nejvýše 2 MB).");
                let csv: string;
                try {
                  csv = new TextDecoder(encoding, { fatal: true }).decode(
                    await file.arrayBuffer(),
                  );
                } catch {
                  throw new Error(
                    "Soubor nelze přečíst ve zvoleném kódování. Vyberte jiné kódování nebo opravte soubor.",
                  );
                }
                setPreview(
                  await api<Preview>("/catalog/import/preview", { csv }),
                );
              });
            }}
          >
            <label>
              Soubor CSV
              <input
                type="file"
                accept=".csv,text/csv"
                required
                onChange={(event) => {
                  setFile(event.target.files?.[0] || null);
                  setPreview(null);
                }}
              />
            </label>
            <label>
              Kódování
              <select
                value={encoding}
                onChange={(event) => {
                  setEncoding(event.target.value);
                  setPreview(null);
                }}
              >
                <option value="utf-8">UTF-8</option>
                <option value="windows-1250">Windows-1250</option>
              </select>
            </label>
            <button disabled={busy}>Zobrazit náhled</button>
          </form>
          {preview && (
            <>
              <h3>Náhled importu</h3>
              <p>
                {preview.rows.length} platných · {preview.errors.length} chyb ·{" "}
                {preview.emptyRows} prázdných řádků
              </p>
              {preview.errors.length > 0 && (
                <ul className="error">
                  {preview.errors.map((error, index) => (
                    <li key={index}>
                      Řádek {error.row}: {error.message}
                    </li>
                  ))}
                </ul>
              )}
              <div className="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Řádek</th>
                      <th>Kód</th>
                      <th>Název</th>
                      <th>Kategorie</th>
                      <th>Cena s DPH</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((row) => (
                      <tr key={row.row}>
                        <td>{row.row}</td>
                        <td>{row.code}</td>
                        <td>{row.name}</td>
                        <td>{row.categoryCode}</td>
                        <td>{money(row.price)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <button
                className="primary"
                disabled={busy || !preview.token}
                onClick={() =>
                  void run(async () => {
                    const result = await api<{
                      created: number;
                      updated: number;
                      categoriesCreated: number;
                    }>("/catalog/import/commit", { token: preview.token });
                    await refresh();
                    setPreview(null);
                    setMessage(
                      `Import dokončen: ${result.created} nových, ${result.updated} aktualizovaných položek, ${result.categoriesCreated} nových kategorií.`,
                    );
                  })
                }
              >
                Importovat {preview.rows.length} platných řádků
              </button>
            </>
          )}
        </section>
      ) : (
        <div className="admin-layout">
          <section className="admin-panel">
            <h2>
              {editing ? "Upravit záznam" : "Nový záznam"} · {labels[section]}
            </h2>
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void run(save);
              }}
            >
              {section === "accounts" ? (
                <>
                  {textField("username", "Uživatelské jméno", 100)}
                  {textField(
                    "password",
                    editing
                      ? "Nové heslo (prázdné = beze změny)"
                      : "Heslo (nejméně 12 znaků)",
                    256,
                    "password",
                  )}
                  <label>
                    Role
                    <select
                      value={String(values.role)}
                      onChange={(event) => set("role", event.target.value)}
                    >
                      <option value="Operational">Obsluha a kuchyně</option>
                      <option value="Administrator">Správce</option>
                    </select>
                  </label>
                </>
              ) : (
                <>
                  {textField(
                    "name",
                    "Název",
                    section === "items"
                      ? 200
                      : section === "tables"
                        ? 100
                        : 150,
                  )}
                  {(section === "categories" || section === "items") &&
                    textField("code", "Kód", 50)}
                  {(section === "subcategories" || section === "items") && (
                    <label>
                      Kategorie
                      <select
                        value={String(values.categoryId || "")}
                        required
                        onChange={(event) =>
                          set("categoryId", event.target.value)
                        }
                      >
                        <option value="">Vyberte kategorii</option>
                        {catalog.categories.map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                            {!category.enabled && " (zakázaná)"}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                  {section === "items" && (
                    <>
                      <label>
                        Podkategorie
                        <select
                          value={String(values.subcategoryId || "")}
                          onChange={(event) =>
                            set("subcategoryId", event.target.value)
                          }
                        >
                          <option value="">Přímo v kategorii</option>
                          {catalog.subcategories
                            .filter(
                              (sub) => sub.categoryId === values.categoryId,
                            )
                            .map((sub) => (
                              <option value={sub.id} key={sub.id}>
                                {sub.name}
                              </option>
                            ))}
                        </select>
                      </label>
                      {numberField("priceBeforeVat", "Cena bez DPH", "0.01", 0)}
                      {numberField("vatRate", "DPH (%)", "0.01", 0, 100)}
                      <p>Konečnou cenu s DPH vypočítá server při uložení.</p>
                    </>
                  )}
                  {numberField("sortOrder", "Pořadí", "1")}
                </>
              )}
              <label className="check">
                <input
                  type="checkbox"
                  checked={!!values.enabled}
                  onChange={(event) => set("enabled", event.target.checked)}
                />
                Povoleno
              </label>
              <div className="actions">
                <button className="primary" disabled={busy}>
                  Uložit
                </button>
                {editing && (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => {
                      setEditing(null);
                      setValues(initial(section));
                    }}
                  >
                    Zrušit úpravy
                  </button>
                )}
              </div>
            </form>
          </section>
          <section className="admin-panel">
            <h2>
              {labels[section]} · {rows.length}
            </h2>
            {section === "items" && (
              <details>
                <summary>
                  Hromadné přiřazení podkategorie ({selected.length} vybraných)
                </summary>
                <label>
                  Cílová podkategorie
                  <select
                    value={assignment}
                    onChange={(event) => setAssignment(event.target.value)}
                  >
                    <option value="">Bez podkategorie</option>
                    {catalog.subcategories.map((sub) => (
                      <option value={sub.id} key={sub.id}>
                        {
                          catalog.categories.find(
                            (category) => category.id === sub.categoryId,
                          )?.name
                        }{" "}
                        / {sub.name}
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  disabled={busy || !selected.length}
                  onClick={() =>
                    void run(async () => {
                      await api("/catalog/items/assign-subcategory", {
                        itemIds: selected,
                        subcategoryId: assignment || null,
                      });
                      await refresh();
                      setSelected([]);
                      setMessage("Podkategorie byla přiřazena.");
                    })
                  }
                >
                  Přiřadit vybraným
                </button>
              </details>
            )}
            {!rows.length && <p>Zatím žádné záznamy.</p>}
            {rows.map((row) => (
              <article key={row.id} className="admin-row">
                <div>
                  {section === "items" && (
                    <label className="check">
                      <input
                        type="checkbox"
                        aria-label={`Vybrat ${"name" in row ? row.name : ""}`}
                        checked={selected.includes(row.id)}
                        onChange={(event) =>
                          setSelected((previous) =>
                            event.target.checked
                              ? [...previous, row.id]
                              : previous.filter((id) => id !== row.id),
                          )
                        }
                      />
                      Vybrat
                    </label>
                  )}
                  <strong>{"username" in row ? row.username : row.name}</strong>
                  <p className="muted">
                    {row.enabled ? "Povoleno" : "Zakázáno"}
                    {"code" in row && ` · ${row.code}`}
                    {"price" in row &&
                      typeof row.price === "number" &&
                      ` · ${money(row.price)}`}
                    {"role" in row &&
                      ` · ${row.role === "Administrator" ? "Správce" : "Obsluha"}`}
                  </p>
                </div>
                <button
                  disabled={busy}
                  aria-label={`Upravit ${"username" in row ? row.username : row.name}`}
                  onClick={() => {
                    setEditing(row.id);
                    setValues({ ...row, password: "" });
                    setMessage("");
                  }}
                >
                  Upravit
                </button>
              </article>
            ))}
          </section>
        </div>
      )}
    </>
  );
}
