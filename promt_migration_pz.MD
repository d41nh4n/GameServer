You are working on my GameServer project and need to prepare and implement a Change Request for integrating the existing Project Zomboid dedicated server into the generic Game Server Control Panel.

Repository:
https://github.com/d41nh4n/GameServer

IMPORTANT:
Do not assume the recap below is fully current.
First inspect the actual repository and actual Ubuntu runtime.
The existing running Project Zomboid server is the source of truth.

Do NOT modify, delete, reset, regenerate, or migrate any Project Zomboid save/world during discovery.
Do NOT restart or stop the Project Zomboid game server unless the implementation/test explicitly requires it and you explain why first.
Do NOT print passwords, JWT secrets, RCON passwords, or other secrets.
Do NOT push or merge to main.
Work locally and produce a CR/PR-ready implementation and description.

Respond/explain findings in Vietnamese, but keep code, class names, comments, commits and technical identifiers in English.


============================================================
1. PROJECT GOAL
============================================================

GameServer is intended to be a generic web control panel capable of managing multiple dedicated games through adapter architecture.

Current supported/implemented runtime:
- Valheim

Target of this CR:
- Project Zomboid

The architecture must remain generic:

API / GameServerManager
        |
        v
IGameServerAdapter
        |
        +---- ValheimAdapter
        |
        +---- ProjectZomboidAdapter
        |
        +---- future MinecraftAdapter
        +---- future adapters

Project Zomboid-specific logic MUST NOT leak into:
- API endpoints
- GameServerManager
- generic frontend lifecycle logic

The API/UI should continue calling the same generic Start / Stop / Status flow.


============================================================
2. CURRENT GAMESERVER PROJECT BASELINE
============================================================

Known state from the previous milestone:

Solution:
GamePanel.slnx

Projects:
- Domain
- Application
- Infrastructure
- Api

Backend:
- ASP.NET Core 10
- EF Core
- SQLite gamepanel.db
- EF Core Migrations
- SignalR /hubs/server
- JWT Bearer authentication
- Tailscale optional whitelist middleware

Frontend:
frontend/game-panel-web/
- React
- TypeScript
- Vite
- @microsoft/signalr

Ports:
Backend:
- localhost:5000
- Tailscale:100.82.102.38:5000

Frontend:
- localhost:5173
- Tailscale:100.82.102.38:5173

Current model contains approximately:
ServerInstance:
- Id
- Type
- ProcessId
- Port
- WorldName
- Password
- other existing fields

Game type enum already has or was planned to have:
- Valheim
- Minecraft
- ProjectZomboid

Current adapter concept approximately:

IGameServerAdapter

StartAsync(ServerInstance)
StopAsync(...)
GetStatusAsync(...)

IMPORTANT:
Inspect the ACTUAL interface before changing anything.
Do not trust this summary over repository code.

Existing components to inspect:

backend/Infrastructure/GameServers/
backend/Infrastructure/Services/GameServerManager.cs
backend/Infrastructure/Hubs/ServerHub.cs
backend/Infrastructure/Data/AppDbContext.cs
backend/Api/Program.cs
backend/Api/AppSettings.cs

Existing Valheim implementation:
ValheimAdapter

Existing factory:
GameServerAdapterFactory

Current Valheim runtime behavior:
- Process.Start
- ProcessStartInfo.ArgumentList
- PID stored in SQLite
- /proc/{pid} used for status
- SIGTERM / kill -15 for stop
- SignalR ServerStateChanged
- backend restart persistence

Do NOT break the existing verified Valheim behavior.


============================================================
3. FIRST TASK: REPOSITORY DISCOVERY
============================================================

Before writing code, inspect the current branch.

Run/read at minimum:

git status
git branch --show-current
git log --oneline -10

Inspect repository structure.

Search for:

IGameServerAdapter
GameServerAdapterFactory
GameServerManager
ValheimAdapter
ProjectZomboid
Minecraft
ServerInstance
GameServerStatus
ProcessId
ServerStateChanged
GameServers
ExecutablePath

Use rg/find as appropriate.

Read the actual implementations.

Then produce a short:

REPOSITORY DISCOVERY REPORT

including:

1. Actual IGameServerAdapter signature
2. Actual ValheimAdapter lifecycle
3. Actual factory behavior
4. GameServerManager responsibilities
5. DB fields involved in runtime
6. SignalR lifecycle
7. Configuration/options structure
8. Current ProjectZomboid placeholders, if any
9. Tests currently available
10. Any mismatch between this prompt recap and actual code

DO NOT start implementation until this discovery is complete.


============================================================
4. EXISTING PROJECT ZOMBOID SERVER
============================================================

There is already a real working Project Zomboid B42 dedicated server on this Ubuntu machine.

Known baseline:

Linux game user:
pzserver

Install root:
 /home/pzserver/steamcmd/zomboid

Main executable:
 /home/pzserver/steamcmd/zomboid/ProjectZomboid64

Current systemd service:
 pzserver-game.service

Known service invocation currently resembles:

/home/pzserver/steamcmd/zomboid/ProjectZomboid64 \
    -servername servertest_new

The active server profile was last known as:

servertest_new

BUT VERIFY THIS FROM systemd.
Do not assume it.

Project Zomboid server configs are under:

/home/pzserver/Zomboid/Server/

Known files:

servertest_new.ini
servertest_new_SandboxVars.lua
servertest_new_spawnpoints.lua
servertest_new_spawnregions.lua

World/save:

/home/pzserver/Zomboid/Saves/Multiplayer/servertest_new/

Old world/profile may also exist:

servertest

DO NOT DELETE OR MODIFY THE OLD WORLD.


============================================================
5. CURRENT PZ LIFECYCLE
============================================================

Project Zomboid currently does NOT rely on the ASP.NET app for lifecycle.

It is managed by systemd.

Inspect:

systemctl cat pzserver-game.service

and:

systemctl show pzserver-game.service \
  --property=ActiveState \
  --property=SubState \
  --property=MainPID \
  --property=MemoryCurrent \
  --property=ActiveEnterTimestamp

Known service characteristics:

- runs as user pzserver
- WorkingDirectory points to PZ install
- direct ProjectZomboid64 execution
- KillSignal=SIGINT
- TimeoutStopSec around 90
- Restart=on-failure
- SuccessExitStatus may include 130
- server is intentionally NOT enabled for automatic boot
- control panel is responsible for manually starting it

Verify all of this from the actual service.


============================================================
6. PRIVILEGED PZ CONTROL WRAPPER
============================================================

A root-owned wrapper already exists:

/usr/local/sbin/pz-gamectl

Known supported commands approximately:

start
stop
restart
is-active
status
show
logs [N]
stream

Inspect the ACTUAL wrapper.

Do not replace it blindly.

The previous PZ control panel used this wrapper instead of allowing arbitrary sudo/systemctl commands.

Security requirement:

The ASP.NET backend must NOT accept arbitrary shell commands.

If the ASP.NET process requires privilege escalation:
- determine the Unix user running GamePanel
- inspect existing sudoers rules
- propose the minimum safe sudo rule
- use only the allowlisted wrapper
- do not grant unrestricted systemctl or sudo shell access

Do NOT change sudoers during discovery.

If deployment requires a sudoers change, document it explicitly as a deployment step.


============================================================
7. WORKSHOP PREFLIGHT
============================================================

A Project Zomboid Workshop/Mod validator already exists:

/usr/local/sbin/pz-workshop-preflight

The systemd service may run it via ExecStartPre.

Inspect:

systemctl cat pzserver-game.service

and:

sudo sed -n '1,240p' /usr/local/sbin/pz-workshop-preflight

Its purpose is approximately:

servertest_new.ini
       |
       +-- WorkshopItems=
       +-- Mods=
       |
       v
validate installed Workshop items / mod.info
       |
       v
allow server startup only when validation passes

Do not recreate this validation inside C# in this CR unless there is a strong reason.

Prefer using the existing service/preflight boundary.

The adapter should trigger the normal service startup and allow systemd ExecStartPre to remain the source of truth.


============================================================
8. RCON
============================================================

The PZ server already has RCON.

Known baseline:

host:
127.0.0.1

port:
27015

The password is secret.

DO NOT:
- print it
- commit it
- return it from an endpoint
- place it in frontend config
- hardcode it in source code

Inspect only non-secret configuration such as:

grep '^RCONPort=' \
  /home/pzserver/Zomboid/Server/servertest_new.ini

If an old manager .env exists, do NOT cat the file.

Allowed inspection:

grep '^RCON_HOST=' ...
grep '^RCON_PORT=' ...

Do not print RCON_PASSWORD.

Desired graceful stop behavior eventually:

GamePanel Stop
      |
      +--> optional broadcast warning through RCON
      |
      +--> RCON save
      |
      +--> systemd stop / SIGINT
      |
      +--> wait for inactive
      |
      +--> clear ProcessId
      |
      +--> SignalR state update

If implementing RCON in this CR would make scope too large,
separate it clearly into:
- required lifecycle CR
- follow-up graceful RCON enhancement CR

However, do NOT implement an unsafe hard kill as the normal PZ stop path.

The current systemd SIGINT behavior should be preserved.


============================================================
9. OLD PZ WEB MANAGER AS REFERENCE ONLY
============================================================

There is an older Flask PZ manager:

/home/pzserver/pzservermanager

It may contain useful working implementations for:

- RCON
- start/stop/restart
- server health
- mods
- logs
- sandbox configuration
- Workshop preflight integration
- status
- systemd control

Important files may include:

app.py
ops_routes.py
sandbox_routes.py
sandbox_schema.py
templates/

Use this ONLY as behavioral/reference material.

DO NOT:
- embed Flask into ASP.NET
- make ASP.NET call the old Flask HTTP API as the permanent architecture
- copy unsafe raw shell execution
- make the new GameServer architecture dependent on the old manager

The .NET app should eventually replace it.


============================================================
10. IMPORTANT ARCHITECTURE QUESTION
============================================================

The current adapter abstraction may be too PID-centric.

Valheim is managed directly as a process:

GamePanel
  -> Process.Start
  -> PID

Project Zomboid is currently:

GamePanel
  -> systemd service
  -> ProjectZomboid64
  -> MainPID

Future game adapters could also be:
- Docker
- systemd
- external service
- direct process

Therefore inspect whether the current interface such as:

StopAsync(int pid)
GetStatusAsync(int pid)

is sufficiently generic.

Do NOT automatically redesign everything.

First answer:

Can ProjectZomboidAdapter be implemented correctly with the existing
IGameServerAdapter without violating the abstraction?

If YES:
keep the interface and make the smallest change.

If NO:
propose the smallest generic evolution.

A possible direction, ONLY if justified by actual code, is instance-centric runtime methods such as:

StartAsync(ServerInstance instance, CancellationToken ct)

StopAsync(ServerInstance instance, CancellationToken ct)

GetRuntimeStateAsync(
    ServerInstance instance,
    CancellationToken ct
)

where runtime state could contain:

Status
ProcessId
IsReady
Message

Do not use these signatures blindly.
They are design guidance, not requirements.

Any generic interface change MUST keep Valheim working.


============================================================
11. PREFERRED PZ ADAPTER BEHAVIOR
============================================================

Target class:

ProjectZomboidAdapter

Infrastructure/GameServers/

Its responsibilities should stay limited to PZ runtime specifics.

Expected Start flow:

ServerInstance
     |
     v
ProjectZomboidAdapter
     |
     v
approved service-control wrapper
     |
     v
systemd starts pzserver-game.service
     |
     v
ExecStartPre runs Workshop preflight
     |
     v
ProjectZomboid64 starts
     |
     v
read actual systemd MainPID
     |
     v
return success + ProcessId

Do not create duplicate ProjectZomboid processes with Process.Start
if systemd is already the lifecycle owner.

There must only be ONE lifecycle owner.

Expected Status flow should use real runtime state.

Preferred source:

systemd ActiveState/SubState/MainPID

Then optionally verify:

/proc/{MainPID}

Do not report Running solely because SQLite still contains an old PID.

This should also help address the existing TODO:

"backend restart / stale ProcessId reconciliation"


============================================================
12. START READINESS
============================================================

Distinguish these concepts:

PROCESS RUNNING
versus
SERVER READY

systemd Type=simple may report active before PZ finishes loading.

Known PZ readiness indicator includes RCON becoming available.

Do not block the generic Start endpoint for an unbounded amount of time.

For this CR decide explicitly whether:

A.
Running means process/service active only

or

B.
Start waits for bounded RCON readiness

If B:
use a bounded timeout and cancellation token.

Never infinite wait.

Explain the decision in the CR.


============================================================
13. CONFIGURATION
============================================================

No hardcoded:

/home/nh4n
/home/pzserver

inside adapter implementation if avoidable.

Add strongly typed Project Zomboid settings consistent with existing
AppSettings/options conventions.

Possible configuration shape:

GameServers:
  ProjectZomboid:
    ServiceName: "pzserver-game.service"
    ControlExecutable: "/usr/local/sbin/pz-gamectl"
    InstallPath: "{PZ_HOME}/steamcmd/zomboid"
    ConfigDirectory: "{PZ_HOME}/Zomboid/Server"
    DefaultServerName: "servertest_new"
    RconHost: "127.0.0.1"
    RconPort: 27015

Secrets such as RconPassword:
- environment variable
- user secrets
- machine-specific ignored appsettings
depending on current project conventions

Do not commit the real secret.

Reuse the existing ExpandHome/path strategy if appropriate.

Do not introduce a second unrelated configuration system.


============================================================
14. COMMAND EXECUTION SECURITY
============================================================

All external processes must use:

ProcessStartInfo
ArgumentList

Do NOT build shell command strings from ServerInstance values.

No:

bash -c "<user input>"
sh -c
sudo bash
eval
arbitrary command endpoint

The adapter may only invoke known executable + allowlisted arguments.

Example conceptual boundary:

/usr/bin/sudo
/usr/local/sbin/pz-gamectl
start

but first inspect the actual deployment/user permissions.


============================================================
15. DATABASE
============================================================

Inspect whether a new migration is actually needed.

Prefer NOT to add Project-Zomboid-specific columns to ServerInstance.

Avoid fields such as:

PzRconPassword
PzSandboxPath
PzServiceName

inside the generic entity unless there is a generic abstraction reason.

Game-specific infrastructure settings belong in options/config.

ServerInstance should remain game-neutral as much as possible.

If the enum already contains ProjectZomboid:
do not create a duplicate value.

If seed data is appropriate, add one PZ ServerInstance such as:

Name: Project Zomboid
Type: ProjectZomboid
WorldName: servertest_new

But inspect current seed strategy first.


============================================================
16. FRONTEND SCOPE
============================================================

For THIS CR keep frontend changes minimal.

The existing generic server card should be able to show:

Project Zomboid
STOPPED / RUNNING
Start
Stop

using the same endpoints and SignalR event used by Valheim.

Do NOT build these yet unless required:

- raw servertest.ini editor
- SandboxVars editor
- mod manager
- map manager
- RCON console
- live journal viewer
- player admin tools

Those should become later PZ feature CRs.

This CR should prove:

"Project Zomboid is a first-class adapter in the generic lifecycle architecture."


============================================================
17. TESTABILITY
============================================================

Do not write tests that actually start/stop the real PZ server.

If ProjectZomboidAdapter requires system commands,
introduce/reuse a small injectable process/command runner if needed.

Tests should be able to simulate:

- service starts successfully
- service start fails
- Workshop preflight/systemctl failure
- MainPID returned correctly
- service inactive
- stale DB PID
- stop succeeds
- stop fails
- invalid/missing configuration
- command arguments cannot be injected

Do not over-engineer a framework just for tests.


============================================================
18. REQUIRED DISCOVERY COMMANDS ON UBUNTU
============================================================

During discovery, inspect safely.

Useful commands:

whoami

ps -ef | grep '[d]otnet'

systemctl is-active pzserver-game.service

systemctl cat pzserver-game.service

systemctl show pzserver-game.service \
  --property=ActiveState \
  --property=SubState \
  --property=MainPID \
  --property=MemoryCurrent \
  --property=ActiveEnterTimestamp

ls -lah /home/pzserver/Zomboid/Server/

ls -ld /home/pzserver/Zomboid/Saves/Multiplayer/*

sudo sed -n '1,220p' /usr/local/sbin/pz-gamectl

sudo sed -n '1,240p' /usr/local/sbin/pz-workshop-preflight

grep '^RCONPort=' \
  /home/pzserver/Zomboid/Server/servertest_new.ini

grep -E '^(Mods|WorkshopItems)=' \
  /home/pzserver/Zomboid/Server/servertest_new.ini

Do NOT cat secret env files.

Do NOT edit any PZ files during discovery.


============================================================
19. REQUIRED OUTPUT BEFORE IMPLEMENTATION
============================================================

Before coding give me:

A. CURRENT REPOSITORY STATE

B. CURRENT PROJECT ZOMBOID RUNTIME STATE

C. ARCHITECTURE GAP ANALYSIS

D. PROPOSED CR SCOPE

E. FILES TO ADD

F. FILES TO MODIFY

G. DATABASE MIGRATION REQUIRED?
YES/NO + reason

H. SECURITY / SUDO IMPACT

I. RISK LIST

J. ACCEPTANCE CRITERIA

Then implement only after those are clear.


============================================================
20. IMPLEMENTATION ACCEPTANCE CRITERIA
============================================================

At minimum the completed CR must demonstrate:

1.
GameServerAdapterFactory resolves:
ProjectZomboid -> ProjectZomboidAdapter

2.
Generic API Start works for a ProjectZomboid ServerInstance.

3.
PZ is started through its existing controlled systemd lifecycle,
not by creating a second unmanaged process.

4.
The actual PZ MainPID is stored/reconciled.

5.
Generic API Stop shuts PZ down gracefully.

6.
No arbitrary shell command construction.

7.
No secret committed.

8.
Valheim lifecycle still passes existing tests/manual verification.

9.
SignalR emits ServerStateChanged for PZ using the same generic mechanism.

10.
Backend restart does not incorrectly report PZ Running solely due to a stale DB PID.

11.
dotnet build succeeds with:
0 errors

12.
Existing frontend can manage the PZ server through the generic server card.

13.
No world/save/config deletion.

14.
No old Flask dependency introduced.


============================================================
21. EXPECTED FOLLOW-UP CRS — DO NOT MIX INTO THIS CR
============================================================

After lifecycle integration succeeds, propose separate CRs such as:

CR-PZ-02:
RCON graceful save / broadcast / player commands

CR-PZ-03:
PZ logs + journal streaming to SignalR

CR-PZ-04:
servertest.ini editor

CR-PZ-05:
SandboxVars.lua safe schema/detected-scalar editor

CR-PZ-06:
Workshop / Mods manager + preflight UI

CR-PZ-07:
World/Profile manager
- create world
- switch world
- backup
- active ServerName

CR-PZ-08:
PZ health/readiness metrics
- systemd active
- process
- RCON ready
- players
- memory
- uptime


============================================================
22. FINAL DELIVERABLE
============================================================

After implementation provide:

1. Summary of actual architecture discovered
2. Final architecture diagram
3. Changed file tree
4. Important code decisions
5. Exact build/test commands run
6. Test results
7. Manual verification commands
8. Deployment changes required on Ubuntu
9. Rollback procedure
10. CR/PR title
11. Full CR/PR description
12. Follow-up TODOs

Suggested CR title:

feat(project-zomboid): add systemd-backed Project Zomboid server adapter

The final solution should strengthen the multi-game adapter architecture,
not create a one-off Project Zomboid hack.
