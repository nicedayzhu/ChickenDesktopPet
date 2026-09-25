# CS2 小鸡桌宠

使用 [ValveResourceFormat.Renderer](https://github.com/ValveResourceFormat/ValveResourceFormat) 实时渲染本机 Counter-Strike 2 的小鸡模型、材质和动画。程序不打包 Valve 游戏资源。当前产品是 Windows 实时 3D 桌宠；早期 2D 图片版源码保留在 `Pet/` 供回顾。

## 运行

从本机发布目录 `dist3d/` 启动 `ChickenDesktopPet3D.exe`。发布目录只有这一个文件，可以直接复制给另一台符合要求的电脑。需要 Windows 10/11 x64、已安装的 CS2 和 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。首次加载约需数秒。若自动定位游戏失败，将 `CHICK_CS2_VPK` 设为 `pak01_dir.vpk` 的绝对路径。

- 单击回应，双击表演，中键喂食，拖动移动，滚轮旋转视角。
- 鼠标停留约半秒出现互动栏，可直接喂食、表演、睡觉或叫醒；移开后自动收起。互动栏只显示当前宠物支持的动作，可在右键菜单中关闭。
- 右键小鸡或托盘图标可选择宠物及羽色、喂食、睡觉、散步、调整大小、置顶和启用省电模式。
- 默认是小鸡；当前 CS2 版本还可选择普通鸡、波兰鸡、丝羽鸡及各自的羽色。破壳蛋可播放破壳动画，完整蛋为静态外观；菜单会按当前外观禁用不可用的互动。烤鸡不再列入宠物列表。
- 外观和动作列表在每次启动时从本机 CS2 VPK 中发现。游戏更新后新增的同目录模型、羽色和兼容动画无需重新打包游戏资源。若已选外观被游戏移除，启动时回退到默认小鸡。
- 设置保存在 `%APPDATA%\ChickenDesktopPet\settings3d.json`。

桌宠使用 512 像素内部渲染、4× MSAA 和原版光照。GPU 使用 MSAA 实际覆盖率输出透明度，保留羽毛细节并避免身体内部透出背景；透明帧交给 WPF 前按预乘透明度约束颜色，避免暗色背景上出现矩形光晕。本机实测静置约 30 FPS、互动目标约 60 FPS，工作集约 420–440 MB；帧率和占用随设备、窗口尺寸及动作而变化。

取景随动画姿态和旋转角度调整，为跳跃、展翅及破壳动作保留边缘空间，动作结束后缓慢恢复。动作按资源自身时长完成，不再用固定秒数提前截断。取景只缓存骨骼的几何范围，保持原有内部渲染分辨率。

## 构建与发布

安装 .NET 10 SDK，在项目根目录运行：

```powershell
./scripts/build_release.ps1
```

脚本构建 `Pet3D/ChickenDesktopPet3D.csproj`，将 Windows x64 单文件程序发布到 `dist3d/`，并检查该目录只含一个 EXE。程序所需的托盘图标、环境光照贴图及 ValveResourceFormat 许可文本已嵌入 EXE；首次运行时 .NET 会在用户临时目录提取原生运行库。本地 `.nuget/`、`bin/`、`obj/` 和 `dist3d/` 均由 Git 忽略。`Pet3D/NuGet.Config` 同时配置了本地缓存和 nuget.org。

Visual Studio 解决方案同时包含 `Pet3D/` 和历史 2D 项目 `Pet/`。2D 版播放烘焙帧图；源码和制作脚本保留，但帧图以及提取出的 CS2 模型、贴图不纳入 Git。若要运行 2D 版，需自行准备 `Pet/Assets/Sprites/`；相关提取与渲染脚本在 `scripts/`。

## 作者与许可

作者：[niceday_zhu](https://github.com/nicedayzhu)。本项目源代码按 [MIT 许可证](LICENSE) 开源。所用组件及游戏资源的权利说明见 [第三方声明](THIRD_PARTY_NOTICES.md)；右键菜单可在程序内查看许可原文。CS2 模型、材质和动画版权属于 Valve，请勿将提取出的游戏资源放进 Git 仓库或发布包。
