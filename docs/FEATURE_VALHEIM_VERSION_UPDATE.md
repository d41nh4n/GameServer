# [Pull Request] Valheim Game Version Check & SteamCMD Update

Tài liệu này tổng hợp chi tiết toàn bộ các thay đổi trong nhánh `feature/valheim-version-steamcmd-update` để hỗ trợ review code và deploy trên máy chủ `100.82.102.38`.

---

## 1. Mục tiêu và giải quyết Feature Gap
* Giải quyết hạng mục còn thiếu trong [docs/FEATURE_GAP_MATRIX.md](file:///d:/Workspace/Antigravity/Game_Server/docs/FEATURE_GAP_MATRIX.md) (**Mục 5. Install and update**).
* Đọc và hiển thị **Installed Build ID** (phiên bản đang cài) và **Latest Build ID** (phiên bản mới nhất trên nhánh public của Steam).
* Cho phép Quản trị viên cập nhật server trực tiếp từ Dashboard thông qua SteamCMD với đầy đủ cơ chế an toàn.

---

## 2. Danh sách các file thay đổi (Files Changed)

| File | Tầng (Layer) | Nội dung thay đổi chính |
| :--- | :--- | :--- |
| [ValheimMonitoringService.cs](file:///d:/Workspace/Antigravity/Game_Server/backend/Infrastructure/GameServers/ValheimMonitoringService.cs) | Backend Service | Thêm hàm đọc manifest `appmanifest_896660.acf`, gọi Steam API lấy latest version, và hàm thực thi SteamCMD cập nhật server. |
| [Program.cs](file:///d:/Workspace/Antigravity/Game_Server/backend/Api/Program.cs) | Backend API | Khai báo 2 endpoint: `GET /api/valheim/version` và `POST /api/valheim/update`. |
| [ValheimMonitoringServiceTests.cs](file:///d:/Workspace/Antigravity/Game_Server/backend/Test/ValheimMonitoringServiceTests.cs) | Backend Test | Bổ sung unit tests cho regex parse `buildid` và kiểm tra logic chặn update khi server đang chạy. |
| [auth.ts](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/auth.ts) | Frontend API Client | Khai báo 2 phương thức `valheimVersion()` và `valheimUpdate()`. |
| [ValheimStatus.tsx](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/components/servers/ValheimStatus.tsx) | Frontend UI | Thêm thẻ **Game Version & SteamCMD Update** trong tab *Status & Checks* và khung xem log SteamCMD. |

---

## 3. Chi tiết thay đổi kỹ thuật

### A. Backend (.NET Web API)

#### 1. Đọc Build ID đã cài đặt (`InstalledBuildId`)
* **Đường dẫn manifest**: `/srv/gamepanel/instances/valheim-main/server/steamapps/appmanifest_896660.acf`.
* Phương thức `ParseBuildIdFromManifest(string content)`: Dùng Regex trích xuất giá trị `"buildid"\s+"(?<buildid>\d+)"`.

#### 2. Lấy phiên bản mới nhất từ Steam (`LatestBuildId`)
* Phương thức `GetLatestBuildIdAsync(CancellationToken ct, bool forceRefresh)`:
  * Gọi API Steam: `https://api.steamcmd.net/v1/info/896660`.
  * Trích xuất `data.896660.depots.branches.public.buildid`.
  * **Caching**: Lưu cache trong **10 phút** để tránh gọi liên tục gây quá tải hoặc chậm request snapshot.

#### 3. Cập nhật qua SteamCMD (`UpdateServerAsync`)
* **Quy tắc an toàn nghiêm ngặt**:
  * Kiểm tra trạng thái service thông qua `_driver.GetStateAsync("valheim-main.service")`.
  * Nếu server đang `active` (`running`), lập tức throw `InvalidOperationException("Stop Valheim before updating via SteamCMD")`.
* **Thực thi tiến trình**:
  * Binary: `/usr/games/steamcmd` với biến môi trường `HOME=/srv/gamepanel/instances/valheim-main/runtime`.
  * Tham số: `+force_install_dir /srv/gamepanel/instances/valheim-main/server +login anonymous +app_update 896660 validate +quit`.
  * Thu thập toàn bộ log `stdout` & `stderr` để trả về cho Frontend.

#### 4. API Endpoints mới trong `Program.cs`
* `GET /api/valheim/version` *(Auth: authenticated)*:
  * Trả về: `{ success: true, installedBuildId: "...", latestBuildId: "...", updateAvailable: true/false }`.
* `POST /api/valheim/update` *(Auth: admin)*:
  * Gọi `monitoring.UpdateServerAsync()`.
  * Ghi nhật ký Audit Log hành động `VALHEIM_UPDATE`.
  * Trả về kết quả, exit code và toàn bộ log output.

---

### B. Frontend (React + Vite)

* **Giao diện trong tab "Status & Checks"** ([ValheimStatus.tsx](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/components/servers/ValheimStatus.tsx)):
  * **Installed Build**: Hiển thị số build hiện tại (hoặc Unknown nếu chưa cài).
  * **Latest Steam Build**: Hiển thị số build mới nhất từ Steam.
  * **Version Status**: Badge trạng thái `Up to date` hoặc `Update Available`.
  * **Update Policy**: Hiển thị `Blocked (Server Active)` nếu server đang chạy, hoặc `Ready (Stopped)`.
  * **Nút "Check for updates"**: Làm mới trực tiếp từ Steam API.
  * **Nút "Update Game"**:
    * Bị disabled khi server đang chạy.
    * Có popup xác nhận (`window.confirm`) trước khi chạy để tránh bấm nhầm.
    * Hộp thoại hiển thị log output của SteamCMD sau khi cập nhật xong.

---

## 4. Hướng dẫn Review & Deploy trên máy chủ Linux

Để test và deploy nhánh này trên máy chủ `100.82.102.38`:

```bash
# 1. Pull nhánh mới về máy chủ
git fetch origin feature/valheim-version-steamcmd-update
git checkout feature/valheim-version-steamcmd-update

# 2. Chạy test backend
cd backend
dotnet test

# 3. Khởi chạy lại backend API
cd Api
dotnet run --urls=http://0.0.0.0:5000
```

### Checklist kiểm thử (Acceptance Criteria)
- [ ] Mở Dashboard, vào tab **Status & Checks**.
- [ ] Xác nhận ô **Installed Build** hiển thị đúng build id trong `appmanifest_896660.acf`.
- [ ] Bấm **Check for updates** -> kiểm tra có lấy được build id mới nhất từ Steam hay không.
- [ ] Thử bấm **Update Game** khi server đang bật -> Nút bị disabled hoặc bị chặn.
- [ ] Dừng server (`Stop Valheim`) -> Nút Update sáng lên -> Bấm cập nhật -> SteamCMD chạy và hiển thị log output thành công.
