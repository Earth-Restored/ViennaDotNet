#!/usr/bin/env bash
set -e

RED='\033[1;31m'
GRN='\033[1;32m'
YLW='\033[1;33m'
ORG='\033[38;5;208m'
CYN='\033[1;36m'
RST='\033[0m'

banner() {
    echo -e "${ORG}"
    echo "=============================="
    echo "    VIENNA DOTNET INSTALLER   "
    echo "=============================="
    echo -e "${RST}"
}

print_step() {
    echo ""
    echo -e "${CYN}========================================${RST}"
    echo -e "${CYN}  $1${RST}"
    echo -e "${CYN}========================================${RST}"
}

ok()   { echo -e "${GRN}[OK] $1${RST}"; }
skip() { echo -e "${YLW}[SKIP] $1${RST}"; }
err()  { echo -e "${RED}[ERROR] $1${RST}"; exit 1; }

banner

# ─────────────────────────────────────────
#  TERMUX BRANCH
# ─────────────────────────────────────────
if [ -n "$TERMUX_VERSION" ] || echo "$PREFIX" | grep -q "com.termux"; then

    print_step "TERMUX DETECTED"

    print_step "1. CHECKING PROOT-DISTRO"
    if ! command -v proot-distro >/dev/null 2>&1; then
        pkg update -y && pkg install -y proot-distro
        ok "Installed proot-distro"
    else
        skip "Already installed"
    fi

    print_step "2. CHECKING UBUNTU"
    if proot-distro login ubuntu -- true 2>/dev/null; then
        skip "Ubuntu already installed"
    else
        proot-distro install ubuntu
        ok "Ubuntu installed"
    fi

    print_step "3. CONFIGURING UBUNTU"
    proot-distro login ubuntu -- bash << 'EOF'
set -e

echo "[1] System update"
apt update -y

echo "[2] Installing dependencies"
apt install -y wget fzf curl unzip gnupg software-properties-common \
    apt-transport-https ca-certificates openjdk-21-jre libicu-dev

if ! command -v pwsh >/dev/null 2>&1; then
    echo "[3] Installing PowerShell"
    mkdir -p /opt/microsoft/powershell/7
    cd /opt/microsoft/powershell/7
    wget -q https://github.com/PowerShell/PowerShell/releases/download/v7.6.1/powershell-7.6.1-linux-arm64.tar.gz
    tar zxf powershell-7.6.1-linux-arm64.tar.gz
    chmod +x pwsh
    ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
fi

if [ ! -d "$HOME/.dotnet" ] || ! "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q "^10\."; then
    echo "[4] Installing .NET 10"
    cd ~
    wget -q https://dot.net/v1/dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 10.0
fi

grep -q DOTNET_ROOT ~/.bashrc || echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
grep -q ".dotnet/tools" ~/.bashrc || echo 'export PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools' >> ~/.bashrc
grep -q COMPlus_gcServer ~/.bashrc || {
    echo 'export COMPlus_gcServer=0'         >> ~/.bashrc
    echo 'export COMPlus_gcConcurrent=1'     >> ~/.bashrc
    echo 'export DOTNET_GCHeapHardLimit=268435456' >> ~/.bashrc
}

mkdir -p ~/Vienna

echo "[5] Downloading pre-compiled server"
cd ~

URL=$(curl -s https://api.github.com/repos/FroquaCubez/ViennaDotNet-PreCompiled/releases/tags/v1 \
    | grep browser_download_url \
    | grep linux-arm64 \
    | cut -d '"' -f 4)

[ -z "$URL" ] && { echo "[ERROR] Could not find download URL"; exit 1; }

wget -q "$URL"
unzip -o ViennaDotNet-linux-arm64.zip
rm -rf ~/Vienna/*

if [ -d ViennaDotNet-linux-arm64 ]; then
    mv ViennaDotNet-linux-arm64/* ~/Vienna/
    rm -rf ViennaDotNet-linux-arm64
else
    mv run_launcher.ps1 ~/Vienna/ 2>/dev/null || true
    mv components       ~/Vienna/ 2>/dev/null || true
    mv launcher         ~/Vienna/ 2>/dev/null || true
    mv staticdata       ~/Vienna/ 2>/dev/null || true
fi

chmod -R +x ~/Vienna/components/ 2>/dev/null || true
echo "[DONE]"
EOF

    ok "Ubuntu configured"

print_step "4. CREATING EARTH COMMAND"

mkdir -p "$PREFIX/bin"

curl -fsSL https://raw.githubusercontent.com/FroquaCubez/ViennaDotNet-PreCompiled/refs/heads/main/TermuxVienna.sh -o "$PREFIX/bin/earth"

chmod +x "$PREFIX/bin/earth"

ok "earth command installed"

echo ""
echo "=============================="
echo " INSTALL COMPLETE"
echo "=============================="
echo "Run: earth"
echo "IMPORTANT: Open Info inside the menu first"
echo "=============================="
exit 0
fi

# ─────────────────────────────────────────
#  LINUX / MACOS BRANCH
# ─────────────────────────────────────────

# Detect the real user even when run via sudo
if [ -n "$SUDO_USER" ]; then
    CURRENT_USER="$SUDO_USER"
else
    CURRENT_USER=$(whoami)
fi

HOME_DIR=$(eval echo "~$CURRENT_USER")
INSTALL_DIR="$HOME_DIR/vienna-dotnet-server"
REPO_DIR="$INSTALL_DIR/ViennaDotNet"
SERVICE_FILE="/etc/systemd/system/vienna.service"

OS=$(uname -s)
case $(uname -m) in
    x86_64)        ARCH_PROFILE="x64"   ; JAVA_ARCH="amd64" ;;
    aarch64|arm64) ARCH_PROFILE="arm64" ; JAVA_ARCH="arm64" ;;
    *) err "Unsupported architecture: $(uname -m)" ;;
esac

if [ "$OS" = "Darwin" ]; then
    PROFILE="framework-dependent-osx-$ARCH_PROFILE"
else
    PROFILE="framework-dependent-linux-$ARCH_PROFILE"
fi

BUILD_DIR="$REPO_DIR/build/Release/$PROFILE"

export DOTNET_ROOT="$HOME_DIR/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"

detect_pkg_manager() {
    if [ "$OS" = "Darwin" ]; then
        PKG_MANAGER="brew"
    elif command -v apt-get &>/dev/null; then
        PKG_MANAGER="apt"
    elif command -v dnf &>/dev/null; then
        PKG_MANAGER="dnf"
    elif command -v pacman &>/dev/null; then
        PKG_MANAGER="pacman"
    elif command -v zypper &>/dev/null; then
        PKG_MANAGER="zypper"
    else
        err "No supported package manager found (apt, dnf, pacman, zypper, brew)."
    fi
    echo "Detected package manager: $PKG_MANAGER"
}

pkg_install() {
    case $PKG_MANAGER in
        apt)    apt-get install -y "$@" ;;
        dnf)    dnf install -y "$@" ;;
        pacman) pacman -S --noconfirm "$@" ;;
        zypper) zypper install -y "$@" ;;
        brew)   sudo -u "$CURRENT_USER" brew install "$@" ;;
    esac
}

pkg_update() {
    case $PKG_MANAGER in
        apt)    apt-get update -qq ;;
        dnf)    dnf check-update -q || true ;;
        pacman) pacman -Sy --noconfirm ;;
        zypper) zypper refresh ;;
        brew)   sudo -u "$CURRENT_USER" brew update ;;
    esac
}

install_java() {
    case $PKG_MANAGER in
        apt)    pkg_install openjdk-17-jre ;;
        dnf)    pkg_install java-17-openjdk ;;
        pacman) pkg_install jre17-openjdk ;;
        zypper) pkg_install java-17-openjdk ;;
        brew)   pkg_install openjdk@17 ;;
    esac
}

install_pwsh() {
    case $PKG_MANAGER in
        apt)
            wget -q "https://packages.microsoft.com/config/$(. /etc/os-release && echo "$ID")/$(. /etc/os-release && echo "$VERSION_ID")/packages-microsoft-prod.deb" \
                -O /tmp/packages-microsoft-prod.deb 2>/dev/null \
            || wget -q "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" \
                -O /tmp/packages-microsoft-prod.deb
            dpkg -i /tmp/packages-microsoft-prod.deb
            apt-get update -qq
            pkg_install powershell
            ;;
        dnf)
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            dnf install -y "https://packages.microsoft.com/rhel/9/prod/packages-microsoft-prod.rpm" 2>/dev/null || true
            pkg_install powershell
            ;;
        pacman)
            sudo -u "$CURRENT_USER" bash -c "
                git clone https://aur.archlinux.org/powershell-bin.git /tmp/powershell-bin
                cd /tmp/powershell-bin && makepkg -si --noconfirm
            "
            ;;
        zypper)
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            zypper addrepo https://packages.microsoft.com/rhel/9/prod/ microsoft
            pkg_install powershell
            ;;
        brew)
            pkg_install powershell
            ;;
    esac
}

install_service() {
    if [ "$OS" = "Darwin" ]; then
        PLIST="$HOME_DIR/Library/LaunchAgents/com.vienna.server.plist"
        PWSH_PATH=$(command -v pwsh)
        cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.vienna.server</string>
    <key>ProgramArguments</key>
    <array>
        <string>$PWSH_PATH</string>
        <string>./run_launcher.ps1</string>
    </array>
    <key>WorkingDirectory</key>
    <string>$BUILD_DIR</string>
    <key>EnvironmentVariables</key>
    <dict>
        <key>DOTNET_ROOT</key>
        <string>$HOME_DIR/.dotnet</string>
        <key>PATH</key>
        <string>$HOME_DIR/.dotnet:/usr/local/bin:/usr/bin:/bin</string>
        <key>DOTNET_SYSTEM_NET_DISABLEIPV6</key>
        <string>1</string>
    </dict>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>$BUILD_DIR/logs/vienna.log</string>
    <key>StandardErrorPath</key>
    <string>$BUILD_DIR/logs/vienna.err</string>
</dict>
</plist>
EOF
        sudo -u "$CURRENT_USER" launchctl unload "$PLIST" 2>/dev/null || true
        sudo -u "$CURRENT_USER" launchctl load "$PLIST"
    else
        cat > "$SERVICE_FILE" <<EOF
[Unit]
Description=Vienna DotNet Server Launcher
After=network.target

[Service]
Type=simple
User=$CURRENT_USER
WorkingDirectory=$BUILD_DIR
Environment=TERM=xterm-256color
Environment=DOTNET_ROOT=$HOME_DIR/.dotnet
Environment=PATH=$HOME_DIR/.dotnet:$HOME_DIR/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
Environment=DOTNET_SYSTEM_NET_DISABLEIPV6=1
ExecStart=/usr/bin/pwsh ./run_launcher.ps1
StandardInput=null
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF
        systemctl daemon-reload
        systemctl enable vienna.service
    fi
}

start_service() {
    if [ "$OS" = "Darwin" ]; then
        sudo -u "$CURRENT_USER" launchctl start com.vienna.server
    else
        systemctl start vienna.service
    fi
}

stop_service() {
    if [ "$OS" = "Darwin" ]; then
        sudo -u "$CURRENT_USER" launchctl stop com.vienna.server 2>/dev/null || true
    else
        systemctl stop vienna.service 2>/dev/null || true
    fi
}

if [ "$OS" != "Darwin" ] && [ "$EUID" -ne 0 ]; then
    err "Please run the script as root!"
fi

detect_pkg_manager

print_step "1. INSTALLING DEPENDENCIES"
pkg_update
pkg_install curl git wget unzip

if ! command -v java &>/dev/null; then
    install_java
else
    skip "Java already installed"
fi

if ! command -v pwsh &>/dev/null; then
    install_pwsh
else
    skip "PowerShell already installed"
fi

if ! command -v dotnet &>/dev/null || ! dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
    wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    sudo -u "$CURRENT_USER" bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME_DIR/.dotnet"
    ok ".NET 10 installed"
else
    skip ".NET 10 already installed"
fi

print_step "2. STOPPING EXISTING SERVICE"
stop_service
sleep 2

print_step "3. PULLING LATEST CODE FROM GITHUB"
mkdir -p "$INSTALL_DIR"
chown "$CURRENT_USER":"$(id -gn "$CURRENT_USER")" "$INSTALL_DIR"

if [ -d "$REPO_DIR/.git" ]; then
    cd "$REPO_DIR"
    git remote set-url origin https://github.com/Earth-Restored/ViennaDotNet.git
    git fetch origin main
    git reset --hard origin/main
    git submodule update --init --recursive
    ok "Repository updated"
else
    sudo -u "$CURRENT_USER" git clone --recurse-submodules https://github.com/Earth-Restored/ViennaDotNet.git "$REPO_DIR"
    cd "$REPO_DIR"
    ok "Repository cloned"
fi

print_step "4. BUILDING SERVER"
sudo -u "$CURRENT_USER" env \
    DOTNET_ROOT="$HOME_DIR/.dotnet" \
    PATH="$HOME_DIR/.dotnet:$PATH" \
    pwsh ./publish.ps1 --profiles "$PROFILE"

print_step "5. PREPARING BUILD ENVIRONMENT"
cd "$BUILD_DIR"
cp *.json components/ 2>/dev/null || true
mkdir -p logs/EventBusServer logs/ObjectStoreServer logs/ApiServer logs/TileRenderer

print_step "6. INSTALLING SERVICE"
install_service

print_step "7. STARTING SERVER"
start_service

echo ""
echo -e "${GRN}==============================${RST}"
echo -e "${ORG}     INSTALL COMPLETE         ${RST}"
echo -e "${GRN}==============================${RST}"
echo ""
echo "   User:    $CURRENT_USER"
echo "   OS:      $OS ($PKG_MANAGER)"
echo "   Arch:    $PROFILE"
echo "   Install: $REPO_DIR"
echo "   Build:   $BUILD_DIR"
echo ""
echo "Next steps:"
echo "  1. Open http://localhost:5000 and create your admin account"
echo "  2. Under 'Server Options', set Network/IPv4 Address to your PC's IP"
echo "  3. Under 'Server Status', click Start"
echo "  4. Accept the Minecraft EULA when prompted in the logs"
echo ""
if [ "$OS" = "Darwin" ]; then
    echo "Useful commands:"
    echo "  tail -f $BUILD_DIR/logs/vienna.log   → live logs"
    echo "  launchctl stop com.vienna.server      → stop"
    echo "  launchctl start com.vienna.server     → start"
else
    echo "Useful commands:"
    echo "  sudo journalctl -u vienna.service -f     → live logs"
    echo "  sudo systemctl status vienna.service     → status"
    echo "  sudo systemctl restart vienna.service    → restart"
fi
