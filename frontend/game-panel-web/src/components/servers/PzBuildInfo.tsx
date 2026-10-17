import { useEffect, useState } from "react";
import type { AuthProvider } from "../../auth";

type BuildInfo = { buildId: string | null; lastUpdatedUtc: string | null; error: string | null };

export default function PzBuildInfo({ auth }: { auth: AuthProvider }) {
  const [info, setInfo] = useState<BuildInfo | null>(null);

  useEffect(() => {
    let active = true;
    auth.pzBuildInfo().then(value => { if (active) setInfo(value); })
      .catch(() => { if (active) setInfo({ buildId: null, lastUpdatedUtc: null, error: "Could not read installed Steam build." }); });
    return () => { active = false; };
  }, [auth]);

  const updatedAt = info?.lastUpdatedUtc && !Number.isNaN(Date.parse(info.lastUpdatedUtc))
    ? new Date(info.lastUpdatedUtc).toLocaleString()
    : null;

  return (
    <section className="detail-section" aria-label="Project Zomboid server build">
      <h4>Project Zomboid server build</h4>
      {!info ? <p>Loading installed Steam build…</p> : info.buildId ? (
        <div className="info-grid">
          <div><span className="info-label">Steam build</span><span>{info.buildId}</span></div>
          <div><span className="info-label">Manifest updated</span><span>{updatedAt ?? "Unknown"}</span></div>
        </div>
      ) : <p><strong>Unavailable</strong>{info.error ? ` — ${info.error}` : ""}</p>}
      <small>Installed server build only; latest Steam build comparison is not enabled.</small>
    </section>
  );
}
