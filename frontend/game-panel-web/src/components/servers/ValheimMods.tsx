import { useCallback, useEffect, useState, useTransition } from "react";
import type { AuthProvider, ThunderstorePackage, ThunderstoreSearchResult, ValheimMod } from "../../auth";
import { Btn } from "../common";

export default function ValheimMods({
  auth,
  serverStatus,
}: {
  auth: AuthProvider;
  serverStatus: number;
}) {
  const [subTab, setSubTab] = useState<"installed" | "thunderstore">("installed");
  const [mods, setMods] = useState<ValheimMod[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [msg, setMsg] = useState("");
  const [search, setSearch] = useState("");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [isPending, startTransition] = useTransition();

  // Thunderstore state
  const [tsQuery, setTsQuery] = useState("");
  const [tsSearching, setTsSearching] = useState(false);
  const [tsResult, setTsResult] = useState<ThunderstoreSearchResult | null>(null);
  const [installingPkg, setInstallingPkg] = useState<string | null>(null);

  // Config editor state
  const [configModal, setConfigModal] = useState<{
    name: string;
    content: string;
    saving: boolean;
  } | null>(null);

  const isServerStopped = serverStatus === 0;

  const loadMods = useCallback(async () => {
    setLoading(true);
    setMsg("");
    try {
      const res = await auth.valheimMods();
      startTransition(() => {
        setMods(res.mods || []);
      });
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Failed to load mods"}`);
    } finally {
      setLoading(false);
    }
  }, [auth]);

  useEffect(() => {
    void loadMods();
  }, [loadMods]);

  const searchThunderstore = useCallback(async (q: string, page = 1) => {
    setTsSearching(true);
    try {
      const res = await auth.valheimThunderstoreSearch(q, page, 20);
      setTsResult(res.result);
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Không thể kết nối Thunderstore"}`);
    } finally {
      setTsSearching(false);
    }
  }, [auth]);

  useEffect(() => {
    if (subTab === "thunderstore" && !tsResult && !tsSearching) {
      void searchThunderstore("", 1);
    }
  }, [subTab, tsResult, tsSearching, searchThunderstore]);

  const handleToggle = async (mod: ValheimMod) => {
    if (!isServerStopped) {
      setMsg("❌ Server must be stopped before toggling mods.");
      return;
    }
    setBusyAction(mod.relativePath);
    setMsg("");
    try {
      const res = await auth.valheimModToggle(mod.relativePath);
      setMods((prev) =>
        prev.map((m) => (m.relativePath === mod.relativePath ? res.mod : m))
      );
      setMsg(`✅ Mod ${res.mod.enabled ? "enabled" : "disabled"}: ${res.mod.name}`);
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Toggle failed"}`);
    } finally {
      setBusyAction(null);
    }
  };

  const handleDelete = async (mod: ValheimMod) => {
    if (!isServerStopped) {
      setMsg("❌ Server must be stopped before deleting mods.");
      return;
    }
    if (!window.confirm(`Are you sure you want to delete "${mod.name}" (${mod.fileName})?`)) {
      return;
    }
    setBusyAction(mod.relativePath);
    setMsg("");
    try {
      await auth.valheimModDelete(mod.relativePath);
      setMods((prev) => prev.filter((m) => m.relativePath !== mod.relativePath));
      setMsg(`✅ Deleted mod: ${mod.name}`);
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Delete failed"}`);
    } finally {
      setBusyAction(null);
    }
  };

  const handleOpenConfig = async (mod: ValheimMod) => {
    const configName = mod.configFileName || `${mod.name}.cfg`;
    setBusyAction(`cfg-${mod.relativePath}`);
    setMsg("");
    try {
      const res = await auth.valheimModConfig(configName);
      setConfigModal({
        name: configName,
        content: res.content ?? "",
        saving: false,
      });
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Failed to load config file"}`);
    } finally {
      setBusyAction(null);
    }
  };

  const handleSaveConfig = async () => {
    if (!configModal) return;
    if (!isServerStopped) {
      setMsg("❌ Server must be stopped before saving configuration.");
      return;
    }
    setConfigModal((prev) => prev ? { ...prev, saving: true } : null);
    try {
      await auth.valheimModConfigSave(configModal.name, configModal.content);
      setMsg(`✅ Config saved: ${configModal.name}`);
      setConfigModal(null);
      void loadMods();
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Failed to save config"}`);
      setConfigModal((prev) => prev ? { ...prev, saving: false } : null);
    }
  };

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) return;
    if (!isServerStopped) {
      setMsg("❌ Server must be stopped before uploading mods.");
      return;
    }

    setUploading(true);
    setMsg("");
    try {
      const res = await auth.valheimModUpload(selectedFile);
      setMsg(`✅ Uploaded successfully: ${res.files.join(", ")}`);
      setSelectedFile(null);
      const input = document.getElementById("valheim-mod-upload-input") as HTMLInputElement | null;
      if (input) input.value = "";
      void loadMods();
    } catch (e: any) {
      setMsg(`❌ ${e.message ?? "Upload failed"}`);
    } finally {
      setUploading(false);
    }
  };

  const handleInstallThunderstore = async (pkg: ThunderstorePackage) => {
    if (!isServerStopped) {
      setMsg("❌ Server must be stopped before installing mods.");
      return;
    }
    setInstallingPkg(pkg.fullName);
    setMsg("");
    try {
      const res = await auth.valheimThunderstoreInstall(pkg.downloadUrl, pkg.fullName);
      setMsg(`✅ Đã cài đặt thành công "${pkg.name}" (${res.files.length} files trích xuất).`);
      void loadMods();
    } catch (e: any) {
      setMsg(`❌ Cài đặt thất bại: ${e.message ?? e}`);
    } finally {
      setInstallingPkg(null);
    }
  };

  const handleExportModpack = async () => {
    setExporting(true);
    setMsg("");
    try {
      const blob = await auth.valheimExportModpack();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "Valheim_Client_Mods.zip";
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
      setMsg("✅ Đã xuất Modpack (Valheim_Client_Mods.zip)! Bạn bè chỉ cần giải nén đè vào thư mục Valheim là xong.");
    } catch (e: any) {
      setMsg(`❌ Lỗi xuất modpack: ${e.message ?? e}`);
    } finally {
      setExporting(false);
    }
  };

  const filteredMods = mods.filter(
    (m) =>
      m.name.toLowerCase().includes(search.toLowerCase()) ||
      m.fileName.toLowerCase().includes(search.toLowerCase())
  );

  const enabledCount = mods.filter((m) => m.enabled).length;

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  const formatDownloads = (num: number) => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`;
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`;
    return String(num);
  };

  return (
    <div className="detail-section valheim-mods">
      <div className="status-panel-header" style={{ flexWrap: "wrap", gap: "10px" }}>
        <div>
          <h4>Valheim Mods</h4>
          <p className="muted">
            Quản lý BepInEx plugins & config · {enabledCount}/{mods.length} mod đang hoạt động
          </p>
        </div>
        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          <Btn
            variant="primary"
            busy={exporting}
            onClick={handleExportModpack}
            title="Tải file zip chứa toàn bộ plugin & config đang active để gửi cho bạn bè"
          >
            📥 Xuất Modpack (.zip)
          </Btn>
          <Btn variant="ghost" busy={loading || isPending} onClick={loadMods}>
            ↻ Refresh
          </Btn>
        </div>
      </div>

      {/* Sub-tab switcher */}
      <div className="log-mode-tabs" style={{ margin: "14px 0" }}>
        <button
          className={`log-mode-tab${subTab === "installed" ? " active" : ""}`}
          onClick={() => setSubTab("installed")}
        >
          📦 Mod đã cài ({mods.length})
        </button>
        <button
          className={`log-mode-tab${subTab === "thunderstore" ? " active" : ""}`}
          onClick={() => setSubTab("thunderstore")}
        >
          🌐 Chợ Mod Thunderstore
        </button>
      </div>

      {!isServerStopped && (
        <div
          style={{
            padding: "10px 14px",
            marginBottom: "16px",
            borderRadius: "8px",
            background: "rgba(255, 123, 114, 0.15)",
            border: "1px solid rgba(255, 123, 114, 0.4)",
            color: "#ff7b72",
            fontSize: "0.85rem",
          }}
        >
          ⚠️ <strong>Server đang chạy.</strong> Cần dừng Valheim server trước khi cài đặt, bật/tắt, xóa, upload mod hoặc sửa config.
        </div>
      )}

      {msg && <p className="config-msg">{msg}</p>}

      {subTab === "installed" ? (
        <>
          {/* Upload Box */}
          <div
            style={{
              background: "var(--bg-card)",
              border: "1px solid var(--border)",
              borderRadius: "8px",
              padding: "14px",
              marginBottom: "16px",
            }}
          >
            <h5 style={{ margin: "0 0 8px 0", color: "var(--text-heading)", fontSize: "0.85rem" }}>
              📤 Upload Mod cục bộ (.dll hoặc .zip)
            </h5>
            <form onSubmit={handleUpload} style={{ display: "flex", gap: "10px", alignItems: "center", flexWrap: "wrap" }}>
              <input
                id="valheim-mod-upload-input"
                type="file"
                accept=".dll,.zip"
                disabled={!isServerStopped || uploading}
                onChange={(e) => setSelectedFile(e.target.files?.[0] ?? null)}
                style={{
                  padding: "6px 10px",
                  background: "var(--bg-input)",
                  border: "1px solid var(--border)",
                  borderRadius: "6px",
                  color: "var(--text)",
                  fontSize: "0.82rem",
                }}
              />
              <Btn
                type="submit"
                busy={uploading}
                disabled={!isServerStopped || !selectedFile || uploading}
              >
                Upload
              </Btn>
              <span className="muted" style={{ fontSize: "0.78rem" }}>
                Hỗ trợ file .dll hoặc .zip modpack (tối đa 50MB).
              </span>
            </form>
          </div>

          {/* Search and summary */}
          <div className="log-controls" style={{ marginBottom: "12px" }}>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Lọc mod theo tên..."
              className="log-filter-input"
            />
            <span className="muted" style={{ fontSize: "0.82rem" }}>
              Hiển thị {filteredMods.length} / {mods.length} mod
            </span>
          </div>

          {/* Mod list table */}
          {loading ? (
            <div className="empty-state">Đang tải danh sách mod đã cài...</div>
          ) : filteredMods.length === 0 ? (
            <div className="empty-state">
              {mods.length === 0
                ? "Chưa có mod nào trong BepInEx/plugins. Bạn có thể cài từ Thunderstore hoặc upload ở trên."
                : "Không tìm thấy mod nào khớp với từ khóa tìm kiếm."}
            </div>
          ) : (
            <div style={{ display: "grid", gap: "8px" }}>
              {filteredMods.map((mod) => {
                const isBusy = busyAction === mod.relativePath || busyAction === `cfg-${mod.relativePath}`;
                return (
                  <div
                    key={mod.relativePath}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      padding: "10px 14px",
                      borderRadius: "8px",
                      background: "var(--panel)",
                      border: "1px solid var(--border)",
                      gap: "12px",
                      flexWrap: "wrap",
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", gap: "10px", minWidth: "220px", flex: 1 }}>
                      <span
                        style={{
                          display: "inline-block",
                          padding: "2px 8px",
                          borderRadius: "12px",
                          fontSize: "0.75rem",
                          fontWeight: 600,
                          background: mod.enabled ? "rgba(126, 231, 135, 0.15)" : "rgba(139, 148, 158, 0.2)",
                          color: mod.enabled ? "#7ee787" : "#8b949e",
                          border: `1px solid ${mod.enabled ? "rgba(126, 231, 135, 0.4)" : "rgba(139, 148, 158, 0.4)"}`,
                        }}
                      >
                        {mod.enabled ? "ACTIVE" : "DISABLED"}
                      </span>
                      <div>
                        <div style={{ fontWeight: 600, color: "var(--text-heading)", fontSize: "0.88rem" }}>
                          {mod.name}
                        </div>
                        <div className="muted" style={{ fontSize: "0.78rem", fontFamily: "monospace" }}>
                          {mod.relativePath} · {formatSize(mod.sizeBytes)} · {new Date(mod.lastModifiedUtc).toLocaleDateString()}
                        </div>
                      </div>
                    </div>

                    <div className="action-btns" style={{ display: "flex", gap: "8px", alignItems: "center" }}>
                      <Btn
                        variant={mod.enabled ? "ghost" : "primary"}
                        disabled={!isServerStopped || isBusy}
                        busy={busyAction === mod.relativePath}
                        onClick={() => handleToggle(mod)}
                      >
                        {mod.enabled ? "Tắt" : "Bật"}
                      </Btn>

                      <Btn
                        variant="ghost"
                        disabled={isBusy}
                        busy={busyAction === `cfg-${mod.relativePath}`}
                        onClick={() => handleOpenConfig(mod)}
                        title="Chỉnh sửa cấu hình file .cfg"
                      >
                        ⚙️ Config
                      </Btn>

                      <Btn
                        variant="danger"
                        disabled={!isServerStopped || isBusy}
                        onClick={() => handleDelete(mod)}
                        title="Xóa mod"
                      >
                        🗑
                      </Btn>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </>
      ) : (
        /* Thunderstore Browser */
        <div>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void searchThunderstore(tsQuery, 1);
            }}
            style={{ display: "flex", gap: "10px", marginBottom: "16px", flexWrap: "wrap" }}
          >
            <input
              value={tsQuery}
              onChange={(e) => setTsQuery(e.target.value)}
              placeholder="Tìm kiếm mod trên Thunderstore (vd: ServerSync, ValheimPlus, EpicLoot)..."
              className="log-filter-input"
              style={{ flex: "1 1 300px" }}
            />
            <Btn type="submit" busy={tsSearching}>
              🔍 Tìm kiếm
            </Btn>
          </form>

          {tsSearching ? (
            <div className="empty-state">Đang tìm kiếm mod trên Thunderstore...</div>
          ) : !tsResult || tsResult.items.length === 0 ? (
            <div className="empty-state">Không tìm thấy mod nào trên Thunderstore.</div>
          ) : (
            <>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))",
                  gap: "12px",
                  marginBottom: "16px",
                }}
              >
                {tsResult.items.map((pkg) => {
                  const isInstalled = mods.some(
                    (m) =>
                      m.name.toLowerCase() === pkg.name.toLowerCase() ||
                      m.relativePath.toLowerCase().includes(pkg.name.toLowerCase())
                  );
                  const isInstalling = installingPkg === pkg.fullName;

                  return (
                    <div
                      key={pkg.fullName}
                      style={{
                        background: "var(--panel)",
                        border: "1px solid var(--border)",
                        borderRadius: "10px",
                        padding: "14px",
                        display: "flex",
                        flexDirection: "column",
                        justifyContent: "space-between",
                        gap: "10px",
                      }}
                    >
                      <div style={{ display: "flex", gap: "12px", alignItems: "flex-start" }}>
                        {pkg.iconUrl ? (
                          <img
                            src={pkg.iconUrl}
                            alt={pkg.name}
                            style={{
                              width: "52px",
                              height: "52px",
                              borderRadius: "8px",
                              objectFit: "cover",
                              background: "rgba(0,0,0,0.2)",
                              border: "1px solid var(--border)",
                              flexShrink: 0,
                            }}
                          />
                        ) : (
                          <div
                            style={{
                              width: "52px",
                              height: "52px",
                              borderRadius: "8px",
                              background: "var(--bg-card)",
                              display: "flex",
                              alignItems: "center",
                              justifyContent: "center",
                              fontSize: "1.4rem",
                              flexShrink: 0,
                            }}
                          >
                            ⚔️
                          </div>
                        )}
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div
                            style={{
                              fontWeight: 600,
                              color: "var(--text-heading)",
                              fontSize: "0.92rem",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                            }}
                            title={pkg.name}
                          >
                            {pkg.name}
                          </div>
                          <div className="muted" style={{ fontSize: "0.78rem" }}>
                            by <span style={{ color: "var(--text)" }}>{pkg.owner}</span> · v{pkg.versionNumber}
                          </div>
                          <div style={{ fontSize: "0.75rem", color: "var(--orange)", marginTop: "2px" }}>
                            🔥 {formatDownloads(pkg.downloads)} lượt tải
                          </div>
                        </div>
                      </div>

                      <p
                        className="muted"
                        style={{
                          fontSize: "0.8rem",
                          margin: 0,
                          lineHeight: "1.4",
                          display: "-webkit-box",
                          WebkitLineClamp: 3,
                          WebkitBoxOrient: "vertical",
                          overflow: "hidden",
                        }}
                      >
                        {pkg.description || "Không có mô tả."}
                      </p>

                      <div
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          alignItems: "center",
                          paddingTop: "8px",
                          borderTop: "1px solid var(--border-subtle, rgba(255,255,255,0.05))",
                        }}
                      >
                        <a
                          href={pkg.packageUrl}
                          target="_blank"
                          rel="noreferrer"
                          style={{
                            fontSize: "0.78rem",
                            color: "var(--accent)",
                            textDecoration: "none",
                          }}
                        >
                          Thunderstore ↗
                        </a>

                        <Btn
                          variant={isInstalled ? "ghost" : "primary"}
                          disabled={!isServerStopped || isInstalling || (installingPkg !== null)}
                          busy={isInstalling}
                          onClick={() => handleInstallThunderstore(pkg)}
                          title={!isServerStopped ? "Cần dừng server để cài đặt" : undefined}
                        >
                          {isInstalled ? "Cài lại" : "⚡ 1-Click Install"}
                        </Btn>
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Pagination */}
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: "10px",
                  padding: "10px 0",
                }}
              >
                <span className="muted" style={{ fontSize: "0.82rem" }}>
                  Trang {tsResult.page} / {Math.max(1, Math.ceil(tsResult.total / tsResult.pageSize))} ({tsResult.total.toLocaleString()} mods)
                </span>
                <div style={{ display: "flex", gap: "8px" }}>
                  <Btn
                    variant="ghost"
                    disabled={tsResult.page <= 1 || tsSearching}
                    onClick={() => void searchThunderstore(tsQuery, tsResult.page - 1)}
                  >
                    ← Trang trước
                  </Btn>
                  <Btn
                    variant="ghost"
                    disabled={tsResult.page * tsResult.pageSize >= tsResult.total || tsSearching}
                    onClick={() => void searchThunderstore(tsQuery, tsResult.page + 1)}
                  >
                    Trang sau →
                  </Btn>
                </div>
              </div>
            </>
          )}
        </div>
      )}

      {/* Config Editor Modal */}
      {configModal && (
        <div
          style={{
            position: "fixed",
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            background: "rgba(0, 0, 0, 0.75)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 1000,
            padding: "20px",
          }}
        >
          <div
            style={{
              background: "var(--bg-primary)",
              border: "1px solid var(--border)",
              borderRadius: "12px",
              maxWidth: "800px",
              width: "100%",
              maxHeight: "90vh",
              display: "flex",
              flexDirection: "column",
              boxShadow: "0 8px 32px rgba(0,0,0,0.5)",
              padding: "20px",
            }}
          >
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "12px",
              }}
            >
              <h4 style={{ margin: 0, color: "var(--text-heading)" }}>
                ⚙️ Config Editor: <span style={{ color: "var(--accent)" }}>{configModal.name}</span>
              </h4>
              <button
                onClick={() => setConfigModal(null)}
                style={{
                  background: "transparent",
                  border: "none",
                  color: "var(--text-muted)",
                  fontSize: "1.2rem",
                  cursor: "pointer",
                }}
              >
                ✕
              </button>
            </div>

            <p className="muted" style={{ fontSize: "0.8rem", margin: "0 0 10px 0" }}>
              Path: <code style={{ color: "var(--text-heading)" }}>BepInEx/config/{configModal.name}</code>
            </p>

            <textarea
              value={configModal.content}
              onChange={(e) => setConfigModal({ ...configModal, content: e.target.value })}
              style={{
                flex: 1,
                minHeight: "350px",
                maxHeight: "500px",
                background: "#0a0b0f",
                border: "1px solid var(--border)",
                borderRadius: "6px",
                color: "var(--text)",
                fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
                fontSize: "0.82rem",
                padding: "12px",
                lineHeight: "1.5",
                whiteSpace: "pre",
                resize: "vertical",
              }}
            />

            <div
              style={{
                display: "flex",
                justifyContent: "flex-end",
                gap: "10px",
                marginTop: "14px",
              }}
            >
              <Btn variant="ghost" onClick={() => setConfigModal(null)}>
                Hủy
              </Btn>
              <Btn
                disabled={!isServerStopped || configModal.saving}
                busy={configModal.saving}
                onClick={handleSaveConfig}
              >
                Lưu cấu hình
              </Btn>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
