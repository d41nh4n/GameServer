Valheim Existing Runtime Handoff for GamePanel Adoption
1. Mục tiêu

Tài liệu này là context để agent Hermes nối Valheim Dedicated Server hiện có vào GamePanel theo chế độ AdoptExisting.

Không cài lại Valheim, không di chuyển file, không tạo world mới, không thay đổi password, không chạy executable trực tiếp và không tạo process Valheim thứ hai. systemd phải tiếp tục là chủ sở hữu duy nhất của runtime.

Trao đổi với người dùng bằng tiếng Việt. Code, identifier và comment kỹ thuật có thể dùng tiếng Anh.

2. Host đã xác nhận
Hostname:       myserver
OS:             Ubuntu 24.04.4 LTS
Architecture:   x86-64
Kernel:         6.8.0-139-generic
Filesystem:     ext4 trên LVM
Free space:     khoảng 193 GB tại thời điểm cài
Private access: Tailscale
Tailscale IP:   100.82.102.38

Project Zomboid đang tồn tại trên host và không được thay đổi:

pzmanager.service
pzserver-game.service
3. Identity của Valheim instance
InstanceKey:       valheim-main
DisplayName:       Valheim Main
GameType:          valheim
ProvisioningMode:  AdoptExisting
RuntimeType:       systemd
RuntimeId:         valheim-main.service
Linux user/group:  valheim:valheim
Steam App ID:      896660
Game SteamAppId:   892970
4. Files và folders hiện tại
/srv/gamepanel/instances/valheim-main/
├── server/       # Valheim Dedicated Server files, SteamCMD App 896660
├── data/         # persistent world và permission lists
├── backups/      # backup destination, không phải live data
├── logs/         # chỉ dùng nếu cần game-generated file log
└── runtime/      # HOME của SteamCMD/Valheim và runtime metadata
Server installation
/srv/gamepanel/instances/valheim-main/server/
├── valheim_server.x86_64
├── valheim_server_Data/
├── linux64/
├── steamapps/
├── steamclient.so
├── UnityPlayer.so
├── start_server.sh
└── steam_appid.txt

Installation size lúc cài: khoảng 2.0 GB.

Persistent data
/srv/gamepanel/instances/valheim-main/data/
├── adminlist.txt
├── bannedlist.txt
├── permittedlist.txt
├── cache/
└── worlds_local/
    └── valheim_main/
        ├── _main.1.db2
        ├── _main.1.fwl2
        ├── _main.1.chunks
        ├── _main.1.ok
        └── *.chunk

World format hiện tại là định dạng mới có .db2, .fwl2, .chunks và chunk files; không được giả định chỉ có .db/.fwl kiểu cũ.

Runtime metadata
/srv/gamepanel/instances/valheim-main/runtime/.local/share/Steam/
/srv/gamepanel/instances/valheim-main/runtime/.steam/
Host-only configuration và launcher
/etc/gamepanel/valheim-main.env
/usr/local/libexec/valheim-main-start
/etc/systemd/system/valheim-main.service

File secret:

/etc/gamepanel/valheim-main.env
owner: root:valheim
mode:  0640

File này chứa VALHEIM_SERVER_NAME, VALHEIM_WORLD_NAME, VALHEIM_PASSWORD, VALHEIM_PORT, VALHEIM_PUBLIC. Không in nội dung, không đưa vào Git, database, API response, log hoặc prompt. Chỉ xác nhận keys/permissions mà không đọc giá trị.

Launcher:

/usr/local/libexec/valheim-main-start
owner: root:root
mode:  0755

Launcher đọc environment do systemd cung cấp, đặt HOME, SteamAppId, LD_LIBRARY_PATH, rồi exec executable hiện có với -savedir trỏ đến data/.

5. Runtime đã xác nhận

Unit:

valheim-main.service

Đặc tính đã kiểm tra:

ActiveState:    active
SubState:       running
User/Group:     valheim:valheim
Restart:        on-failure
Enabled at boot: disabled
Log source:     journald
Graceful stop:  SIGINT
Stop timeout:   120 seconds

Readiness marker đã xác nhận:

Game server connected

Phiên bản tại thời điểm handoff:

Valheim version: l-1.0.7
Network version: 39

Cổng thực tế đã bind:

UDP 2456
UDP 2457

UDP 2458 không được sử dụng trong lần kiểm tra. Không tự mở thêm cổng nếu chưa có bằng chứng runtime.

Client connection đã xác nhận ngày 2026-09-09:

Access path:  Tailscale/private
Endpoint:     100.82.102.38:2456
Result:       Kết nối thành công
Authentication: Password được chấp nhận
World load:   Thành công

Không ghi password thật vào tài liệu này.

6. Network và security boundary

Thiết kế yêu cầu Tailscale-only. Rule dự kiến là:

ufw allow in on tailscale0 to any port 2456:2457 proto udp comment 'Valheim Tailscale'

Agent phải kiểm tra trạng thái UFW thật trước khi kết luận rule đã tồn tại. Không tạo WAN rule, router forwarding, DDNS hoặc public management port.

Server được cấu hình dự kiến với VALHEIM_PUBLIC=0; xác minh key tồn tại nhưng không dump toàn bộ secret file.

Password của Valheim cuối cùng xuất hiện trong process arguments do giới hạn startup interface của game. Không gọi ps -ef, pgrep -a, systemctl status -l hoặc API nào trả về command line đầy đủ. Panel tuyệt đối không expose process arguments.

7. SteamCMD update command

SteamCMD executable:

/usr/games/steamcmd

Update phải thực hiện khi service đã dừng:

sudo -u valheim env \
  HOME=/srv/gamepanel/instances/valheim-main/runtime \
  /usr/games/steamcmd \
  +force_install_dir /srv/gamepanel/instances/valheim-main/server \
  +login anonymous \
  +app_update 896660 validate \
  +quit

Không update khi valheim-main.service đang chạy. Không chạm vào data/ trong quá trình SteamCMD update.

8. GamePanel adoption record đề xuất
Name:              Valheim Main
GameType:          valheim
ProvisioningMode:  AdoptExisting
RuntimeType:       systemd
RuntimeId:         valheim-main.service
InstallationPath:  /srv/gamepanel/instances/valheim-main/server
DataPath:          /srv/gamepanel/instances/valheim-main/data
BackupPath:        /srv/gamepanel/instances/valheim-main/backups
SteamAppId:        896660
BasePort:          2456
Protocol:          UDP
ReadinessMarker:   Game server connected

GamePanel repo đã từng được quan sát tại:

/home/nh4n/projects/game-server-panel

Agent phải xác minh repo/worktree/branch thật trước khi sửa code.

9. Kiến trúc tích hợp mong muốn
ValheimProvider
    -> ValheimRuntimeStrategy
    -> SystemdRuntimeDriver
    -> valheim-main.service

Shared systemd driver quản lý:

start, stop, restart, status, MainPID, logs, resource metrics

Valheim-specific provider cung cấp:

Steam App ID, ports, paths, update procedure, configuration schema,
world-data rules và readiness marker

Panel không được gọi trực tiếp valheim_server.x86_64.

10. Quyền điều khiển cho panel

Không cấp NOPASSWD: ALL cho runtime user của GamePanel. Dùng wrapper root-owned hoặc cơ chế privilege boundary hiện có, chỉ whitelist:

systemctl start valheim-main.service
systemctl stop valheim-main.service
systemctl restart valheim-main.service
systemctl show valheim-main.service với tập property cố định
journalctl read-only cho đúng valheim-main.service

Không nhận arbitrary unit name hoặc arbitrary command/path từ HTTP request.

11. Health check đúng

Không coi systemctl active một mình là đủ. Trạng thái Running yêu cầu:

ActiveState=active.
SubState=running.
MainPID > 0.
PID thuộc valheim-main.service.
UDP 2456–2457 được bind bởi service.
Lần khởi động hiện tại có Game server connected.

Nên lấy InvocationID từ systemd và chỉ tìm readiness marker trong journal của invocation hiện tại, tránh đọc nhầm log cũ.

12. Quy trình adopt bắt buộc ban đầu

Lần tích hợp đầu tiên phải read-only:

Xác minh repo và đọc tài liệu kiến trúc hiện tại.
Xác minh unit, launcher và paths tồn tại.
Xác minh user/group và permissions mà không dump secret.
Xác minh service state, MainPID và journald.
Xác minh port allocation và UFW.
Xác minh installation/data path không overlap.
Đề xuất DB/domain/API changes cho AdoptExisting.
Báo cáo kế hoạch, tests và rollback.
Dừng để xin duyệt trước khi sửa code hoặc runtime.

Không reinstall, relocate, update, rewrite configuration, start/stop/restart hoặc enable service trong discovery/adoption registration đầu tiên.

13. Update và backup workflow tương lai
Stop graceful
-> xác nhận process và ports đã dừng
-> backup toàn bộ persistent data
-> ghi timestamp/source/destination/checksum
-> SteamCMD app_update 896660 validate
-> start systemd
-> chờ readiness của invocation mới
-> kiểm tra client/network/world

Không restore lên live server. Không tự động retention/delete trước khi restore test bằng disposable copy thành công.

14. Việc còn cần xác minh
Rule UFW Tailscale-only đã được thêm hay chưa.
systemctl stop đã được kiểm tra đầy đủ dưới systemd và xác nhận world save hay chưa.
Start lần hai có giữ nguyên world và chỉ tạo một process hay chưa.
Baseline backup/restore test chưa hoàn tất.
Exact GamePanel runtime user, privilege wrapper và domain schema hiện tại.
15. Yêu cầu báo cáo của Hermes

Sau read-only discovery, báo cáo bằng tiếng Việt:

Files/repo đã đọc.
Runtime facts đã xác minh.
Khoảng trống giữa code hiện tại và adoption contract.
Thiết kế đề xuất theo từng layer.
Danh sách file code dự kiến thay đổi.
Tests dự kiến thêm.
Security/rollback plan.
Những điểm cần người dùng quyết định.

Không commit nếu người dùng chưa yêu cầu. Không đưa secret, Tailscale IP hoặc machine-specific credentials vào Git.
