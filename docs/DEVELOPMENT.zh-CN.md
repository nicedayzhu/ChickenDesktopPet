# 开发指南

[项目概览](../README.zh-CN.md) · [English](DEVELOPMENT.md) | 简体中文

## 仓库结构

| 路径 | 用途 |
| --- | --- |
| `Pet3D/` | 当前 Windows 3D 桌宠 |
| `Pet/` | 早期 2D 原型 |
| `scripts/` | 发布构建、资源提取和渲染脚本 |
| `Pet3D/Assets/Resources/` | 随项目保存的精简资源 VPK 和哈希清单 |
| `scripts/AssetBundle/` | 独立的资源依赖提取工具 |
| `tests/RendererRegression/` | 资源完整性及 GPU 渲染集成检查 |
| `docs/official-pet-ui-research.md` | 官方 UI 参考与桌面适配调研 |
| `dist3d/` | 生成的发布目录，由 Git 忽略 |

## 构建

使用 Windows 和 [global.json](../global.json) 指定的 .NET 10 SDK：版本 `10.0.102`，允许 `latestPatch` 滚动。[官方 .NET 下载页面](https://dotnet.microsoft.com/download/dotnet/10.0) 提供 SDK 与 Desktop Runtime。运行程序需要兼容 VRF OpenGL 渲染器的显卡，无需 CS2 或 Steam。

构建前退出发布目录中正在运行的桌宠，在仓库根目录执行：

```powershell
New-Item -ItemType Directory -Force .nuget/feed | Out-Null
./scripts/build_release.ps1
```

[build_release.ps1](../scripts/build_release.ps1) 校验资源包 SHA256，还原并发布 `Pet3D/ChickenDesktopPet3D.csproj`，目标为 Windows x64。输出为 `dist3d/ChickenDesktopPet3D.exe` 和 `dist3d/Resources/`，并在 `dist/releases/v<版本>/` 生成包含二者的 `ChickenDesktopPet3D-v<版本>-win-x64.zip` 和 `SHA256SUMS.txt`。EXE 是依赖框架的单文件程序，需要单独安装 .NET Desktop Runtime。

已有桌宠正在运行时，可用 `./scripts/build_release.ps1 -OutputDirectory ./dist3d-v1.2.0` 构建到独立目录，保留正在运行的版本。脚本仍会检查所选输出目录是否被运行中的桌宠占用。

[NuGet.Config](../Pet3D/NuGet.Config) 配置了本地 `.nuget/feed/` 源及 nuget.org。新克隆先创建空的本地源目录，即可进行还原；离线构建需自行填充所需包。构建脚本使用 `.nuget/packages/` 作为包缓存，两者均由 Git 忽略。具体依赖版本见 [项目文件](../Pet3D/ChickenDesktopPet3D.csproj)。

托盘图标、环境光照贴图和许可文本已嵌入 EXE；首次启动时 .NET 会将打包的原生库提取到用户临时目录。模型、材质、动画和官方 UI 数据从自带的 `Resources/pet_assets.vpk` 读取。资源更新仅需开发机安装游戏，参见[资源包维护](ASSETS.md)。

发布说明统一维护在 [GitHub Releases](https://github.com/nicedayzhu/ChickenDesktopPet/releases)。

## 版本与发布

以 `Pet3D/ChickenDesktopPet3D.csproj` 的 `<Version>` 为唯一程序版本来源，采用 `MAJOR.MINOR.PATCH`：功能新增递增 MINOR，兼容修复递增 PATCH，不兼容的公开接口或设置格式变化递增 MAJOR。资源清单的 `schemaVersion` 表示清单格式，与程序版本分别管理；具体资源快照由游戏版本及 SHA256 标识。

发布时先修改项目版本并提交，构建和验证这个提交，再创建指向该提交的注释标签 `v<版本>`，推送分支与标签，并将构建脚本生成的版本 ZIP 和 `SHA256SUMS.txt` 上传至同名 GitHub Release。脚本会核对 EXE 内部版本；已有正式标签和发布附件不覆盖。

## 配置与本地数据

| 项目 | 位置或行为 |
| --- | --- |
| 默认资源 | EXE 旁的 `Resources/pet_assets.vpk`，路径与当前工作目录无关 |
| `CHICK_PET_VPK` | 覆盖资源包路径，供开发测试；路径无效时明确报错 |
| `CHICK_CS2_VPK` | 仅资源更新脚本读取，运行程序不使用 |
| 桌面设置 | `%APPDATA%\ChickenDesktopPet\settings3d.json` |
| 照片 | 系统“图片”目录下的 `CS2 鸡桌宠/` |
| `--inspect` | 启动时直接打开检视摄影棚 |

桌面设置保存位置、大小、外观、视角、宠物名称、散步、省电、置顶和悬停互动栏等配置，通过程序界面修改即可。名称和照片属于本地数据，不更新 Steam 库存或线上照片服务。

## 渲染与动画

渲染器使用 512 像素内部帧图、4× MSAA，以及模型原版材质和光照。MSAA 实际采样覆盖率决定输出透明度，保留羽毛细节，并避免身体内部出现透出背景的缝隙。帧图交给 WPF 前按预乘透明度约束颜色，避免暗色背景上的矩形光晕。

检视窗口与桌宠复用同一渲染器和帧图。检视时隐藏桌面小鸡并暂停散步，关闭后恢复位置、视角和互动；最小化检视窗口会重新显示桌宠。

加载模型时计算动画取景。日常动作共用固定镜头；喂食、表演和破壳在播放前选择安全取景范围，播放及旋转时保持相机距离和目标点恒定。大幅动作可能采用更远的镜头，一次性动作按资源自身时长完成。

目标为静置约 30 FPS、互动与检视约 60 FPS；此前桌面模式的本机工作集实测约 420–440 MB。实际性能随硬件、模式和动画变化。

## 兼容性与验证

每次启动从项目资源 VPK 发现模型、羽色、兼容动画、官方 SVG 图标、动作数据和动画图。VRF 文件加载器仅搜索这个包，不加载本机 `gameinfo.gi` 或其他游戏 VPK。保存的外观不再存在时回退到默认小鸡；菜单按当前模型禁用不可用动作。

[渲染回归指南](../tests/RendererRegression/README.md) 提供资源哈希与依赖闭包校验、模型切换与光照、整段动画取景、悬停互动、检视和本地拍照流程检查。检查需要 Windows、SDK 和项目自带资源；渲染检查还需要可用的 OpenGL 上下文。截图与隔离设置保存在 Git 忽略的 `research/` 下。新加入的资源仍需目视检查。

官方检视与互动参考、动作映射和桌面适配方案见 [Panorama UI 调研](official-pet-ui-research.md)。

## 历史 2D 实现

解决方案同时包含 `Pet3D/` 和早期 `Pet/` 项目。2D 原型播放烘焙的透明 PNG 帧；源码和制作脚本保留，提取出的游戏资源及生成帧图不纳入 Git。

运行 2D 版需使用 `scripts/export_model.ps1` 和 `scripts/render_sprites.py` 准备 `Pet/Assets/Sprites/`，详见 [2D 原型说明](../Pet/README.md)。当前产品为 3D 实现。
