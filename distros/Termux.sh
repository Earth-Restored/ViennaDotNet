#!/data/data/com.termux/files/usr/bin/bash

REMOTE_URL="https://raw.githubusercontent.com/FroquaCubez/Solace/refs/heads/main/distros/Termux.sh"
SELF_PATH="$(realpath "$0")"

echo "Checking for updates..."

update_self() {
    command -v curl >/dev/null 2>&1 || return

    TMP_PATH="$(mktemp /data/data/com.termux/files/usr/tmp/.earth_update_XXXXXX)"

    curl -fsSL --max-time 5 "$REMOTE_URL" -o "$TMP_PATH" 2>/dev/null

    if [ -s "$TMP_PATH" ]; then
        chmod +x "$TMP_PATH"

        if ! cmp -s "$TMP_PATH" "$SELF_PATH"; then
            mv "$TMP_PATH" "$SELF_PATH"
            echo "[earth] updated"

            if [ -n "$PROOT" ]; then
                echo "Please exit proot environment and run the command again."
                exit 0
            else
                echo "[earth] restarting..."
                exec "$SELF_PATH" "$@"
            fi
        else
            rm -f "$TMP_PATH"
        fi
    else
        rm -f "$TMP_PATH"
    fi
}

update_self "$@"

proot-distro login ubuntu -- bash << 'EOF'

# =========================
#        MAIN SCRIPT 
# =========================

DB=~/Solace/nohup.log
PID_FILE=~/Solace/server.pid
TIME_FILE=~/Solace/server.start
STARTING_FILE=~/Solace/.starting

mkdir -p ~/Solace

is_running() {
    curl -s --max-time 1 http://127.0.0.1:5000 | grep -q .
}

is_process_alive() {
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
    if is_process_alive; then return; fi

    cd ~/Solace || exit 1

    export DOTNET_ROOT=$HOME/.dotnet
    export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools
    export COMPlus_gcServer=0

    echo "1" > "$STARTING_FILE"

    setsid pwsh run_launcher.ps1 > "$DB" 2>&1 &

    PID=$!
    echo "$PID" > "$PID_FILE"
    date +%s > "$TIME_FILE"

    (
        for i in $(seq 1 60); do
            if is_process_alive && curl -s --max-time 1 http://127.0.0.1:5000 | grep -q .; then
                rm -f "$STARTING_FILE"
                exit 0
            fi
            sleep 1
        done

        rm -f "$STARTING_FILE"
    ) &
}

stop_server() {
    if ! is_process_alive; then return; fi

    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE")
        PGID=$(ps -o pgid= "$PID" 2>/dev/null | tr -d ' ')
        kill -- -"$PGID" 2>/dev/null
        kill "$PID" 2>/dev/null
    fi

    pkill -f run_launcher.ps1 2>/dev/null
    fuser -k 5000/tcp 2>/dev/null

    rm -f "$PID_FILE" "$TIME_FILE" "$STARTING_FILE"
}

toggle_server() {
    if is_process_alive; then
        CH=$(printf "Yes\nNo" | fzf --height=20% --reverse --border --prompt="Stop server? > ")
        [ "$CH" = "Yes" ] && stop_server
    else
        check_eula || return
        start_server
    fi
}

check_eula() {
    EULA_FILE=~/Solace/staticdata/server_template_dir/eula.txt

    # If already accepted → skip
    if [ -f "$EULA_FILE" ] && grep -q "eula=true" "$EULA_FILE"; then
        return 0
    fi

    while true; do
        clear
        echo "======================================="
        echo "        MINECRAFT SERVER EULA"
        echo "======================================="
        echo ""
        echo "Before starting the server, you must accept"
        echo "the End User License Agreement (EULA)."
        echo ""
        echo "Read it here:"
        echo "https://aka.ms/MinecraftEULA"
        echo ""
        echo "======================================="

        CHOICE=$(printf "Yes, I agree\nNo, I deny" | fzf \
            --height=20% \
            --reverse \
            --border \
            --prompt="Accept EULA > ")

        case "$CHOICE" in
            "Yes, I agree")
                mkdir -p "$(dirname "$EULA_FILE")"
                echo "# By changing the setting below to TRUE you are indicating your agreement to the EULA." > "$EULA_FILE"
                echo "eula=true" >> "$EULA_FILE"

                echo "[earth] EULA accepted"
                sleep 1
                return 0
                ;;
            "No, I deny"|"")
                echo "[earth] You must accept the EULA to start the server"
                sleep 2
                return 1
                ;;
        esac
    done
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
    echo "Solace [RUNNING] http://localhost:5000"
    printf "Uptime: %s | RAM: %s%% | Processes: %s\n" \
    "$UPTIME_TEXT" "$MEM_PCT" "$PROC_COUNT"
else
    echo "Solace [STOPPED]"
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

update_solace() {
    while true; do
        clear

        echo "======================================="
        echo "        UPDATE Solace"
        echo "======================================="
        echo ""

        CURRENT_VERSION="unknown"
        [ -f ~/Solace/version.txt ] && CURRENT_VERSION=$(cat ~/Solace/version.txt)

        RELEASE_JSON=$(curl -s https://api.github.com/repos/Earth-Restored/Solace/releases)

        LATEST_TAG=$(echo "$RELEASE_JSON" | grep '"tag_name"' | head -n1 | cut -d '"' -f4)

        echo "Current Version: $CURRENT_VERSION"
        echo "Latest Version:  $LATEST_TAG"
        echo ""
        echo "Download Solace build?"
        echo ""
        echo "This will:"
        echo "- Replace updated files ONLY"
        echo "- Keep your databases untouched"
        echo ""
        echo "======================================="

        CHOICE=$(printf "Latest ($LATEST_TAG)\nOther versions...\nNo" \
            | fzf --height=20% --reverse --border --prompt="Select update option > ")

        [ "$CHOICE" = "No" ] && return

        if echo "$CHOICE" | grep -q "Other versions"; then
            TAG=$(echo "$RELEASE_JSON" \
                | grep '"tag_name"' \
                | cut -d '"' -f4 \
                | fzf --height=50% --reverse --border --prompt="Select version > ")
        else
            TAG="$LATEST_TAG"
        fi

        [ -z "$TAG" ] && return

        force_stop_server
        echo "[earth] preparing download for $TAG..."

        URL=$(echo "$RELEASE_JSON" \
            | grep -o '"browser_download_url": "[^"]*linux-arm64[^"]*"' \
            | cut -d '"' -f4 \
            | head -n1)

        [ -z "$URL" ] && echo "[earth] failed to get download URL" && sleep 2 && return

        TMP_DIR="$(mktemp -d ~/Solace_update_XXXXXX)"
        cd "$TMP_DIR" || return

        echo "[earth] downloading $TAG..."
        curl -L --fail "$URL" -o update.zip

        unzip -o update.zip >/dev/null 2>&1

        TARGET=~/Solace

        echo "[earth] applying update ($TAG)..."
        cp -r . "$TARGET"/
        echo "$TAG" > ~/Solace/version.txt
        echo "[earth] update complete ($TAG)"
        rm -rf "$TMP_DIR"

        sleep 2
        return
    done
}

force_stop_server() {
    if is_running; then
        echo "[earth] stopping server before update..."

        if [ -f "$PID_FILE" ]; then
            PID=$(cat "$PID_FILE")
            PGID=$(ps -o pgid= "$PID" 2>/dev/null | tr -d ' ')
            kill -- -"$PGID" 2>/dev/null
            kill "$PID" 2>/dev/null
        fi

        pkill -f run_launcher.ps1 2>/dev/null
        fuser -k 5000/tcp 2>/dev/null

        rm -f "$PID_FILE" "$TIME_FILE"
        sleep 2
    fi
}

info_panel() {
while true; do
clear

echo "======================================="
echo " INFORMATION"
echo "======================================="
echo
echo "Resourcepack:"
echo "- Check the server log on the admin panel or ask for help on the Discord server"
echo "- Location of the file: ~/Solace/staticdata/resourcepacks/vanilla.zip"
echo "- This can be accessed using the proot-distro command referred below"
echo
echo "Solace Storage:"
echo "- Files are stored inside Ubuntu using proot-distro"
echo "- Enter Ubuntu with: proot-distro login ubuntu"
echo
echo "Admin Panel Configuration:"
echo "- If you are running a patched Minecraft Earth APK on the same device:"
echo "  → Use IP: 127.0.0.1"
echo
echo "MapTiler Setup:"
echo "- Create an API key at: https://cloud.maptiler.com/account/keys/"
echo "- Add the API key inside the server admin panel settings"
echo
echo "APK:"
echo "- Patch your own LEGALLY obtained minecraft earth app"
echo "- and set the IP to 127.0.0.1 if you're using it on the same device"
echo
echo "Notes:"
echo "- This setup is intended for local device use only"
echo "- You can change the IP if you want to host it for multiple devices"
echo "- Make sure your APK is patched to match the server IP"
echo
echo "======================================="
echo ""

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

if [ -f "$STARTING_FILE" ]; then
    TITLE="Solace [STARTING...]"
elif is_running; then
    TITLE="Solace [RUNNING] http://localhost:5000"
else
    TITLE="Solace [STOPPED]"
fi

OPTIONS=(
"Start/Stop Server"
"Process Explorer"
"Open Admin Panel"
"Update Solace"
"Information"
"Exit"
)

CHOICE=$(printf "%s\n" "${OPTIONS[@]}" | fzf \
    --height=30% \
    --reverse \
    --border \
    --prompt="$TITLE > " \
    --no-multi \
    --ansi)

case "$CHOICE" in
"Start/Stop Server") toggle_server ;;
"Process Explorer") process_viewer ;;
"Open Admin Panel") open_admin_panel ;;
"Update Solace) update_solace ;;
"Information") info_panel ;;
"Exit") exit 0 ;;
esac

done

EOF
