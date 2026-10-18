# Yuzu Presence for Playnite (v3, native plugin)

Discord Rich Presence for every game you launch from Playnite, with VNDB covers/info, automatic VNDB link
registration, and a full settings page under **Add-ons > Extensions settings > Yuzu Presence**.

## Build the .pext

**Option A - on your PC**
1. Install the .NET SDK (6 or newer) from https://dotnet.microsoft.com/download
2. In this folder run:  `powershell -ExecutionPolicy Bypass -File build.ps1`
3. You get `YuzuPresence_Playnite.pext`.

**Option B - no tools needed**
1. Create a free GitHub repository and upload everything in this folder (keep the `.github` folder).
2. Open the repository's **Actions** tab, wait for "Build Playnite extension" to finish.
3. Download the `YuzuPresence_Playnite` artifact; it contains the `.pext`.

## Install
1. Remove the earlier PowerShell version first (it is listed as "Hygger Presence" under Add-ons > Installed > Library > Uninstall)
   so two copies don't fight over your Discord status. Restart Playnite.
2. Drag `YuzuPresence_Playnite.pext` onto the Playnite window and restart.
3. Open Add-ons > Extensions settings > Yuzu Presence and paste your Discord Application ID.
   (It is imported automatically from the old version if that was configured.)
