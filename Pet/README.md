# 2D 桌宠原型（历史版本）

此版本使用 WPF 播放从 CS2 小鸡模型烘焙出的透明 PNG 帧。源码、图标和动作清单保留在仓库中，用于回顾早期实现。帧图位于本机 `Assets/Sprites/`，属于生成资源，不纳入 Git。

要运行此版本，需自行准备已安装的 CS2、提取模型资源，并使用 `scripts/export_model.ps1` 和 `scripts/render_sprites.py` 生成帧图。当前推荐使用 `Pet3D/` 中直接读取本机 CS2 资源的实时 3D 版本。
