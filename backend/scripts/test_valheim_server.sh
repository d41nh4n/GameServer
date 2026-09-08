#!/usr/bin/env bash
# Test wrapper cho ValheimAdapter — mô phỏng một dedicated server chạy lâu
# (cùng code path StartAsync/StopAsync/GetStatusAsync; để test pipeline PID + /proc)
set -m
while [ $# -gt 0 ]; do
  case "$1" in
    -name) shift; NAME="$1" ;;
    -port) shift; PORT="$1" ;;
    -world) shift; WORLD="$1" ;;
    -password) shift; PASS="$1" ;;
    -logFile) ;;
  esac
  shift
done

# Ghi log khi có thể
LOG="${HOME}/valheim-test-${NAME// /_}.log"
echo "Started fake valheim: name=${NAME} port=${PORT} world=${WORLD} pid=$$" >> "$LOG"

echo "FAKE-VALHEIM-READY name=${NAME} port=${PORT} world=${WORLD} pid=$$" >&1

# Duy trì process chạy
trap 'echo "Stopping fake valheim pid=$$" >> "$LOG"; exit 0' TERM INT
while true; do
  sleep 3600 &
  wait $!
done