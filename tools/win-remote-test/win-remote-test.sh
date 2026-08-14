#!/usr/bin/env bash
# Runs the window automation suite on a remote multi-monitor Windows machine. Publishes self-contained
# here, ships the payload over SSH, and hands the run to the broker in that machine's console session:
# SSH itself lands in the sshd service session, where the physical monitors are not visible.
#
# The suite derives its cases from whatever displays the machine has (see MonitorMatrix), so the point
# of a remote box is the scales it owns, not the machine itself.
#
# Pass an ssh-config alias rather than user@host: scp spells the port -P and ssh -p, so a target
# carrying a non-default port cannot satisfy both, and the machine is reached over a non-default port
# from outside its LAN.
#
# Usage: ./win-remote-test.sh <ssh-target> [-- <extra runner args>]
#   ./win-remote-test.sh mewui-testbox
#   ./win-remote-test.sh mewui-testbox -- --filter-class MenuCaptionTrimTests
set -euo pipefail

SSH_TARGET="${1:?usage: win-remote-test.sh <ssh-target> [-- <extra runner args>]}"
shift
if [[ "${1:-}" == "--" ]]; then shift; fi
RUNNER_ARGS="$*"

HERE="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$HERE/../../tests/MewUI.WindowAutomationTest/MewUI.WindowAutomationTest.csproj"
PUBLISH_DIR="$HERE/publish"
REMOTE_ROOT="${MEWUI_REMOTE_ROOT:-C:\\Workspace\\Dev}"
JOB_ID="run-$(date +%Y%m%d-%H%M%S)"
TIMEOUT_SECONDS=900

# UseVSTest=false selects the MSTest runner, which produces a plain executable: the remote machine then
# needs no SDK, and self-contained means it needs no .NET at all.
echo "== publishing the suite (self-contained)"
dotnet publish "$PROJECT" -c Debug -f net8.0 -r win-x64 --self-contained \
  -p:UseVSTest=false -o "$PUBLISH_DIR" -v:q --nologo

echo "== checking the broker"
ssh "$SSH_TARGET" "powershell -NoProfile -Command \"if (Test-Path '$REMOTE_ROOT\\broker.alive') { Get-Content '$REMOTE_ROOT\\broker.alive' } else { 'NO_BROKER'; exit 1 }\""

echo "== shipping the payload"
tar -C "$HERE" -czf "$HERE/payload.tgz" publish
scp -q "$HERE/payload.tgz" "$SSH_TARGET:$REMOTE_ROOT\\payload.tgz"
rm "$HERE/payload.tgz"
ssh "$SSH_TARGET" "powershell -NoProfile -Command \"Remove-Item -Recurse -Force '$REMOTE_ROOT\\publish' -ErrorAction SilentlyContinue; tar -C '$REMOTE_ROOT' -xzf '$REMOTE_ROOT\\payload.tgz'; Remove-Item '$REMOTE_ROOT\\payload.tgz'\""

# The job is written to a staging name and renamed, so the broker never picks up a half-uploaded file.
echo "== queueing $JOB_ID"
cat > "$HERE/$JOB_ID.cmd" <<EOF
@echo off
"$REMOTE_ROOT\\publish\\Aprillz.MewUI.WindowAutomationTest.exe" $RUNNER_ARGS
EOF
scp -q "$HERE/$JOB_ID.cmd" "$SSH_TARGET:$REMOTE_ROOT\\jobs\\$JOB_ID.staging"
rm "$HERE/$JOB_ID.cmd"
ssh "$SSH_TARGET" "powershell -NoProfile -Command \"Move-Item '$REMOTE_ROOT\\jobs\\$JOB_ID.staging' '$REMOTE_ROOT\\jobs\\$JOB_ID.cmd' -Force\""

echo "== waiting for the console session to finish"
# Double quotes, not single: powershell -File takes a single-quoted path literally, quotes included.
ssh "$SSH_TARGET" "powershell -NoProfile -ExecutionPolicy Bypass -File \"$REMOTE_ROOT\\wait-job.ps1\" -Id \"$JOB_ID\" -Root \"$REMOTE_ROOT\" -TimeoutSeconds $TIMEOUT_SECONDS"
