#!/data/data/com.termux/files/usr/bin/bash

REMOTE_URL="https://raw.githubusercontent.com/FroquaCubez/ViennaDotNet-PreCompiled/refs/heads/main/TermuxVienna.sh"
SELF_PATH="$(realpath "$0")"

echo "Checking for updates..."

update_self() {
    command -v curl >/dev/null 2>&1 || return

    TMP_PATH="$(mktemp -u /data/data/com.termux/files/usr/tmp/.earth_update_XXXXXX)"

    curl -fsSL --max-time 5 "$REMOTE_URL" -o "$TMP_PATH" 2>/dev/null

    if [ -s "$TMP_PATH" ]; then
        chmod +x "$TMP_PATH"

        if ! cmp -s "$TMP_PATH" "$SELF_PATH"; then
            mv "$TMP_PATH" "$SELF_PATH"
            echo "[earth] updated, restarting..."
            exec "$SELF_PATH" "$@"
        else
            rm -f "$TMP_PATH"
        fi
    else
        rm -f "$TMP_PATH"
    fi
}

update_self "$@"

# =========================
#        MAIN SCRIPT 
# =========================

proot-distro login ubuntu -- bash << 'EOF'

DB=~/Vienna/nohup.log
PID_FILE=~/Vienna/server.pid
TIME_FILE=~/Vienna/server.start

mkdir -p ~/Vienna

is_running() {
    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        kill -0 "$PID" 2>/dev/null && return 0
    fi
    pgrep -f run_launcher.ps1 >/dev/null 2>&1
}

get_pid() {
    [ -f "$PID_FILE" ] && cat "$PID_FILE"
}

start_server() {
    if is_running; then return; fi

    cd ~/Vienna || exit 1

    export DOTNET_ROOT=$HOME/.dotnet
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
    export COMPlus_gcServer=0

    setsid pwsh run_launcher.ps1 > "$DB" 2>&1 &

    PID=$!
    echo "$PID" > "$PID_FILE"
    date +%s > "$TIME_FILE"
}

stop_server() {
    if ! is_running; then return; fi

    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        PGID=$(ps -o pgid= "$PID" 2>/dev/null | tr -d ' ')
        kill -- -"$PGID" 2>/dev/null
        kill "$PID" 2>/dev/null
    fi

    pkill -f run_launcher.ps1 2>/dev/null
    fuser -k 5000/tcp 2>/dev/null

    rm -f "$PID_FILE" "$TIME_FILE"
}

toggle_server() {
    if is_running; then
        CH=$(printf "Yes\nNo" | fzf --height=20% --reverse --border --prompt="Stop server? > ")
        [ "$CH" = "Yes" ] && stop_server
    else
        start_server
    fi
}

process_viewer() {
while true; do
clear

PID=$(get_pid)

if is_running && [ -f "$TIME_FILE" ]; then
    NOW=$(date +%s)
    START=$(cat "$TIME_FILE")
    UPTIME_SEC=$((NOW - START))

    DAYS=$((UPTIME_SEC/86400))
    HOURS=$(( (UPTIME_SEC%86400)/3600 ))
    MINS=$(( (UPTIME_SEC%3600)/60 ))

    UPTIME_TEXT="${DAYS}d ${HOURS}h ${MINS}m"
else
    UPTIME_TEXT="--"
fi

LOAD=$(uptime 2>/dev/null | awk -F'load average:' '{print $2}' | cut -c2-)

MEM_TOTAL=$(grep MemTotal /proc/meminfo 2>/dev/null | awk '{print $2}')
MEM_AVAIL=$(grep MemAvailable /proc/meminfo 2>/dev/null | awk '{print $2}')

if [ -n "$MEM_TOTAL" ] && [ -n "$MEM_AVAIL" ]; then
    MEM_USED=$((MEM_TOTAL - MEM_AVAIL))
    MEM_PCT=$(awk "BEGIN {printf \"%.1f\", ($MEM_USED/$MEM_TOTAL)*100}")
else
    MEM_PCT="?"
fi

PROC_COUNT=$(ps -eo cmd 2>/dev/null | grep -E "pwsh|Launcher|ApiServer|EventBus|ObjectStore|TileRenderer|BuildplateLauncher" | grep -v grep | wc -l)

if is_running; then
    echo "ViennaTermux [RUNNING] http://localhost:5000"
    printf "Uptime: %s | RAM: %s%% | Processes: %s\n" \
    "$UPTIME_TEXT" "$MEM_PCT" "$PROC_COUNT"
else
    echo "ViennaTermux [STOPPED]"
fi

echo "────────────────────────────────"
printf "Load: %s\n" "$LOAD"
echo "────────────────────────────────"
echo ""

SELECT=$(
{
echo "Back to Main Menu"

if [ -n "$PID" ]; then
    ps -eo pid,ppid,cmd --no-headers 2>/dev/null | \
    grep -E "pwsh|Launcher|ApiServer|EventBus|ObjectStore|TileRenderer|BuildplateLauncher" | \
    grep -v grep
fi
} | fzf --height=50% --reverse --border --prompt="Process > "
)

[ -z "$SELECT" ] && continue
[ "$SELECT" = "Back to Main Menu" ] && return

SELPID=$(echo "$SELECT" | awk '{print $1}')
[[ "$SELPID" =~ ^[0-9]+$ ]] || continue

while true; do
clear
echo "==== PROCESS LOG ===="
echo "PID: $SELPID"
echo ""
tail -n 120 "$DB"

CH=$(printf "Refresh\nBack" | fzf --height=10% --reverse --border --prompt="Log > ")
[ "$CH" = "Back" ] && break
done

done
}

info_panel() {
while true; do
clear

echo "======================================="
echo " INFO"
echo "======================================="
echo
echo "Made with ♡ by Cosmetide"
echo
echo "ViennaDotNet Storage:"
echo "- Files are stored inside Ubuntu using proot-distro"
echo "- Enter Ubuntu with: proot-distro login ubuntu"
echo
echo "Admin Panel Configuration:"
echo "- If running patched Minecraft Earth on same device:"
echo "  → Use IP: 127.0.0.1"
echo
echo "MapTiler:"
echo "https://cloud.maptiler.com/account/keys/"
echo
echo "======================================="

CHOICE=$(printf "Back\n" | fzf --height=20% --reverse --border --prompt="Info > ")
[ "$CHOICE" = "Back" ] && return

done
}

open_admin_panel() {
termux-open-url "http://localhost:5000" 2>/dev/null || echo "Open: http://localhost:5000"
sleep 2
}

while true; do
clear

if is_running; then
    TITLE="ViennaTermux [RUNNING] http://localhost:5000"
else
    TITLE="ViennaTermux [STOPPED]"
fi

CHOICE=$(printf "%s\n%s\n%s\n%s\n%s\n" \
"Start/Stop Server" \
"Process Explorer" \
"Open Admin Panel" \
"Info" \
"Exit" \
| fzf --height=50% --reverse --border --prompt="$TITLE > ")

case "$CHOICE" in
"Start/Stop Server") toggle_server ;;
"Process Explorer") process_viewer ;;
"Open Admin Panel") open_admin_panel ;;
"Info") info_panel ;;
"Exit") exit 0 ;;
esac

done

EOF
