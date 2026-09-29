# Development Guide

[Project overview](../README.md) · English | [简体中文](DEVELOPMENT.zh-CN.md)

## Repository Layout

| Path | Purpose |
| --- | --- |
| `Pet3D/` | Current Windows 3D desktop pet |
| `Pet/` | Earlier 2D prototype |
| `scripts/` | Release build and resource extraction/rendering scripts |
| `Pet3D/Assets/Resources/` | Versioned compact VPK and hash manifest |
| `scripts/AssetBundle/` | Independent dependency extraction tool |
| `tests/RendererRegression/` | Asset integrity and GPU integration checks |
| `docs/official-pet-ui-research.md` | Official UI references and desktop adaptation notes |
| `dist3d/` | Generated release output, ignored by Git |

## Build

Use Windows and the .NET 10 SDK selected by [global.json](../global.json), which requests `10.0.102` with `latestPatch` roll-forward. The [official .NET downloads](https://dotnet.microsoft.com/download/dotnet/10.0) include SDKs and Desktop Runtimes. Running the application requires a GPU compatible with the VRF OpenGL renderer, with no CS2 or Steam installation.

Exit a running pet from the release directory before building. From the repository root:

```powershell
New-Item -ItemType Directory -Force .nuget/feed | Out-Null
./scripts/build_release.ps1
```

[build_release.ps1](../scripts/build_release.ps1) verifies the resource bundle's SHA256, restores and publishes `Pet3D/ChickenDesktopPet3D.csproj` for Windows x64, and verifies `dist3d/ChickenDesktopPet3D.exe` plus `dist3d/Resources/`. It creates `ChickenDesktopPet3D-v<version>-win-x64.zip` containing both, plus `SHA256SUMS.txt`, under `dist/releases/v<version>/`. The EXE uses a separately installed .NET Desktop Runtime.

To keep an existing pet running, build to a separate directory with `./scripts/build_release.ps1 -OutputDirectory ./dist3d-v1.2.0`. The script checks whether the selected output directory is in use by a running pet.

[NuGet.Config](../Pet3D/NuGet.Config) declares the local `.nuget/feed/` source and nuget.org. Creating the empty local source allows restoration from a clean checkout; for offline builds, populate it with the required packages. The build script uses `.nuget/packages/` as its package cache. Both locations are ignored by Git. Exact dependency versions are listed in the [project manifest](../Pet3D/ChickenDesktopPet3D.csproj).

The tray icon, environment lighting texture, and license texts are embedded in the EXE. At first launch, .NET extracts bundled native libraries to a user temporary location. Models, materials, animations, and official UI data are read from the bundled `Resources/pet_assets.vpk`. Only asset maintenance requires a game installation; see [asset maintenance](ASSETS.md) (Chinese).

Release notes are maintained on [GitHub Releases](https://github.com/nicedayzhu/ChickenDesktopPet/releases).

## Versioning and Releases

The `<Version>` in `Pet3D/ChickenDesktopPet3D.csproj` is the single source of truth for the application version. Use `MAJOR.MINOR.PATCH`: increment MINOR for features, PATCH for compatible fixes, and MAJOR for incompatible changes to public interfaces or settings formats. The resource manifest's `schemaVersion` tracks its format independently; the game build and SHA256 identify the asset snapshot.

Update and commit the project version, build and verify that commit, create an annotated `v<version>` tag pointing to it, push the branch and tag, and upload the generated versioned ZIP and `SHA256SUMS.txt` to the matching GitHub Release. The build script checks the EXE's internal version. Do not overwrite published tags or release assets.

## Configuration and Local Data

| Item | Location or behavior |
| --- | --- |
| Default resources | `Resources/pet_assets.vpk` next to the EXE, independent of the working directory |
| `CHICK_PET_VPK` | Overrides the bundle path for development; invalid paths fail explicitly |
| `CHICK_CS2_VPK` | Used only by the maintenance script, ignored by the runtime |
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

Models, appearances, compatible animations, official SVG icons, action data, and animation graphs are discovered from the project VPK at startup. The VRF file loader searches only this package, without loading local gameinfo or other game VPKs. If a saved appearance no longer exists, the app falls back to the default chick. Menus disable actions unavailable for the selected model.

Use the [renderer regression guide](../tests/RendererRegression/README.md) (Chinese) for hash and dependency closure verification, model switching and lighting checks, framing throughout entire animations, hover controls, inspection, and local photo flows. These checks require Windows, the SDK, and bundled resources; rendering also requires an OpenGL context. Test captures and isolated settings are stored under the ignored `research/` directory. They do not replace visual checks of newly added assets.

Official inspection and interaction references, action mappings, and the desktop adaptation are documented in the [Panorama UI research](official-pet-ui-research.md) (Chinese).

## Earlier 2D Implementation

The solution includes both `Pet3D/` and the earlier `Pet/` project. The 2D prototype plays baked transparent PNG frames. Its source and preparation scripts are preserved, while extracted game assets and generated frames are excluded from Git.

Running it requires preparing `Pet/Assets/Sprites/` using `scripts/export_model.ps1` and `scripts/render_sprites.py`. See the [2D prototype notes](../Pet/README.md) (Chinese). The current product is the 3D implementation.
