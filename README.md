# CS2 小鸡桌宠

使用 [ValveResourceFormat.Renderer](https://github.com/ValveResourceFormat/ValveResourceFormat) 实时渲染本机 Counter-Strike 2 的小鸡模型、材质和动画。程序不打包 Valve 游戏资源。当前产品是 Windows 实时 3D 桌宠；早期 2D 图片版源码保留在 `Pet/` 供回顾。

## 运行

从 [GitHub Releases](https://github.com/nicedayzhu/ChickenDesktopPet/releases/latest) 下载 Windows x64 压缩包，解压后启动 `ChickenDesktopPet3D.exe`。当前版本为 **v1.1.0**，更新内容见 [GitHub Release 页面](https://github.com/nicedayzhu/ChickenDesktopPet/releases/tag/v1.1.0)。本机构建输出位于 `dist3d/`，发布目录只有这一个文件，可以直接复制给另一台符合要求的电脑。需要 Windows 10/11 x64、已安装的 CS2 和 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。首次加载约需数秒。若自动定位游戏失败，将 `CHICK_CS2_VPK` 设为 `pak01_dir.vpk` 的绝对路径。

- 单击回应，双击表演，中键喂食，拖动移动，滚轮旋转视角。
- 鼠标停留约半秒出现官方风格互动栏：喂食、动作、睡觉/叫醒、检视、拍照。展开“动作”可选择坐下、惊慌、摇尾、跳舞、跳跃、踢腿、飞翔；不兼容的入口会禁用并提供提示。移开后自动收起，可在右键菜单中关闭互动栏。
- 右键小鸡或托盘图标打开精简菜单：检视、选择宠物、互动、照片、桌面设置、关于、退出。互动菜单按状态显示睡觉或叫醒；桌面设置集中提供散步、省电、置顶、悬停互动栏、大小和旋转，开关用勾选状态表示。作者、组件来源和许可放在“关于”中。
- 默认是小鸡；当前 CS2 版本还可选择普通鸡、波兰鸡、丝羽鸡及各自的羽色。破壳蛋可播放破壳动画，完整蛋为静态外观；菜单会按当前外观禁用不可用的互动。烤鸡不再列入宠物列表。
- 外观和动作列表在每次启动时从本机 CS2 VPK 中发现。游戏更新后新增的同目录模型、羽色和兼容动画无需重新打包游戏资源。若已选外观被游戏移除，启动时回退到默认小鸡。
- 设置保存在 `%APPDATA%\ChickenDesktopPet\settings3d.json`。

## 检视与摄影棚

从互动栏或右键菜单打开“检视与摄影棚”。也可以运行 `ChickenDesktopPet3D.exe --inspect` 直接打开。窗口支持：

- 拖动标题或预览空白处移动窗口，拖动小鸡旋转；Shift+拖动小鸡也可移动窗口。滚轮或放大镜加减按钮拉近/拉远，边缘支持调整窗口大小。
- 右侧“外观”选择宠物和羽色；点击名称旁的铅笔编辑本地名称，Enter 保存，Esc 取消编辑。切换羽色保留名称。
- 底部集中放置日常互动和七种具名动作；空格停止当前动作，Esc 回到桌面。
- 右侧“摄影”选择摄影棚、浅色、深色或透明背景，点击拍照或 Ctrl+P 保存 512×512 PNG；照片库可打开原图或照片文件夹。

照片默认保存在系统“图片”目录下的 `CS2 鸡桌宠/`。名称和照片属于本地桌宠数据。此版本不连接 Steam 库存成长或照片服务。

检视窗口与桌宠复用同一个渲染器和帧图。检视时隐藏桌面小鸡并暂停散步，关闭后恢复位置、视角和桌面互动；最小化检视窗口会重新显示桌宠。官方 SVG 图标、动作数据和动画图在启动时从本机 VPK 读取，发布包不包含这些提取资源。

桌宠使用 512 像素内部渲染、4× MSAA 和原版光照。GPU 使用 MSAA 实际覆盖率输出透明度，保留羽毛细节并避免身体内部透出背景；透明帧交给 WPF 前按预乘透明度约束颜色，避免暗色背景上出现矩形光晕。静置目标约 30 FPS、互动与检视目标约 60 FPS；此前桌面模式的本机工作集实测约 420–440 MB。帧率和占用随设备、模式及动作而变化。

加载模型时预先计算整段动画的安全取景范围。日常动作共用固定镜头，喂食、表演及破壳动作在开始时切换到预先算好的取景；播放和旋转过程中镜头距离、目标点保持不变，避免宠物反复变大变小。大幅动作可能使用更远的镜头。动作按资源自身时长完成，不再用固定秒数提前截断；保持原有内部渲染分辨率。

## 构建与发布

安装 .NET 10 SDK，在项目根目录运行：

```powershell
./scripts/build_release.ps1
```

脚本构建 `Pet3D/ChickenDesktopPet3D.csproj`，将 Windows x64 单文件程序发布到 `dist3d/`，并检查该目录只含一个 EXE。程序所需的托盘图标、环境光照贴图及 ValveResourceFormat 许可文本已嵌入 EXE；首次运行时 .NET 会在用户临时目录提取原生运行库。本地 `.nuget/`、`bin/`、`obj/` 和 `dist3d/` 均由 Git 忽略。`Pet3D/NuGet.Config` 同时配置了本地缓存和 nuget.org。

Visual Studio 解决方案同时包含 `Pet3D/` 和历史 2D 项目 `Pet/`。2D 版播放烘焙帧图；源码和制作脚本保留，但帧图以及提取出的 CS2 模型、贴图不纳入 Git。若要运行 2D 版，需自行准备 `Pet/Assets/Sprites/`；相关提取与渲染脚本在 `scripts/`。

## 界面设计参考

官方宠物悬停工具条、检视和摄影棚的源码位置、动作映射与桌面适配方案见 [Panorama UI 调研](docs/official-pet-ui-research.md)。首版已经实现官方风格互动栏、命名动作、本地检视与摄影棚；原生视线跟随、地图场景特效和头饰尚未加入。

## 作者与许可

作者：[niceday_zhu](https://github.com/nicedayzhu)。本项目源代码按 [MIT 许可证](LICENSE) 开源。所用组件及游戏资源的权利说明见 [第三方声明](THIRD_PARTY_NOTICES.md)；右键菜单可在程序内查看许可原文。CS2 模型、材质和动画版权属于 Valve，请勿将提取出的游戏资源放进 Git 仓库或发布包。
