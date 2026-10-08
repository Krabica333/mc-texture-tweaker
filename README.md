<img width="300" height="300" alt="McTextureTweaker" src="https://github.com/user-attachments/assets/c4c5a579-8a70-4300-abdb-d3aa476e63da" />
# McTextureTweaker

> A modern, fast, native desktop tool for creating Minecraft resource packs — recolor vanilla and mod textures, paint pixel art on them, and export everything in one go.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-11.2-8B44AC)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What is McTextureTweaker?

McTextureTweaker is a **texture editor** for Minecraft Java Edition. Point it at a client `.jar`, pick blocks from a searchable list, and let it recolor every texture with one of **five shading engines**. Fine-tune individual textures with per-dye overrides, paint pixel art on top, preview everything in a live 3D viewport, then export a folder of ready-to-use PNGs.

It works offline, without touching Minecraft's install, and without needing Photoshop.

---

## Features

### Collect blocks
- Scan **any Minecraft version** from 1.12.2 through the latest by reading `assets/` directly out of the client jar.
- Also scans **mod jars** (Fabric / NeoForge / Forge) — including nested jar-in-jar assets.
- Automatic family detection (`red_wool`, `blue_wool`, … collapse into the "wool" family).
- Search, filter by namespace, and multi-select with checkboxes.

### Recolor & preview
- **Five recolor methods**:
  - Average match + dark/bright compensation
  - Classic multiply
  - Perceptual (OKLab, hue-shifted shadows)
  - Pick-a-pixel (Paint.NET style)
  - HSV match (channel flattening)
- **Blend two methods** with a mix slider.
- Per-dye **exception profiles** — override the recolor settings for specific dyes (up to 6 per texture) while everything else keeps the default.
- **Per-category colors** — wool, brick, text, or your own custom categories — with individual hex values per dye.
- **Adjustments** — brightness, contrast, vibrance, clarity, hue shift — applied either before recoloring, after, or both.
- **Real-time 3D viewport** with drag-to-rotate, scroll-to-zoom, and animated zoom-in on entry.
- Supports both **cube** models and **cross** models (flowers, saplings, grass).
- Multi-face blocks (crafting table, furnace, piston, cartography table, logs) render with the correct texture per face.

### Paint (pixel art editor - in early stage development)
- 16×16 grid canvas with optional grid overlay and zoom.
- **Tools**: Pencil, Eraser, Fill (flood fill), Eyedropper, Line, Rectangle, Rect-filled, Ellipse, Ellipse-filled.
- **Right-click to erase** and **Alt-click to pick color**, regardless of current tool.
- **Layers** — add, duplicate, merge down, reorder, per-layer visibility and opacity.
- **Transform active layer** — flip horizontal / vertical, rotate 90° CW / CCW.
- **Palette** — 20 basic colors + a customizable saved palette + auto-tracked recent colors.
- Any hex color, saved into your custom palette with one click.
- **Undo / Redo** — up to 40 steps.
- **Save PNG** — export the composited texture immediately, or leave it in the project for the batch exporter.
- Paints on top of the recolored output by default; toggle **"Recolored base"** off to paint on the raw source texture.

### Export
- Batch export **every texture × every dye** with a customizable filename template (`{dye}_{name}.png`).
- Paint layers are composited on top of the recolored output automatically.
- Skipped automatically for textures without a category.
- Progress reported in the status bar.

### Project management
- Multiple projects, each with its own dyes, categories, textures, and export folder.
- Saved to `%LOCALAPPDATA%\.mctexturetweaker\projects\` (Windows) or the platform equivalent.
- Atomic saves — no more corrupt project files after a crash.

---

## Screenshots

*(Add your own here. Suggested: Stage 1 with the block list, Stage 2 with the 3D preview and slider panel, Stage 3 with the paint canvas, Stage 4 export.)*

---

## Installation

### Option A — Installer (recommended)

1. Download `McTextureTweaker-Setup-x.y.z.exe` from the [Releases](../../releases) page.
2. Run it. Follow the wizard.
3. Launch **McTextureTweaker** from the Start Menu or desktop shortcut.

No admin rights required. Uninstall via Windows Settings → Apps.

### Option B — Portable

1. Download `McTextureTweaker.exe` (single-file, self-contained) from [Releases](../../releases).
2. Put it anywhere — USB stick, desktop, `C:\Tools\`.
3. Double-click. Done.

### Requirements

- **Windows 10 / 11** (x64)
- .NET 10 Desktop Runtime — **already bundled** in the installer and portable builds
- Minecraft Java Edition client jar (any version from 1.12.2 up)
- Optional: mod jars you want to scan

---

## Quick start

1. **Launch McTextureTweaker** and pick a project (or create a new one).
2. **Stage 1 — Collect blocks**:
   - Click **Browse client jar…** and pick your Minecraft client jar (`%APPDATA%\.minecraft\versions\1.20.1\1.20.1.jar`).
   - Click **Scan**. The block list populates.
   - Type a name in the search box, tick checkboxes next to the blocks you want, and click **Add checked block(s)**.
3. **Stage 2 — Recolor & Preview**:
   - Click a texture on the left. Its dye previews appear.
   - Pick a dye (try `red`, `blue`, `purple`).
   - Drag sliders and watch the 3D preview update live.
   - Click **Reset all sliders** if you want to start over.
4. **Stage 3 — Paint** (optional):
   - Draw on top of the recolored texture with the pixel tools.
   - Add layers, use the palette, save a custom color for later.
   - Click **Save PNG…** for a one-off, or leave it for the batch export.
5. **Stage 4 — Export**:
   - Set the filename pattern (`{dye}_{name}.png` is the default).
   - Click **Select destination…** to pick an output folder.
   - Click **Export all textures × all dyes**.

Your PNGs are ready to be dropped into a resource pack's `assets/minecraft/textures/block/` folder.

---

## Recolor methods explained

| Method | What it does | When to use it |
|---|---|---|
| **Average match + compensation** | Shifts the texture so its average becomes the target dye color. Dark/bright colors get extra contrast compensation so they stay readable. | Default for most textures. Great for stone, wood, wool, concrete. |
| **Classic multiply** | Multiplies each pixel by the dye color. Simple, flat look. | Historical Minecraft-style wool/terracotta recolors. |
| **Perceptual (OKLab)** | Measures shading in OKLab space, so dark and bright colors keep their contrast. Shadows and highlights rotate hue in opposite directions. | Rich, saturated recolors with visible hue movement — good for gemstones, flowers, unusual palettes. |
| **Pick-a-pixel** | You click a pixel; that pixel's color becomes the dye color, every other pixel moves with it. | Matching a specific vanilla color exactly, or when you want to preserve an existing color relationship. |
| **HSV match** | Shifts hue / saturation / value independently toward the target, with a shadow-hue split and saturation boost. | Anything where you want the shading to be tinted differently from the base color — metal armor, glowing textures, magical items. |

You can **blend two methods** with the mix slider — for example, `Average × HSV` at 50% gives you a stable base with a richer shadow tint.

---

## File locations

| What | Where |
|---|---|
| **Projects** | `%LOCALAPPDATA%\.mctexturetweaker\projects\` |
| **Cached textures** | `%LOCALAPPDATA%\.mctexturetweaker\projects\<name>\cache\` |
| **Exports** | Whatever folder you selected in Stage 4 |
| **Custom colors** | Stored per texture, inside the project JSON |

Nothing is written into the Minecraft installation folder. Nothing is written into the app install folder.

---

## Building from source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10 or 11
- (Optional, for installers) [Inno Setup 6](https://jrsoftware.org/isinfo.php)

### Build

```powershell
git clone https://github.com/yourname/McTextureTweaker.git
cd McTextureTweaker
dotnet restore
dotnet build -c Debug
dotnet run
```

## Known limitations

1. Windows only for now. Avalonia supports Linux/macOS, and the code is platform-agnostic, but only Windows builds are produced.
2. Flat models (fire, lily pad, torch) render as cubes in the 3D preview. Also doors, cake, etc. are broken too. The exported PNGs are still correct. (I'll fix that in future)
3. Orientation on directional blocks always assumes front faces the camera. Real placement is handled by Minecraft's blockstate files at runtime, not by us. This means some block preview is slightly broken like beehive (I'll try to fix it)
4. No resource pack zip export yet — the exporter writes PNGs, not a packaged .zip with pack.mcmeta / blockstates / models. Coming in a future release.
5. No auto-update — you'll need to download a new release manually. (I'll add that in future)

## Credits

Author: KrabicaDev
UI framework: Avalonia
Icons and inspiration: Mojang's Minecraft assets (used only for reading, never redistributed)
Color math: Björn Ottosson's OKLab — used under public domain
Original Python prototype: this project began as a Python script called texture_tinter.py by **Tomeshec**

Changelog
v1.0.0 — Initial release
5 recolor methods with per-dye exception profiles
3D live preview (cube + cross models, multi-face support)
Pixel-art paint editor with layers, transforms, and undo
Batch export with customizable filename templates
Multi-project support with atomic saves
Mod jar scanning with jar-in-jar support
Portable single-file and installer builds


## License
MIT License

Copyright (c) 2026 KrabicaDev

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Minecraft is a trademark of Mojang Studios / Microsoft. This tool is not affiliated with, endorsed by, or sponsored by Mojang or Microsoft.
