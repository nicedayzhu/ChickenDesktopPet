# 模型切换渲染回归检查

需要 Windows、.NET 10 SDK、可用的 OpenGL 显卡与本机 CS2。使用生产渲染器在同一进程内连续切换宠物、羽色和静态外观；每次返回小鸡时，对比首次启动的亮度及黑色像素比例。检查失败或超时会返回非零退出码。

在仓库根目录运行：

```powershell
dotnet restore tests/RendererRegression/RendererRegression.csproj --configfile Pet3D/NuGet.Config
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- research/switch-regression
```

每一步的真实透明渲染帧保存到指定目录，便于检查绒毛、光照和透明边缘。默认输出在 Git 忽略的 `research/` 下。测试不会修改用户桌宠设置。

## 旋转取景与快捷互动

```powershell
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --framing
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --toolbar
```

`--framing` 连续旋转小鸡、普通鸡、波兰鸡、丝羽鸡、破壳蛋和完整蛋，并播放可用的待机、表演、喂食和睡觉动作。逐帧检查外侧 4 像素是否存在不透明内容，保存每个动作的末帧及首次失败帧，并检查目录中已排除烤鸡。该检查覆盖当前游戏资源，不能替代对未来新动画的目视检查。

`--framing` 同时断言每段动作播放期间相机距离和目标点恒定，旋转也不能引发自动缩放。

`--toolbar` 渲染互动栏预览，验证按钮动作分发，以及蛋/静态外观不显示不可用动作。
