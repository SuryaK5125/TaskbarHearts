# Taskbar Hearts

Minecraft-style survival bars on the Windows 11 taskbar: hearts for battery, hunger for CPU,
armor for free RAM. Unofficial; not affiliated with Mojang or Microsoft.

## Features

- **Three taskbar layouts:** Compact (one heart, hunger and armor on the battery icon), Survival Bars
  (10 armor over 10 hearts, left of the tray), and Battery spot (the same rows in place of the
  network/volume/battery icons; click them for Quick Settings).
- **Choose the bars:** hearts + armor (default), hearts only, hearts + hunger, or armor + hunger.
  Armor + hunger suits desktop PCs, which have no battery.
- **Hover popup** with exact values, in two styles:
  - *Classic:* labelled rows for battery, CPU, RAM and GPU.
  - *Minecraft-style:* laid out like the game's HUD. GPU is the XP bar and level, and the hotbar shows
    download/upload speed (sugar, feather), free space on C: (chest minecart), uptime (clock),
    battery health (golden apple) and processes (redstone). Hover anything for a tooltip.
- **Real game textures** read from your own Minecraft Java install (1.6 and newer, or a resource
  pack). Nothing from Minecraft is bundled; without an install it uses its own pixel art.
- Stays visible over Start and Quick Settings, follows light/dark theme, and uses about 6 MB of RAM idle.

## Install

Download `TaskbarHearts.exe` from Releases and run it. No installer or .NET needed.
A crafting-table icon appears under **Show hidden icons ^**:

- **Left-click:** app window (on/off, layouts, popup style, Start with Windows, diagnostics).
- **Right-click:** menu, including Exit.

Windows SmartScreen may warn that the app is unrecognised because it isn't code-signed.
The full source is here if you'd rather build it yourself.

## Build

Requires the .NET 10 SDK.

```
dotnet run --project tests\SurvivalHud.Tests -c Release
dotnet publish src\SurvivalHud -c Release -o publish
```

## Notes

- Windows 11 only. Desktop PCs without a battery show full hearts.
- The battery icon is covered, not removed; Windows has no API to hide it.
- Taskbar on the left or right edge (via taskbar mods) falls back to three tray icons.
- Transparent taskbars (TranslucentTB etc.) work best with the Survival Bars layout.
- Settings and log: `%LOCALAPPDATA%\TaskbarHearts\`.

## Reporting problems

Open the app window, click **Copy diagnostics**, and paste the result into an issue. It includes your
Windows build, taskbar setup and what the app detected, but no user name or folder paths.
