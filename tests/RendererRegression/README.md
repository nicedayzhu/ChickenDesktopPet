# 模型切换渲染回归检查

需要 Windows、.NET 10 SDK 和项目自带资源包，无需本机 CS2。渲染检查还需可用的 OpenGL 显卡。使用生产渲染器在同一进程内连续切换宠物、羽色和静态外观；每次返回小鸡时，对比首次启动的亮度及黑色像素比例。检查失败或超时会返回非零退出码。

在仓库根目录运行：

```powershell
dotnet restore tests/RendererRegression/RendererRegression.csproj --configfile Pet3D/NuGet.Config
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --assets
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- research/switch-regression
```

每一步的真实透明渲染帧保存到指定目录，便于检查绒毛、光照和透明边缘。默认输出在 Git 忽略的 `research/` 下。测试不会修改用户桌宠设置。

`--assets` 不创建 OpenGL 窗口，检查资源包与逐文件 SHA256、VPK 校验、清单和实际 RERL 依赖闭包、宠物目录及动作图标。也验证默认路径相对于 EXE、自定义包覆盖、失效路径报错、旧游戏环境变量被忽略及加载器不发现游戏搜索路径。

## 旋转取景与快捷互动

```powershell
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --framing
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --toolbar
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --ui-live
```

`--framing` 连续旋转小鸡、普通鸡、波兰鸡、丝羽鸡、破壳蛋和完整蛋，并播放可用的待机、表演、喂食、睡觉和七种具名动作。每种一次性动作至少覆盖完整资源时长。逐帧检查外侧 4 像素是否存在不透明内容，保存末帧和首次失败帧，并检查目录中已排除烤鸡。该检查覆盖当前游戏资源，不能替代对未来新动画的目视检查。

`--framing` 同时断言每段动作播放期间相机距离和目标点恒定，旋转也不能引发自动缩放。

`--toolbar` 从自带 VPK 读取官方动作与图标，生成实时小鸡帧及新界面截图，验证动作分发、播放状态、睡觉/叫醒、蛋的破壳入口、命名、透明/实底照片及八个屏幕边缘位置。无需提前准备截图。

检视窗口还验证 Windows 原生标题拖动命中、客户区覆盖整个窗口（避免顶部系统横条）、模型与空白拖动区域区分，以及缩放方向和上下限。输出外观、摄影、820×620 小窗口和浅色背景截图供目视检查。

`--ui-live` 在生产桌宠窗口与实时渲染器之间检查完整流程：打开检视、命名、切换品种/羽色、缩放、返回小鸡、拍照、关闭并重开检视，以及本地名称持久化。截图和测试设置保存在 `research/panorama-live/`，不修改用户原有桌宠设置。
