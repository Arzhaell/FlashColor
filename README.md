# FlashColor

*[Version française](README.fr.md)*

A Dalamud plugin for Final Fantasy XIV: adjust the **contrast**, **saturation**, **exposure** and **white balance** of
the game's image, without ReShade, without clipping bright lights and without touching the interface.

## Installation
1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL, click **+**, tick the checkbox and save:
   ```
   https://raw.githubusercontent.com/Arzhaell/FlashColor/main/repo.json
   ```
3. Type `/xlplugins`, search for **FlashColor** and install it.

## Usage
`/flashcolor` opens the window. The interface is in English and French (it follows Dalamud's language, and can be set).
Keep the **Hold: original image** button pressed to compare with the game's image.

## Why not ReShade
ReShade works on the final image, already brought down to what the screen can show. A flame is already almost white
there: raising the contrast pushes it past white, it gets cut into a flat patch and its color slides towards yellow.
Effects that use depth also react badly to particles, which have none.

FlashColor works **inside the game's tone mapping**, while bright lights still go past white. The settings apply there,
then bright lights are **softened** instead of cut: each color tends towards white without ever reaching it, as on film
(an orange flame turns yellow then white), or keeps its hue if you prefer.

**Details are kept**: contrast, colors and softening apply to the average light around each pixel (an average over two
rings up to 1.5% of the image height, which ignores neighbors on the other side of a sharp edge), then the difference
between the pixel and that average, its texture, is put back as it was. Raising the colors therefore no longer flattens
the inside of flames or light textures. Zone tints spare vivid colors (an orange flame doesn't turn pink under a blue
tint).

## Settings
The window sorts the settings into tabs. Right-click a slider: default value.

- **Colors**: exposure, contrast, saturation, vibrance (mostly dull colors), temperature, tint, softening of bright
  lights (where it starts, color of bright lights), keep details.
- **Zones**: shadows, midtones and lights, each with its brightness and color (hue + strength). The three zones overlap
  smoothly according to brightness, from 4 stops below middle grey to 4 stops above.
- **Per color**: red, orange, yellow, green, cyan, blue, purple and magenta, each with its hue shift, saturation and
  brightness. Greys are never touched.
- **Details**: sharpness (immediate neighbors, limited around edges) and clarity (local contrast on a ring of 2% of the
  image height), on brightness only.
- **GPose filter**: one of the game's GPose filters, everywhere, with its strength (see below).
- **Flash**: see below.

The **…** menu next to the presets saves the settings as a new preset, updates, renames or deletes the chosen preset,
and copies or pastes the settings as text (`FlashColor:{…}`) to share them.

## Presets
- **Game image**: the game's colors, only bright lights are softened.
- **Measured on ReShade presets**: [Alive](https://www.nexusmods.com/finalfantasy14/mods/156) style (Wafflezs),
  [Arfien](https://www.nexusmods.com/finalfantasy14/mods/684) style,
  [Stamp's Enhanced Gameplay](https://www.nexusmods.com/finalfantasy14/mods/1651) style,
  [Colorful Fantasy](https://www.nexusmods.com/finalfantasy14/mods/790) style,
  [Simple and Clean](https://www.nexusmods.com/finalfantasy14/mods/1592) style.
- **Ambiances** made for FlashColor: Cinema, Soft pastel, Cold night, Sunset, Contrasted black and white, Very vivid.
- **GPose filters**: the game's 24 GPose filters (Sepia 1, Monochrome 1…), identical, everywhere.

The ReShade presets were **measured** on the before/after screenshots published by their authors: FlashColor's
calculation (exposure, contrast, saturation, vibrance, color and brightness of the three zones) was fitted to turn each
"before" image into its "after" image as closely as possible (difference of logarithms, 2,500 pixels per pair), with a
small penalty on extreme settings. Badly aligned pairs are discarded (REALXIV has no usable one). Nothing is taken from
their files. Their ambient shading, sharpening, fog and blur aren't colors: FlashColor doesn't reproduce them. Owl's
FFXIV Fantasy Reshades publishes no screenshot without its preset, and the screenshots of Alex's, Obsidian and Newfound
Adventures don't say which one has the preset: not measured.

## White flash
The **White flash** button recreates a cutscene flash: the scene's light jumps up (adjustable strength, +5 EV by
default, that is ×32), holds for a moment, then comes back down in about a second. **Flash, game image** plays the same
flash as the game would render it, bright lights cut, to compare. The flash goes through the tone mapping: what doesn't
change during the flash (the interface, for example) is drawn after it.

## GPose filters
The 24 GPose filters ("Color Filter") are available everywhere, outside GPose, identical: choose them in the **GPose
filter** tab or in the **GPose filters** preset group.

Each filter was **recorded in game** (on 2026-10-15, game version 2026.10.06): in GPose, what the game sends to its
filter shader (`ColorFilter_DarkBlend.shcd`) for each filter, that is a curve of 256 values, the same for all three
colors, then a 3 × 4 color matrix. Each record matches its row of the game's `ColorFilter` sheet exactly. FlashColor
does the same calculation at the end of its tone mapping, on the image cut at 1 like the one the game's filter receives.

- As in GPose, the chosen filter **replaces** the zone's color filter: the game's is set to zero while a FlashColor
  filter is active.
- It applies **after** the color settings, which stay usable with it. **Strength** mixes the unfiltered image (0%) and
  the filtered image (100%, as in the game).
- In GPose, a filter chosen in the game takes priority over FlashColor's (they don't stack); with "Standard",
  FlashColor's stays.

## How it works
The game's image chain (shaders `shader/sm5/posteffect/*.shcd`, read from the game files):

1. the scene, in "gamma 2" light, can go past white;
2. `ToneMapping.shcd`: a factor read from a curve according to brightness, applied to all three colors;
3. `Saturate.shcd`: **each color is cut at 1** (this is where the game's flames already turn yellow);
4. `ColorFilter_DarkBlend.shcd`: the zone's curve and color filter;
5. vignette, gamma (`ToneAdjust.shcd`), then the interface on top.

FlashColor intercepts the game function that activates shaders (`ImmediateContext.SetPixelShader`). When it is the tone
mapping (step 2), it activates its own shader instead: the same calculation as the game, then the settings, then the
softening of bright lights. Its settings go through a separate buffer (register b13), put back right after.

With neutral settings and softening off, the image is **identical** to the game's (checked by the tests).

With a GPose filter chosen, it also binds the filter's curve (t2 / s2) for its tone mapping, and when the game activates
its own color filter (step 4), it gives it a strength of 0 (buffer b2), put back right after.

### Hooking the game
To intercept the game's function, Dalamud needs a little free memory at a precise location; sometimes there is none for
a moment ("Unable to find memory location to fit MemoryBuffer" in `dalamud.log`, at random when the plugin reloads).
FlashColor says so in its window and tries again by itself, less and less often (2 s, up to 30 s), instead of stopping.

The window stays open in GPose, to adjust colors while taking pictures; hiding the game's interface hides it.

### After a game update
On load, FlashColor compares the fingerprint of `ToneMapping.shcd` with the one it was written for (`0x85E777EF`). If
the game has changed it, FlashColor stays off and says so in its window: it then needs adapting.

## Disclaimer
Like any third-party tool, using Dalamud and its plugins is against the FFXIV Terms of Service. Use it at your own
risk.

FlashColor is not affiliated with or endorsed by ReShade or the authors of the presets named above.

## Building and testing
- `dotnet test FlashColor.slnx`: the tests read the game's real `ToneMapping.shcd` and `ColorFilter_DarkBlend.shcd`
  (read only) and run the game's shaders and FlashColor's on the same pixels with WARP, Windows' software graphics card:
  - neutral settings: same image as the game;
  - a flame going past white: the game cuts it and changes its hue, FlashColor can keep its hue under white, or turn it
    yellow without making it pink;
  - the texture of a very bright flame survives the softening when details are kept;
  - strong settings: no color goes past white;
  - below where the softening starts: nothing changes;
  - same inputs as the game's shader (signature, slots);
  - the 24 GPose filters: same image as the game's real filter shader (`ColorFilter_DarkBlend.shcd`), within 0.00001,
    with color settings on top; at half strength, the image is halfway.

  Without the game installed, these tests fail, unless the environment variable `FLASHCOLOR_TESTS_WITHOUT_GAME=1` is set.
- `dotnet build FlashColor -c Release`: to try it, add `FlashColor\bin\Release\FlashColor.dll` to the dev plugin
  locations (`/xlsettings`, **Experimental** tab); Dalamud reloads it by itself, even with the game running. It does not
  load the version installed from the repository at the same time: disable that one.

## Releasing a new version
Pushing an annotated `vX.Y.Z` tag is all it takes: the GitHub workflow runs the tests, builds the plugin, creates the
release (its notes are the tag message) and updates `repo.json`.

```bash
git tag -a v1.0.0 --cleanup=verbatim -F notes.txt
git push origin v1.0.0
```

## Credits
- [Dalamud](https://github.com/goatcorp/Dalamud) (AGPL-3.0): plugin loading, access to the game.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) (MIT): game structures (`ImmediateContext`,
  `PixelShader`).
- [TerraFX.Interop.Windows](https://github.com/terrafx/terrafx.interop.windows) (MIT): access to DirectX 11, provided by
  Dalamud.
- [Lumina](https://github.com/NotAdam/Lumina) (MIT): reading the game files in the tests.
- [RenoDX](https://github.com/clshortfuse/renodx) (MIT): its FFXIV mod showed which game shaders form the tone mapping
  chain. No code is taken from it: the game's shaders were read directly.
- [Dear ImGui](https://github.com/ocornut/imgui) (MIT): the user interface library used by Dalamud.
- [xUnit](https://xunit.net/) for the tests.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. This plugin is not affiliated with or endorsed by Square Enix.
