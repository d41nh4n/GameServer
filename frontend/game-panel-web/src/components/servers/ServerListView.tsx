import type { AuthProvider, Server } from "../../auth";
import { Btn } from "../common";
import ResourceOverview from "./ResourceOverview";
import ServerCard from "./ServerCard";

export default function ServerListView({ servers, error, loading, resources, onRefresh, onSelect }: { servers: Server[]; error: string; loading: boolean; resources: any; auth?: AuthProvider; onRefresh: () => void; onSelect: (s: Server) => void }) {
  return <><ResourceOverview data={resources} /><div className="toolbar"><h2 className="section-title">Servers <span className="server-count">{servers.length}</span></h2><div className="toolbar-actions">{error && <p className="error-msg">{error}</p>}<Btn variant="ghost" onClick={onRefresh} disabled={loading}>&#8635; Refresh</Btn></div></div>{servers.length === 0 && !loading && <div className="empty-state"><span style={{ fontSize: 40 }}>📡</span><p>No servers found</p></div>}<div className="server-grid">{servers.map(s => <ServerCard key={s.id} server={s} onClick={() => onSelect(s)} />)}</div></>;
}
