import { useId, useState, type ReactNode } from "react";
import { money } from "./orderHelpers";
import type { Order } from "./types";

export function OrderDisclosure({
  order,
  children,
}: {
  order: Order | null;
  children: ReactNode;
}) {
  const [expanded, setExpanded] = useState(false);
  const contentId = useId();
  const count = order?.units.filter((unit) => !unit.removedAt).length || 0;

  return (
    <div className="order-disclosure">
      <button
        className="order-disclosure-toggle"
        aria-expanded={expanded}
        aria-controls={contentId}
        onClick={() => setExpanded((previous) => !previous)}
      >
        <span>Objednané položky</span>
        <span className="order-disclosure-total">
          {count} ks · {money(order?.total || 0)}
          <span aria-hidden="true">{expanded ? " ▴" : " ▾"}</span>
        </span>
      </button>
      <div
        id={contentId}
        className={`order-disclosure-content${expanded ? " expanded" : ""}`}
      >
        {children}
      </div>
    </div>
  );
}
