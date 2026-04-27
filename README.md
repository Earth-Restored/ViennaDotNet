![Banner](src/ViennaDotNet.LauncherUI/wwwroot/banner.png)
# ViennaDotNet
An unofficial port of [Project Vienna](https://github.com/Project-Genoa/Vienna) to .NET

> [!WARNING]
> **Work In Progress (WIP):** This project is currently under active development. Some features may be incomplete, and you may encounter bugs or breaking changes. Use at your own risk!

## Disclaimer

**ViennaDotNet** is an independent, community-driven project and is **not affiliated with, authorized, maintained, endorsed, or sponsored** by Microsoft Corporation, Mojang Studios, or any of their affiliates or subsidiaries.

* *Minecraft Earth™* is a trademark of Microsoft Corporation. All trademarks and registered trademarks are the property of their respective owners.
* This project does not distribute, host, or provide access to original game assets, proprietary binaries, or resource packs. Users are responsible for providing their own legally obtained assets.
* This software is provided solely for educational, research, and archival purposes to restore functionality to a discontinued service.
* This project is provided "as-is" without any warranty of any kind, express or implied. In no event shall the authors be held liable for any claim, damages, or other liability.

## New Features
In addition to the original Vienna feature set, this port adds:
- Shop
- Map rendering
- Admin panel

## Installation guide

### Requirements

- Hardware/OS: A PC running Windows or Linux.
   - Note: macOS is currently untested. High-end Android devices using Termux may work but are not officially supported.
- [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Java 17 (JRE or JDK), newer versions may not work
- On linux, [powershell](https://learn.microsoft.com/en-us/powershell/scripting/install/linux-overview) to run the build script

### Instructions

- Before you start, you'll need to know the IP address of your PC
 - Windows
   - Type `ipconfig` and press enter
   - Look for either `Wireless LAN adapter Wi-Fi` if you use WiFi or `Ethernet adapter Ethernet` if you use ethernet
   - Under it, there should be `IPv4 Address`
- Linux
   - Use a command such as `ip address`, `hostname -I` or `ifconfig -a`
   - The address will usually (but not always) be in the format `192.168.XXX.XXX`

## Semi-automatic (Linux, macOS, Termux only)
- Open your terminal and run the following command:
```
curl -sSl https://raw.githubusercontent.com/Earth-Restored/ViennaDotNet/main/install.sh | bash
```
- Wait until the command finishes
- Continue following the guide from the 4th point of the "Server" part in the Manual instructions

## Manual
#### Server

- Clone the repository by running the following command on your terminal:
```
git clone https://github.com/Earth-Restored/ViennaDotNet.git
```
1. CD to the ViennaDotNet directory, then run `publish.ps1 -profiles framework-dependent-{os}-{arch}`, replace `{os}` with you os (win, linux, osx) and `{arch}` with the cpu architecture (x64, x86, arm64, arm32), e.g. `framework-dependent-win-x64`
2. Run "run_launcher.ps1"
3. Now on the same device open http://localhost:5000, create an account, make sure you confirm your email on the page that opens, if you fail to do this, you need to [Delete account db (Option B)](#i-cannot-see-the-start-server-button-when-logged-in), and login
4. Under "Server Options", set "Network/IPv4 Address" to your PC's IP address and either disable "Map/Enable Tile Rendering" or set the "Map/MapTiler API Key" (it can be found [here](https://cloud.maptiler.com/account/keys/) when logged in)
5. Under "Server Status", click "Start"
6. Accept the Minecraft Server's EULA when prompted in the Launcher's logs
7. Download and move the "resourcepack" file as described in the Launcher's logs

#### Client

- For iOS you may use [this patcher](https://github.com/catdogmat/ProjectEarthiOSPatcher), but it is not officially supported

| Feature | Project Earth patcher | MCE Patcher |
| ------- | --------------------- | ----------- |
| Target Device | Android | Android |
| Patcher Runs On | Android | Windows, Linux, macOS |
| Login | Microsoft account only | Microsoft or custom |
| Shop | Requires that you have played the game before it shut down using the microsoft account | Always works if you use custom login |

##### Project Earth patcher

1. Download [the patcher](https://archive.org/download/dev.projectearth.patcher-1.0/dev.projectearth.patcher-1.0.apk)
2. Install the patcher on your device
3. Make sure you have a LEGAL copy of Minecraft Earth installed on that same device
4. Open the patcher, press on the 3 dots then go to Settings
5. Under Locator Server, set the following: `http://{ip}:8080`, replace `{ip}` with your PC's ip or hostnamr, **make sure you have http:// instead of https://**
6. Now go back and start patching
7. Once that's done, congratulations! You can now open the newly installed app and play Minecraft Earth!

##### MCE patcher

1. Download [the patcher](https://github.com/Earth-Restored/Minecraft_Earth_Patcher/releases) (UI is highly recommended) or build it from source
2. Acquire a Minecraft Earth apk, such as by dumping in from you phone.
2. Run the patcher.
3. Select the downloaded APK file.
4. Change locator Hostname/IP to `http://{ip}:8080`, replace `{ip}` with your PC's ip or hostname
- If you want to use non microsoft login, change the options like so (use the same ip/hostname as the locator):
![Correct options for replacement server](https://github.com/Earth-Restored/ViennaDotNet/blob/main/images/patcher-login-server-options.png?raw=true)
5. Click patch
6. Move the patched apk to your phone and install it
7. Once that's done, congratulations! You can now open the newly installed app and play Minecraft Earth!

#### Launcher Buildplate Preview
1. To enable the buildplate preview, you must first obtain the Minecraft 1.20.4 resource pack.
2. The simplest method is to extract the files directly from the game's JAR:
   - Locate and open '1.20.4.jar' in your Minecraft installation folder using an archive viewer (like 7-Zip).
   - Navigate to the 'assets/minecraft/' directory.
3. Copy all folders from 'assets/minecraft/' and paste them into:
   - 'staticdata/resourcepacks/java/minecraft/'
4. Finally, toggle 'Enable Buildplate Preview in Launcher' within ServerOptions/Data Handling.

## Common Errors & Troubleshooting

### I cannot see the "Start Server" button when logged in
**Cause:** Only the very first account created on the launcher is granted full administrative permissions by default. Subsequent accounts lack the necessary privileges to manage the server.

**Solutions:**
* **Option A (Grant Permissions):** Log into the original (first) account and use the Manage Users/Roles page to grant server permissions to your second account.
* **Option B (Reset Database):** If you have lost access to the first account and need to start fresh, you can reset the user database. 
    * Navigate to: `launcher/Data/`
    * **Delete** the `app.db` file.
    * *Note: This will remove all existing accounts and allow you to register a new primary admin account.*

### When I open the app, I get "Cannot connect to the network! ..."
**Possible causes**:
- Server is not running
- Incorrect PC IP address
- Firewall blocks the server
- PC and phone are not on the same network

### The app closes when I join a buildplate
**Possible causes**:
- The server took too long to start - quickly open the app and join the same buildplate again
- You do not have Java 17 installed - check that the `JAVA_HOME` environment variable is set to Java 17, or that `java --version` prints java 17
