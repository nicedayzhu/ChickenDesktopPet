# CS2 小鸡桌宠

[English](README.md) | 简体中文

把 CS2 小鸡养在 Windows 桌面上。程序通过 [ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)，实时渲染项目自带资源包中的模型、材质和动画，无需安装 CS2 或 Steam。

![检视与摄影棚](docs/assets/inspection-studio.png)

## 功能

- 默认是小鸡，也可选择普通鸡、波兰鸡、丝羽鸡及其羽色，或体验破壳蛋。完整蛋为静态外观。
- 支持喂食、表演、睡觉和叫醒；兼容动作包括坐下、惊慌、摇尾、跳舞、跳跃、踢腿和飞翔。
- 鼠标悬停出现互动栏，移开后自动收起；不支持的动作会禁用。
- 检视与摄影棚支持旋转、缩放、命名、切换外观和拍照。
- 精简右键菜单集中提供散步、置顶、省电模式、大小和旋转等桌面设置。
- 每次启动从自带资源包发现可用宠物及兼容动画；保存的外观被移除时，回退到默认小鸡。

## 开始使用

| 要求 | 说明 |
| --- | --- |
| 系统 | Windows 10/11 x64 |
| 运行库 | [.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0) |

1. 从 [GitHub Releases](https://github.com/nicedayzhu/ChickenDesktopPet/releases/latest) 下载 Windows x64 压缩包。
2. 完整解压后运行 `ChickenDesktopPet3D.exe`，保留 EXE 旁的 `Resources/` 文件夹。
3. 首次加载需要数秒。

升级时退出旧版本，替换 EXE 和 `Resources/` 文件夹。资源包约 69 MiB，可独立更新；程序不会搜索本机游戏安装。版本信息和更新记录统一放在 [GitHub Release 页面](https://github.com/nicedayzhu/ChickenDesktopPet/releases)。

## 桌面操作

| 操作 | 效果 |
| --- | --- |
| 单击 | 回应 |
| 双击 | 表演 |
| 中键 | 喂食 |
| 拖动 | 移动桌宠 |
| 滚轮 | 旋转视角 |
| 悬停约半秒 | 打开互动栏 |
| 右键桌宠或托盘图标 | 打开菜单 |

右键菜单将宠物选择、互动、照片、桌面设置和项目信息集中到子菜单。睡觉/叫醒按当前状态显示，设置开关用勾选表示；可在桌面设置中关闭悬停互动栏。

## 检视与摄影棚

从互动栏或右键菜单打开，也可以运行 `ChickenDesktopPet3D.exe --inspect` 直接进入。

- 拖动小鸡旋转，滚轮或放大镜按钮缩放；拖动标题或预览空白处移动窗口。Shift+拖动小鸡也可移动窗口，窗口边缘支持调整大小。
- 侧栏选择宠物和羽色。点击名称旁的铅笔编辑名称，Enter 保存、Esc 取消编辑；切换羽色保留名称。
- 底部动作栏提供日常互动，空格停止当前动作，Esc 回到桌面。
- 可选摄影棚、浅色、深色或透明背景。点击拍照或按 Ctrl+P 保存 512×512 PNG；照片库可打开原图或照片文件夹。

设置保存在 `%APPDATA%\ChickenDesktopPet\settings3d.json`；照片保存在系统“图片”目录下的 `CS2 鸡桌宠/`。

## 兼容性与当前范围

程序界面当前为中文。互动以所选模型支持的动画为准。名称和照片保存在本机，不连接 Steam 库存成长或照片服务。原生视线跟随、地图场景特效和头饰尚未加入。

## 文档

- [开发指南](docs/DEVELOPMENT.zh-CN.md)：构建、配置、渲染与历史 2D 实现。
- [资源包维护](docs/ASSETS.md)：资源结构、提取更新、清单校验和发布。
- [渲染回归检查](tests/RendererRegression/README.md)。
- [CS2 Panorama 宠物 UI 调研](docs/official-pet-ui-research.md)。

## 作者与许可

作者：[niceday_zhu](https://github.com/nicedayzhu)。渲染基于 [Source 2 Viewer / ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat)。

项目源代码按 [MIT 许可证](LICENSE) 开源。第三方组件及游戏内容遵循各自的条款，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。自带的 CS2 模型、材质、动画和图标属于 Valve，不受本项目 MIT 许可证授权。
