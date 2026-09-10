#!/usr/bin/env bash
cd /home/nh4n/projects/game-server-panel/backend/Api
export PATH="/home/nh4n/.dotnet:$PATH"
export DOTNET_ROOT="/home/nh4n/.dotnet"
exec /home/nh4n/.dotnet/dotnet run --urls=http://100.82.102.38:5000