# Taskbar Hearts

Your laptop battery as Minecraft hearts, right on the Windows 11 taskbar.

<!-- Add a screenshot or GIF here, e.g. ![Taskbar Hearts](docs/demo.gif) -->

Hearts show your battery, armor shows free RAM, and hunger shows how busy your CPU is. Hover over
them and you get a little Minecraft-style HUD with the exact numbers, plus a hotbar full of extra
stats.

**[Download the latest version](../../releases/latest)** · Windows 11 · free and open source

## What it does

- **Bars on your taskbar.** Pick one of three spots: a compact heart/hunger/armor trio on the
  battery icon, two rows of bars next to the tray, or bars that take over the battery spot itself.
- **Choose your bars.** Hearts + Armor, Hearts only, Hearts + Hunger, or Armor + Hunger (handy on
  a desktop PC with no battery).
- **A hover HUD.** Either a simple list, or one laid out like the real game: hearts, armor,
  hunger, your GPU as the XP bar, and a hotbar showing:

  | Item | Shows |
  | --- | --- |
  | Sugar / Feather | Download and upload speed |
  | Chest minecart | Free space on C: |
  | Clock | Uptime |
  | Golden apple | Battery health |
  | Redstone | Running processes |
  | Bundle | Other drives |
  | Book and quill | What's using the most CPU |
  | Music disc | Volume |

  Hover anything for the details. Click the HUD to keep it open; click anywhere else to close it.
- **Real game textures** come from your own Minecraft Java install. No Minecraft? It uses its own
  pixel art instead, so everything still works.

## Getting started

1. Download `TaskbarHearts.exe` from [Releases](../../releases/latest) and run it. There's
   nothing to install.
2. If Windows says *"Windows protected your PC"*, click **More info → Run anyway**. That happens
   because the app isn't code-signed; the full source is right here.
3. Look for the crafting table under the **^** in your system tray. Click it to change the layout,
   turn on *Start with Windows*, and so on. Right-click it for the menu, including Exit.

## Good to know

- It doesn't go online, collect anything, or need admin rights. It uses about 6 MB of memory.
- It sits on top of the battery icon rather than removing it; Windows doesn't let apps hide it.
- If your taskbar is transparent (TranslucentTB and similar), the **Bars** layout looks best.
- Settings live in `%LOCALAPPDATA%\TaskbarHearts`.
- **Uninstall:** Exit from the tray menu, then delete the exe and that settings folder. If you
  turned on *Start with Windows*, turn it off first.

## Something looks wrong?

Open the app, click **Copy diagnostics**, and paste it into a new issue. It tells me your Windows
version and taskbar setup, without your name or any folder paths.

## Building it yourself

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project tests\SurvivalHud.Tests -c Release
dotnet publish src\SurvivalHud -c Release -o publish
```

## Credits

Built with assistance from Claude, primarily for implementation and debugging.

Minecraft is a trademark of Mojang. This is an unofficial fan project, not affiliated with Mojang
or Microsoft, and it doesn't include any Minecraft files.

## License

MIT, see [LICENSE](LICENSE).
