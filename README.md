# Yuzu Presence for Playnite

Discord Rich Presence for the visual novels (and any other games) you launch from Playnite. Covers, developer, rating and length come from VNDB, and your games are linked to VNDB automatically. Choose exactly what shows up on Discord with a simple template system.

Built as a small contribution to the VN community.

------------

## Features

##### Discord Rich Presence

- Shows the game you are playing, with session timer and total playtime.
- Optional VNDB cover image, rating, developer, release year and length.
- Optional **View on VNDB** button on your status (text is customizable).
- Presence is cleared when the game closes or Playnite exits.

##### Customizable text

- Four editable fields: header, first line, second line and cover tooltip.
- Use placeholders such as `{name}` or `{developer}` (see [Template Variables](#template-variables)).
- Separate parts with `|`. A part whose placeholders are all empty is left out automatically, so there is no dangling "Rating /10".
- Choose romanized or original (e.g. Japanese) titles.

##### VNDB integration

- Matches your games to VNDB and adds the link to the game's **Links** tab.
- Runs on game launch, after a library update, or on demand with **Link all games to VNDB now**.
- **Strict matching** only links when the title, original title or an alias matches exactly.
- **Cover content filter**: show all, hide explicit, or hide suggestive and explicit covers.

##### When it is shown

- Master switch to turn presence on or off.
- Optionally only show games that have a VNDB entry.
- Skips hidden games by default.
- Skips any game with the `NoPresence` tag (the tag name is changeable).

## Installation

1. Download the latest `YuzuPresence_Playnite.pext` from the Releases page, or [build it from source](#build-from-source).
2. Drag the `.pext` file onto the Playnite window.
3. Restart Playnite.
4. Open **Add-ons → Extensions settings → Yuzu Presence**.

> If you used the earlier PowerShell version ("Hygger Presence"), uninstall it first so two copies don't fight over your Discord status. Your Application ID is imported automatically if it was configured there.

## Quick Start

You need a Discord Application to get an Application ID. It connects the extension to Discord and gives your status its default icon. The game's own name is shown after "Playing", so the application's name doesn't matter.

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications "Discord Developer Portal") and click **New Application**.
2. Type any name and click **Create**.
3. On **General Information**, click **Copy** under **Application ID**.
4. In Playnite, open **Add-ons → Extensions settings → Yuzu Presence** and paste it into **Discord Application ID**.
5. Click **Test connection** (the Discord desktop app has to be running).
6. Start a game and check Discord.

No art assets need to be uploaded. Covers are loaded directly from VNDB.

## Template Variables

Use these placeholders in the header, first line, second line and cover tooltip fields:

| Placeholder        | Meaning                                              |
| ------------------ | ---------------------------------------------------- |
| `{name}`           | Game name as it appears in Playnite                  |
| `{vndb_title}`     | VNDB title (romanized or original, per your setting) |
| `{original_title}` | VNDB original title                                  |
| `{developer}`      | Developer from VNDB                                  |
| `{rating}`         | VNDB rating out of 10                                |
| `{released}`       | Release year                                         |
| `{length}`         | Estimated length (e.g., `25h`)                       |
| `{total_time}`     | Total playtime in Playnite                           |
| `{platform}`       | Primary platform                                     |
| `{source}`         | Game source (e.g., `Steam`)                          |
| `{vndb}`           | VNDB id (e.g., `v17`)                                |

Defaults:

| Field             | Default                                   |
| ----------------- | ----------------------------------------- |
| Header            | `{name}`                                  |
| First line        | `{developer}`                             |
| Second line       | `Rating {rating}/10 \| Played {total_time}` |
| Cover tooltip     | `{vndb_title}`                            |

Tip: start with something obvious (e.g. `TEST · {name}`), check that it appears in Discord, then expand.

## Build from source

**Option A: on your PC**

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (6 or newer).
2. In the project folder run:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

3. You get `YuzuPresence_Playnite.pext`.

**Option B: GitHub Actions (no tools needed)**

1. Push the project to a GitHub repository (keep the `.github` folder).
2. Open the **Actions** tab and wait for **Build Playnite extension** to finish.
3. Download the `YuzuPresence_Playnite` artifact, which contains the `.pext`.

## Troubleshooting

- **Nothing shows on Discord**: make sure the Discord desktop app is running, the Application ID is correct, and press **Test connection**.
- **Wrong game linked**: turn on **Strict matching**, or edit the VNDB link in the game's **Links** tab.
- **Game not matched**: add the VNDB link manually and it will be used.
- **No cover**: check the cover content filter, or whether VNDB is used for cover and game info.

## Credits

- Data from [VNDB](https://vndb.org). Please be kind to their API.
- Built with the [Playnite SDK](https://api.playnite.link/docs/).

#### License

MIT

###### Not affiliated with Discord, VNDB or Playnite.
