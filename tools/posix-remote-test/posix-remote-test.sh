#!/usr/bin/env bash
# Runs the window automation suite on a remote macOS or Linux (X11) machine. SSH reaches the logged-in GUI
# session there directly, so unlike win-remote-test.sh no broker is involved: the suite is published here,
# shipped over SSH, and run in place against the session's display.
#
# The payload is framework-dependent and runs through `dotnet`, so the remote needs the .NET 10 runtime:
# an osx apphost published off a Mac is not signed.
#
# Pass an ssh-config alias rather than user@host when the machine uses a non-default port: scp spells the
# port -P and ssh -p, so a target carrying one cannot satisfy both.
#
# Usage: ./posix-remote-test.sh <ssh-target> [-- <extra runner args>]
#   ./posix-remote-test.sh my-mac
#   ./posix-remote-test.sh my-linux-box -- --filter "FullyQualifiedName~PlatformSessionTests"
#
# Environment:
#   MEWUI_REMOTE_ROOT     remote directory under the home directory (default: mewui-window-automation)
#   MEWUI_REMOTE_DISPLAY  X11 display for Linux targets (default: :0)
#   MEWUI_REMOTE_ENV      NAME=value pairs, semicolon-separated, exported for the run
set -euo pipefail

SSH_TARGET="${1:?usage: posix-remote-test.sh <ssh-target> [-- <extra runner args>]}"
shift
if [[ "${1:-}" == "--" ]]; then shift; fi
RUNNER_ARGS="$*"

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
PROJECT="$REPO/tests/MewUI.WindowAutomationTest/MewUI.WindowAutomationTest.csproj"
# Everything a run writes stays under the ignored .artifacts: this folder is synchronized to other machines.
OUT="$REPO/.artifacts/posix-remote-test"
mkdir -p "$OUT"
REMOTE_ROOT="${MEWUI_REMOTE_ROOT:-mewui-window-automation}"
FRAMEWORK=net10.0
# Non-interactive SSH shells on macOS do not read the profile that puts dotnet on PATH.
REMOTE_PATH='export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet:/opt/homebrew/bin"'

echo "== probing the remote"
REMOTE_UNAME="$(ssh "$SSH_TARGET" 'uname -sm')"
case "$REMOTE_UNAME" in
  "Darwin arm64") RID=osx-arm64 ;;
  "Darwin x86_64") RID=osx-x64 ;;
  "Linux x86_64") RID=linux-x64 ;;
  "Linux aarch64") RID=linux-arm64 ;;
  *) echo "unsupported remote: $REMOTE_UNAME" >&2; exit 1 ;;
esac
ssh "$SSH_TARGET" "$REMOTE_PATH; dotnet --list-runtimes | grep 'Microsoft.NETCore.App 10\.' || { echo NO_DOTNET_10; exit 1; }"

PUBLISH_NAME="publish-$RID"
# UseVSTest=false selects the MSTest runner, which produces a plain executable application.
echo "== publishing the suite ($RID, framework-dependent)"
rm -rf "$OUT/$PUBLISH_NAME"
dotnet publish "$PROJECT" -c Debug -f "$FRAMEWORK" -r "$RID" --self-contained false \
  -p:UseVSTest=false -o "$OUT/$PUBLISH_NAME" -v:q --nologo

echo "== shipping the payload"
tar -C "$OUT" -czf "$OUT/payload-$RID.tgz" "$PUBLISH_NAME"
scp -q "$OUT/payload-$RID.tgz" "$SSH_TARGET:payload-$RID.tgz"
rm "$OUT/payload-$RID.tgz"
ssh "$SSH_TARGET" "rm -rf ~/$REMOTE_ROOT && mkdir -p ~/$REMOTE_ROOT && tar -C ~/$REMOTE_ROOT -xzf ~/payload-$RID.tgz && rm ~/payload-$RID.tgz"

ENV_EXPORTS=""
if [[ -n "${MEWUI_REMOTE_ENV:-}" ]]; then
  IFS=';' read -ra PAIRS <<< "$MEWUI_REMOTE_ENV"
  for pair in "${PAIRS[@]}"; do
    [[ -n "$pair" ]] && ENV_EXPORTS+="export $pair; "
  done
fi

SESSION_ENV=""
if [[ "$RID" == linux-* ]]; then
  # The session manager keeps the display cookie under the user's runtime directory; older setups use ~/.Xauthority.
  SESSION_ENV="export DISPLAY='${MEWUI_REMOTE_DISPLAY:-:0}'; export XAUTHORITY=\"\$(ls -t /run/user/\$(id -u)/xauth_* 2>/dev/null | head -1)\"; [ -n \"\$XAUTHORITY\" ] || export XAUTHORITY=\"\$HOME/.Xauthority\"; "
fi

# The whole suite takes three to six minutes on the test machines, and a filtered run seconds. A run
# that hangs is given up that much sooner; MEWUI_REMOTE_TIMEOUT overrides both.
if [[ "$RUNNER_ARGS" == *--filter* ]]; then DEFAULT_TIMEOUT_SECONDS=120; else DEFAULT_TIMEOUT_SECONDS=480; fi
TIMEOUT_SECONDS="${MEWUI_REMOTE_TIMEOUT:-$DEFAULT_TIMEOUT_SECONDS}"

echo "== running in the remote GUI session"
STATUS=0
timeout "$TIMEOUT_SECONDS" ssh "$SSH_TARGET" "$REMOTE_PATH; $SESSION_ENV$ENV_EXPORTS cd ~/$REMOTE_ROOT/$PUBLISH_NAME && dotnet Aprillz.MewUI.WindowAutomationTest.dll --settings test.runsettings $RUNNER_ARGS" || STATUS=$?
if [[ $STATUS -eq 124 ]]; then
  # Giving up on the connection leaves the runner, and the windows it opened, on the remote desktop.
  echo "== timed out after $TIMEOUT_SECONDS s; stopping the runner"
  ssh "$SSH_TARGET" "pkill -f Aprillz.MewUI.WindowAutomationTest.dll" || true
fi
exit $STATUS
