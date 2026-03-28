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

- First thing first, make sure you have the [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) installed on your PC;
- Clone the repository by running the following command on your terminal:
```
git clone https://github.com/Earth-Restored/ViennaDotNet.git
```
- CD to the ViennaDotNet directory, then run "publish.ps1";
- CD to build/{configuration}/{profile};
- Run "run_launcher.ps1";
- Now on the same device open http://localhost:5000, create an account and login;
- Under "Server Options", set "Network/IPv4 Address" to your PC's IP address and either disable "Map/Enable Tile Rendering" or set the "Map/MapTiler API Key" (it can be found [here](https://cloud.maptiler.com/account/keys/) when logged in);
- Under "Server Status", click "Start";
- Accept the Minecraft Server's EULA when prompted in the Launcher's logs;
- Download and move the "resourcepack" file as described in the Launcher's logs;
- Download a tool to patch Minecraft Earth's apk, such as [Project Earth's patcher;](https://archive.org/download/dev.projectearth.patcher-1.0/dev.projectearth.patcher-1.0.apk)
- Install the app on your device;
- Make sure you have a LEGAL copy of Minecraft Earth installed on that same device;
- Open the patcher, press on the 3 dots then go to Settings;
- Under Locator Server, set the following:
```
http://YOURPCIPADDRESS:8080
```
- Now go back and start patching;
- Once that's done, congratulations! You can now open the newly installed app and play Minecraft Earth!

## Common Errors & Troubleshooting

### I cannot see the "Start Server" button when logged in
**Cause:** Only the very first account created on the launcher is granted full administrative permissions by default. Subsequent accounts lack the necessary privileges to manage the server.

**Solutions:**
* **Option A (Grant Permissions):** Log into the original (first) account and use the Manage Users/Roles page to grant server permissions to your second account.
* **Option B (Reset Database):** If you have lost access to the first account and need to start fresh, you can reset the user database. 
    * Navigate to: `launcher/Data/`
    * **Delete** the `app.db` file.
    * *Note: This will remove all existing accounts and allow you to register a new primary admin account.*