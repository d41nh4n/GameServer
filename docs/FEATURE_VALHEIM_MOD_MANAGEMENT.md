# Feature: Quản lý Mod Valheim (BepInEx, Thunderstore API & Client Modpack Export)

Tài liệu tóm tắt kiến trúc, thay đổi và hướng dẫn kiểm thử phục vụ review Pull Request.

---

## 1. Tổng quan (Overview)

Tính năng cung cấp giải pháp toàn diện để quản lý Mod cho dedicated server Valheim (trên nền BepInEx), bao gồm:
1. **Quản lý mod cục bộ**: Quét file `.dll` trong `BepInEx/plugins/`, bật/tắt mod bằng cách đổi đuôi `.dll` ↔ `.dll.disabled`, xóa mod, chỉnh sửa trực tiếp các file cấu hình `.cfg` trong `BepInEx/config/`, và upload mod thủ công (`.dll` hoặc `.zip`).
2. **Tích hợp chợ mod Thunderstore**: Tìm kiếm toàn văn qua API công khai của Thunderstore (`valheim.thunderstore.io`), có cache bộ nhớ và cơ chế cài đặt 1-Click tự động tải, kiểm tra an toàn và giải nén vào server.
3. **Đồng bộ mod cho người chơi (Client Modpack Export)**: Nút xuất file `.zip` (`Valheim_Client_Mods.zip`) chứa đầy đủ các plugin active và file config hiện có trên server, giúp bạn bè tải về giải nén đè vào thư mục Valheim là có thể vào chơi cùng ngay lập tức mà không sợ lệch phiên bản hay thiếu mod.

---

## 2. Danh sách file thay đổi (Files Changed)

### Backend (.NET C#)
| File | Thay đổi |
|:---|:---|
| [`ValheimModService.cs`](file:///d:/Workspace/Antigravity/Game_Server/backend/Infrastructure/GameServers/ValheimModService.cs) *(NEW)* | Service quản lý file system BepInEx: quét danh sách, bật/tắt, xóa, đọc/ghi file `.cfg`, upload file, tải & giải nén ZIP từ Thunderstore, và nén file export modpack. Kiểm tra an toàn chống Zip Slip, path traversal, và khóa thao tác khi server đang chạy. |
| [`ThunderstoreService.cs`](file:///d:/Workspace/Antigravity/Game_Server/backend/Infrastructure/GameServers/ThunderstoreService.cs) *(NEW)* | Service giao tiếp với Thunderstore API, cache 30 phút qua `IMemoryCache`, hỗ trợ tìm kiếm theo từ khóa và phân trang theo lượt tải. |
| [`ValheimContracts.cs`](file:///d:/Workspace/Antigravity/Game_Server/backend/Api/CoreContracts/ValheimContracts.cs) | Khai báo các DTO/Record hợp đồng: `ValheimModToggleRequest`, `ValheimModDeleteRequest`, `ValheimModConfigSaveRequest`, `ValheimThunderstoreInstallRequest`. |
| [`Program.cs`](file:///d:/Workspace/Antigravity/Game_Server/backend/Api/Program.cs) | Đăng ký DI (`ValheimModService`, `ThunderstoreService`, `MemoryCache`, `HttpClient`), mở 8 REST endpoints cho mod. |
| [`ValheimModServiceTests.cs`](file:///d:/Workspace/Antigravity/Game_Server/backend/Test/ValheimModServiceTests.cs) *(NEW)* | Bộ unit test kiểm thử: liệt kê mod, toggle, xóa, sửa config, chặn path traversal, upload `.dll`/`.zip`, chặn host không an toàn và xuất file modpack zip. |

### Frontend (React + Vite + TypeScript)
| File | Thay đổi |
|:---|:---|
| [`auth.ts`](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/auth.ts) | Bổ sung types (`ValheimMod`, `ThunderstorePackage`, `ThunderstoreSearchResult`), hỗ trợ `FormData` cho multipart request và các API client methods. |
| [`ValheimMods.tsx`](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/components/servers/ValheimMods.tsx) *(NEW)* | Component UI: Nút xuất Modpack, 2 sub-tabs (Mod đã cài & Chợ Thunderstore), bộ lọc tìm kiếm, bảng mod với badge trạng thái, toggle, modal Config Editor `.cfg`, và thẻ mod Thunderstore với nút 1-Click Install. |
| [`ServerDetailView.tsx`](file:///d:/Workspace/Antigravity/Game_Server/frontend/game-panel-web/src/components/servers/ServerDetailView.tsx) | Bổ sung tab **Mods** vào thanh điều hướng của Valheim. |

---

## 3. Chi tiết API Endpoints

| Method | Endpoint | Quyền (Auth) | Mô tả |
|:---|:---|:---|:---|
| `GET` | `/api/valheim/mods` | `authenticated` | Lấy danh sách mod đã cài đặt (`.dll`, trạng thái, dung lượng, file config) |
| `POST` | `/api/valheim/mods/toggle` | `admin` | Bật / Tắt mod (`.dll` ↔ `.dll.disabled`) |
| `DELETE` | `/api/valheim/mods` | `admin` | Xóa file mod |
| `GET` | `/api/valheim/mods/config` | `authenticated` | Đọc nội dung file `.cfg` |
| `PUT` | `/api/valheim/mods/config` | `admin` | Lưu thay đổi nội dung file `.cfg` |
| `POST` | `/api/valheim/mods/upload` | `admin` | Upload file mod `.dll` hoặc file `.zip` cục bộ |
| `GET` | `/api/valheim/mods/thunderstore/search` | `authenticated` | Tìm kiếm mod từ Thunderstore API (tham số `q`, `page`, `pageSize`) |
| `POST` | `/api/valheim/mods/thunderstore/install` | `admin` | 1-Click tải và cài đặt mod từ Thunderstore |
| `GET` | `/api/valheim/mods/export-modpack` | `authenticated` | Xuất và tải về file `Valheim_Client_Mods.zip` |

---

## 4. Cơ chế bảo vệ & An toàn (Security & Reliability)

1. **State Isolation**: Mọi thao tác thay đổi mod (toggle, delete, upload, install từ Thunderstore, sửa config) đều yêu cầu Valheim Server ở trạng thái **Stopped**. Nếu server đang chạy, backend trả về `409 Conflict`, frontend tự động vô hiệu hóa các nút hành động kèm banner cảnh báo màu đỏ.
2. **Zip Slip Prevention**: Quá trình giải nén file `.zip` (cả upload và từ Thunderstore) kiểm tra chuẩn hóa đường dẫn (`Path.GetFullPath`) đảm bảo không có file nào thoát ra ngoài thư mục đích `plugins/` hoặc `config/`.
3. **SSRF & Host Whitelist**: Cài đặt mod từ Thunderstore chỉ chấp nhận URL thuộc tên miền `thunderstore.io`.
4. **Path Traversal Protection**: Kiểm tra chặn các ký tự `..`, `/`, `\` trong tên mod và file config.
5. **Giới hạn kích thước**: Giới hạn upload tối đa 50MB và tải Thunderstore tối đa 100MB.

---

## 5. Kết quả kiểm tra (Verification)

- **Frontend Linting (`oxlint`)**: Passed (0 errors).
- **Frontend Unit Tests (`vitest run`)**: Passed 9/9 tests (bao gồm `ServerDetailView.test.tsx`).
- **Frontend Production Build (`npm run build`)**: Passed (tsc + Vite bundle hoàn tất).
- **Backend Unit Tests**: Đã viết đầy đủ trong `ValheimModServiceTests.cs`.
