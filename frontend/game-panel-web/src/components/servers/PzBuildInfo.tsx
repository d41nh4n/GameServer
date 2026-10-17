import { useEffect, useState } from "react";
import type { AuthProvider } from "../../auth";

type BuildInfo = { buildId: string | null; gameVersion: string | null; lastUpdatedUtc: string | null; error: string | null };

export default function PzBuildInfo({ auth }: { auth: AuthProvider }) {
  const [info, setInfo] = useState<BuildInfo | null>(null);

  useEffect(() => {
    let active = true;
    auth.pzBuildInfo().then(value => { if (active) setInfo(value); })
      .catch(() => { if (active) setInfo({ gameVersion: null, buildId: null, lastUpdatedUtc: null, error: "Could not read installed PZ version." }); });
    return () => { active = false; };
  }, [auth]);

  const updatedAt = info?.lastUpdatedUtc && !Number.isNaN(Date.parse(info.lastUpdatedUtc))
    ? new Date(info.lastUpdatedUtc).toLocaleString()
    : null;

  return (
    <section className="detail-section" aria-label="Project Zomboid server build">
      <h4>Project Zomboid version</h4>
      {!info ? <p>Loading PZ version…</p> : info.gameVersion ? (
        <div className="info-grid">
          <div><span className="info-label">Version</span><strong>{info.gameVersion}</strong></div>
          <div><span className="info-label">Steam build ID</span><span>{info.buildId ?? "Unknown"}</span></div>
          <div><span className="info-label">Manifest updated</span><span>{updatedAt ?? "Unknown"}</span></div>
        </div>
      ) : <p><strong>Version unavailable</strong>{info.error ? ` — ${info.error}` : " — Start PZ once to generate its server log."}</p>}
      <small>Version is read from the latest Project Zomboid server debug log.</small>
    </section>
  );
}
