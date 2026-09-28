# Development Guide

[Project overview](../README.md) · English | [简体中文](DEVELOPMENT.zh-CN.md)

## Repository Layout

| Path | Purpose |
| --- | --- |
| `Pet3D/` | Current Windows 3D desktop pet |
| `Pet/` | Earlier 2D prototype |
| `scripts/` | Release build and resource extraction/rendering scripts |
| `tests/RendererRegression/` | Integration checks using the local CS2 installation and GPU |
| `docs/official-pet-ui-research.md` | Official UI references and desktop adaptation notes |
| `dist3d/` | Generated release output, ignored by Git |

## Build

Use Windows and the .NET 10 SDK selected by [global.json](../global.json), which requests `10.0.102` with `latestPatch` roll-forward. The [official .NET downloads](https://dotnet.microsoft.com/download/dotnet/10.0) include SDKs and Desktop Runtimes. Running the application also requires a local CS2 installation and a GPU compatible with the VRF OpenGL renderer.

Exit a running pet from the release directory before building. From the repository root:

```powershell
New-Item -ItemType Directory -Force .nuget/feed | Out-Null
./scripts/build_release.ps1
```

[build_release.ps1](../scripts/build_release.ps1) restores and publishes `Pet3D/ChickenDesktopPet3D.csproj` for Windows x64, then verifies that `dist3d/` contains only `ChickenDesktopPet3D.exe`. The EXE uses a separately installed .NET Desktop Runtime.

[NuGet.Config](../Pet3D/NuGet.Config) declares the local `.nuget/feed/` source and nuget.org. Creating the empty local source allows restoration from a clean checkout; for offline builds, populate it with the required packages. The build script uses `.nuget/packages/` as its package cache. Both locations are ignored by Git. Exact dependency versions are listed in the [project manifest](../Pet3D/ChickenDesktopPet3D.csproj).

The tray icon, environment lighting texture, and license texts are embedded in the EXE. At first launch, .NET extracts bundled native libraries to a user temporary location. Game models, materials, animations, and official UI icons are read from the installed CS2 VPK.

Release notes are maintained on [GitHub Releases](https://github.com/nicedayzhu/ChickenDesktopPet/releases).

## Configuration and Local Data

| Item | Location or behavior |
| --- | --- |
| Game discovery | Locates the Steam installation for app ID `730` |
| `CHICK_CS2_VPK` | Overrides discovery with an absolute path to `pak01_dir.vpk` |
| Desktop settings | `%APPDATA%\ChickenDesktopPet\settings3d.json` |
| Photos | `CS2 鸡桌宠/` under the Windows Pictures folder |
| `--inspect` | Opens the inspection studio on startup |

Desktop settings store position, size, appearance, camera angle, pet names, roaming, power saving, staying on top, and hover interactions. Use the application controls to change them. Names and photos are local data; they do not update Steam inventory or online photo services.

## Rendering and Animation

The renderer uses 512×512 internal frames, 4× MSAA, and the model's original materials and lighting. MSAA sample coverage determines output transparency, preserving feather detail and avoiding gaps that expose the background through the body. Colors are constrained for premultiplied alpha before the image is handed to WPF, avoiding rectangular halos on dark backgrounds.

The inspection window and desktop pet share the same renderer and frames. Inspection hides the desktop pet and pauses roaming; closing it restores desktop placement, view, and interactions. Minimizing inspection shows the desktop pet again.

Animation framing is calculated when loading the model. Everyday actions share a fixed camera; feeding, performances, and hatching select a safe framing before playback begins. Camera distance and target stay fixed during playback and rotation. Larger actions may use a more distant view. The duration of actions that play once comes from the animation resource.

The targets are about 30 FPS while idle and 60 FPS during interaction or inspection. An earlier local measurement of the desktop mode reported a working set of about 420–440 MB. Actual performance varies with hardware, mode, and animation.

## Compatibility and Validation

Models, appearances, compatible animations, official SVG icons, action data, and animation graphs are discovered from the local VPK at startup. If a saved appearance no longer exists, the app falls back to the default chick. Menus disable actions unavailable for the selected model.

Use the [renderer regression guide](../tests/RendererRegression/README.md) (Chinese) for model switching and lighting checks, framing throughout entire animations, hover controls, inspection, and local photo flows. These checks require Windows, the SDK, local CS2 resources, and a working OpenGL context. Test captures and isolated settings are stored under the ignored `research/` directory. They do not replace visual checks of newly added game assets.

Official inspection and interaction references, action mappings, and the desktop adaptation are documented in the [Panorama UI research](official-pet-ui-research.md) (Chinese).

## Earlier 2D Implementation

The solution includes both `Pet3D/` and the earlier `Pet/` project. The 2D prototype plays baked transparent PNG frames. Its source and preparation scripts are preserved, while extracted game assets and generated frames are excluded from Git.

Running it requires preparing `Pet/Assets/Sprites/` using `scripts/export_model.ps1` and `scripts/render_sprites.py`. See the [2D prototype notes](../Pet/README.md) (Chinese). The current product is the 3D implementation.
