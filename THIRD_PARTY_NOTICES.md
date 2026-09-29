# Third-party notices / 第三方声明

CS2 鸡桌宠由 niceday_zhu 编写，项目源代码按根目录 `LICENSE` 中的 MIT 条款授权。程序是非官方的独立作品，与 Valve 无关联。

程序使用随项目及发布包保存的精简 Counter-Strike 2 资源快照，运行时无需用户安装游戏。资源位于 `Pet3D/Assets/Resources/pet_assets.vpk`，发布后位于 EXE 旁的 `Resources/`；对应清单记录源游戏版本、文件列表、依赖和 SHA256。Counter-Strike 2 的模型、材质、动画、图标和相关游戏内容的权利属于 Valve。项目的 MIT 许可证不授予游戏资源的使用或再分发许可。

单文件程序使用以下第三方软件：

| 组件 | 用途 | 许可与来源 |
| --- | --- | --- |
| ValveResourceFormat / Source 2 Viewer | Source 2 资源解析与渲染 | MIT；原文见内置 `ValveResourceFormatLicense`，<https://github.com/ValveResourceFormat/ValveResourceFormat> |
| OpenTK | OpenGL 窗口及图形接口 | MIT，<https://github.com/opentk/opentk> |
| SkiaSharp | VRF 的图像处理依赖 | MIT，<https://github.com/mono/SkiaSharp> |
| ValvePak | VPK 读取 | MIT，<https://github.com/ValveResourceFormat/ValvePak> |
| ValveKeyValue | Valve KeyValues 读取 | MIT，<https://github.com/ValveResourceFormat/ValveKeyValue> |
| Blake3 | 哈希 | BSD-2-Clause，<https://github.com/xoofx/Blake3.NET> |

内置环境光照贴图 `industrial_sunset_puresky.vtex_c` 随 ValveResourceFormat 的查看器资源提供，其使用遵循该项目的许可。其他传递依赖的许可与版权信息以对应 NuGet 包及其上游仓库为准。
