# 模型切换渲染回归检查

需要 Windows、.NET 10 SDK、可用的 OpenGL 显卡与本机 CS2。使用生产渲染器在同一进程内连续切换宠物、羽色和静态外观；每次返回小鸡时，对比首次启动的亮度及黑色像素比例。检查失败或超时会返回非零退出码。

在仓库根目录运行：

```powershell
dotnet restore tests/RendererRegression/RendererRegression.csproj --configfile Pet3D/NuGet.Config
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- research/switch-regression
```

每一步的真实透明渲染帧保存到指定目录，便于检查绒毛、光照和透明边缘。默认输出在 Git 忽略的 `research/` 下。测试不会修改用户桌宠设置。
