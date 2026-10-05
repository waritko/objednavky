export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
  ) {
    super(message);
  }
}
let token = "";
export async function csrf() {
  const response = await fetch("/auth/csrf", { cache: "no-store" });
  if (!response.ok) throw new Error("Nepodařilo se připojit k serveru.");
  token = (await response.json()).token;
}
export async function api<T>(
  path: string,
  body?: unknown,
  method = "POST",
): Promise<T> {
  if (body !== undefined && !token) await csrf();
  let response: Response;
  try {
    response = await fetch(path, {
      method: body === undefined ? "GET" : method,
      cache: "no-store",
      headers:
        body === undefined
          ? {}
          : { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(
      "Spojení se serverem selhalo. Ověřte připojení a opakujte akci.",
      0,
    );
  }
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    if (
      response.status === 401 &&
      path !== "/auth/login" &&
      path !== "/auth/me"
    )
      window.dispatchEvent(new Event("session-expired"));
    throw new ApiError(
      error.message ||
        ({
          401: "Přihlášení vypršelo.",
          403: "Nemáte oprávnění.",
          429: "Příliš mnoho pokusů. Počkejte minutu.",
        }[response.status] ??
          "Akci se nepodařilo dokončit."),
      response.status,
    );
  }
  return response.status === 204 ? (null as T) : response.json();
}
