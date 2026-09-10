export default function ServerResourceDetail({ resource }: { resource: any }) {
  if (!resource) return null;
  const tone = (value: number) => value >= 90 ? "critical" : value >= 75 ? "warning" : "normal";
  return <div className="server-resource-detail"><div className="status-panel-header"><div><h4>Live resource usage</h4><p className="muted">Realtime process snapshot · PID {resource.pid ?? "—"}</p></div><span className={`resource-dot ${resource.online ? "online" : "offline"}`} /></div><div className="server-resource-grid"><div><span>CPU</span><strong className={tone(resource.cpuPercent)}>{resource.cpuPercent.toFixed(1)}%</strong></div><div><span>RAM RSS</span><strong>{(resource.memoryRssKb / 1024).toFixed(1)} MB</strong></div><div><span>Threads</span><strong>{resource.threads}</strong></div><div><span>File descriptors</span><strong>{resource.fileDescriptors}</strong></div></div></div>;
}
