// Thin fetch wrapper. credentials:"include" is critical; without it the
// browser won't send the auth cookie set by the ASP.NET server.

const j = (r: Response) => (r.ok ? r.json().catch(() => ({})) : Promise.reject(r));

export const api = {
  me:       () => fetch("/api/auth/me",     { credentials: "include" }).then(j),
  login:    (email: string, password: string) =>
              fetch("/api/auth/login", {
                method: "POST", credentials: "include",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ email, password }),
              }).then(j),
  logout:   () => fetch("/api/auth/logout", { method: "POST", credentials: "include" }).then(j),
  devices:  () => fetch("/api/devices",     { credentials: "include" }).then(j),
  unlock:   (id: number) =>
              fetch(`/api/devices/${id}/unlock`, { method: "POST", credentials: "include" }).then(j),
  events:   (id: number) =>
              fetch(`/api/devices/${id}/events`, { credentials: "include" }).then(j),
};
