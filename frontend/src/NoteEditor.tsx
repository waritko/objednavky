import { useState } from "react";

export function NoteEditor({ note, label, readOnly, busy, save }: {
  note: string | null;
  label: string;
  readOnly: boolean;
  busy: boolean;
  save: (note: string) => Promise<boolean>;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  return <div className="note-editor">
    {note && <p className="saved-note" aria-label={label}>{note}</p>}
    {!readOnly && !editing && <button className="note-toggle" disabled={busy} onClick={() => {
      setDraft(note || "");
      setEditing(true);
    }}>{note ? "Upravit poznámku" : "+ Poznámka"}<span className="sr-only">: {label}</span></button>}
    {editing && <form onSubmit={(event) => {
      event.preventDefault();
      void save(draft).then((saved) => { if (saved) setEditing(false); });
    }}>
      <label>{label}<textarea autoFocus rows={2} maxLength={1000} value={draft} disabled={busy} onChange={(event) => setDraft(event.target.value)} /></label>
      <div className="actions">
        <button className="primary" disabled={busy}>Uložit</button>
        <button type="button" disabled={busy} onClick={() => setEditing(false)}>Zrušit</button>
      </div>
    </form>}
  </div>;
}
