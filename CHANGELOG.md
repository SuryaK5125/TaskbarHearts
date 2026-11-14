# Changelog

## v1.1.0

### New
- **Custom hotbar.** Turn on *Custom hotbar* and click *Choose items* to fill the popup's hotbar
  with any item from your Minecraft install (over 600 in 1.21), just for looks.
  - Click an item to drop it in the first free slot, or drag it onto any slot.
  - Items stay where you put them, gaps included. Dropping onto a filled slot nudges its
    neighbours along.
  - Middle-click for a full stack (64, or 16 for ender pearls, snowballs, eggs, signs and so on).
    Tools, armor and potions don't stack, like in the game.
  - Right-click a slot, or drag it out, to remove it. There's a search box for the item list.
- **Real Minecraft font.** Popup text now uses the game's own font, taken from your install.
  Without Minecraft it falls back to the built-in pixel font.
- **Durability bars in the hotbar.** Stats that are a percentage now show as a durability bar
  instead of a number, like a worn-out tool: free space on C: (now a diamond pickaxe), battery
  health (now an elytra), the top CPU process (now flint and steel, wearing down as it burns more
  CPU), volume (now a music disc) and how full your other drives are (bundle). Upload speed is now
  a firework rocket. Hover any of them for the exact numbers, as before.
- **Warning animations.** Hearts shake when the battery drops to 20% or below, and the hunger bar
  jitters when the CPU hits 90%, like in the game. They show on the taskbar and in the popup, and
  you can turn them off with *Animations*.
- **Windows 10 (experimental).** The Compact and Bars layouts now find the battery icon and the
  clock on the Windows 10 taskbar. Battery spot still needs Windows 11.

### Changed
- Download and upload speeds keep one decimal below 100 Mbps (`0.3`, `12.4`), so slow speeds no
  longer vanish from the hotbar.

### Fixed
- Icons could sit a pixel or two off at 125% display scaling.
- Automatic texture selection could pick an old modded profile (OptiFine, Forge, Fabric) over
  your newest Minecraft, because mod loaders record their install date as the release date. It
  now goes by the game version they're built on.
