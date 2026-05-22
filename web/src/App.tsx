import { useEffect, useState } from "react";
import { api } from "./api";

type Me = { id: string; email: string; displayName: string; roles: string[] };
type Device = { id: number; name: string; isOnline: boolean; lastSeenAt: string | null };
type Event = { id: number; type: number; occurredAt: string; note: string | null; user: string | null };

const TYPE_LABEL: Record<number, string> = {
  1: "Unlock requested", 2: "Unlock granted", 3: "Unlock denied",
  4: "Physical entry", 5: "Online", 6: "Offline",
};

export function App() {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => { api.me().then(setMe).catch(() => setMe(null)).finally(() => setLoading(false)); }, []);

  if (loading) return <p>Loading...</p>;
  if (!me) return <Login onLogin={() => api.me().then(setMe)} />;
  return <Dashboard me={me} onLogout={async () => { await api.logout(); setMe(null); }} />;
}

function Login({ onLogin }: { onLogin: () => void }) {
  const [email, setEmail] = useState("admin@home.local");
  const [password, setPassword] = useState("Admin123!");
  const [err, setErr] = useState("");
  return (
    <form onSubmit={async e => {
      e.preventDefault();
      try { await api.login(email, password); onLogin(); }
      catch { setErr("Invalid credentials"); }
    }} style={{ maxWidth: 320, margin: "100px auto", fontFamily: "system-ui" }}>
      <h2>Home Access</h2>
      <input value={email} onChange={e => setEmail(e.target.value)} placeholder="email" style={{ width: "100%", marginBottom: 8 }} />
      <input value={password} onChange={e => setPassword(e.target.value)} type="password" placeholder="password" style={{ width: "100%", marginBottom: 8 }} />
      <button type="submit">Sign in</button>
      {err && <p style={{ color: "crimson" }}>{err}</p>}
    </form>
  );
}

function Dashboard({ me, onLogout }: { me: Me; onLogout: () => void }) {
  const [devices, setDevices] = useState<Device[]>([]);
  const [selected, setSelected] = useState<Device | null>(null);
  const [events, setEvents] = useState<Event[]>([]);

  useEffect(() => { api.devices().then(setDevices); }, []);
  useEffect(() => { if (selected) api.events(selected.id).then(setEvents); }, [selected]);

  const unlock = async (d: Device) => {
    await api.unlock(d.id);
    if (selected?.id === d.id) api.events(d.id).then(setEvents);
  };

  return (
    <div style={{ fontFamily: "system-ui", padding: 24, maxWidth: 900, margin: "0 auto" }}>
      <header style={{ display: "flex", justifyContent: "space-between" }}>
        <h2>Home Access</h2>
        <div>{me.displayName} ({me.roles.join(",")}) <button onClick={onLogout}>Sign out</button></div>
      </header>
      <section style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 24 }}>
        <div>
          <h3>Devices</h3>
          {devices.map(d => (
            <div key={d.id} style={{ border: "1px solid #ddd", padding: 12, marginBottom: 8 }}>
              <strong>{d.name}</strong> {d.isOnline ? "🟢" : "⚪️"}
              <div>
                <button onClick={() => unlock(d)}>Unlock</button>
                <button onClick={() => setSelected(d)}>View log</button>
              </div>
            </div>
          ))}
        </div>
        <div>
          <h3>{selected ? `Log: ${selected.name}` : "Select a device"}</h3>
          <ul>{events.map(e => (
            <li key={e.id}>
              {new Date(e.occurredAt).toLocaleString()} - {TYPE_LABEL[e.type]}
              {e.user ? ` (${e.user})` : ""} {e.note ? `- ${e.note}` : ""}
            </li>
          ))}</ul>
        </div>
      </section>
    </div>
  );
}
