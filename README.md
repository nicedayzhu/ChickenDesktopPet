# CS2 小鸡桌宠

使用 [ValveResourceFormat.Renderer](https://github.com/ValveResourceFormat/ValveResourceFormat) 实时渲染本机 Counter-Strike 2 的小鸡模型、材质和动画。程序不打包 Valve 游戏资源。当前产品是 Windows 实时 3D 桌宠；早期 2D 图片版源码保留在 `Pet/` 供回顾。

## 运行

从本机发布目录 `dist3d/` 启动 `ChickenDesktopPet3D.exe`。需要 Windows 10/11 x64、已安装的 CS2 和 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。首次加载约需数秒。若自动定位游戏失败，将 `CHICK_CS2_VPK` 设为 `pak01_dir.vpk` 的绝对路径。

- 单击回应，双击表演，拖动移动，滚轮旋转视角。
- 右键小鸡或托盘图标可喂食、睡觉、散步、调整大小、置顶和启用省电模式。
- 设置保存在 `%APPDATA%\ChickenDesktopPet\settings3d.json`。

桌宠使用 512 像素内部渲染、4× MSAA 和原版光照。透明帧在交给 WPF 前按预乘透明度约束颜色，避免暗色背景上出现矩形光晕。当前实现静置约 30 FPS，互动目标约 60 FPS；本机实测工作集约 390 MB。帧率和占用随设备、窗口尺寸及动作而变化。

## 构建与发布

安装 .NET 10 SDK，在项目根目录运行：

```powershell
./scripts/build_release.ps1
```

脚本构建 `Pet3D/ChickenDesktopPet3D.csproj`，将 Windows x64 程序发布到 `dist3d/`，并生成 `ChickenDesktopPet3D-win-x64.zip`。本地 `.nuget/`、`bin/`、`obj/`、`dist3d/` 和压缩包均由 Git 忽略。`Pet3D/NuGet.Config` 同时配置了本地缓存和 nuget.org。

Visual Studio 解决方案同时包含 `Pet3D/` 和历史 2D 项目 `Pet/`。2D 版播放烘焙帧图；源码和制作脚本保留，但帧图以及提取出的 CS2 模型、贴图不纳入 Git。若要运行 2D 版，需自行准备 `Pet/Assets/Sprites/`；相关提取与渲染脚本在 `scripts/`。

ValveResourceFormat 代码遵循 MIT 许可证，见 `Pet3D/LICENSE-ValveResourceFormat.txt`。CS2 模型、材质和动画版权属于 Valve，请勿单独分发从游戏中提取的资源。