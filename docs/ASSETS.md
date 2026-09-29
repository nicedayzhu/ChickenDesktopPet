# 自带资源包维护

当前 3D 桌宠默认读取 EXE 旁的 `Resources/pet_assets.vpk`，不定位 Steam，不依赖本机 CS2。仓库保存资源快照后，开发者可以直接构建、测试和发布；游戏安装只用于维护时提取新的快照。

## 文件与发布结构

源码资源位于 `Pet3D/Assets/Resources/`，不受根目录 `/assets/` 的忽略规则影响：

```text
Pet3D/Assets/Resources/
  pet_assets.vpk     # 单个 VPK，包含所有编译资源及其依赖
  manifest.json      # 来源版本、包和逐文件 SHA256、根文件及依赖
```

构建与发布自动复制为：

```text
ChickenDesktopPet3D.exe
Resources/
  pet_assets.vpk
  manifest.json
```

完整解压即可运行。移动、升级或打包时保留这个目录结构。资源包使用原有 Source 2 路径，VRF 仍直接解析编译资源并实时渲染，保留绒毛材质、原版动画、羽色与官方动作数据。

## 更新资源

在安装了 CS2 的维护机器上，从仓库根目录运行：

```powershell
./scripts/update_pet_assets.ps1 -Cs2Vpk 'F:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk'
```

也可设置 `CHICK_CS2_VPK` 后省略参数。`-OutputDirectory` 可输出到另一个目录，先验证候选快照。工具只读取游戏文件，将结果写入项目；无需启动游戏。独立工具 `scripts/AssetBundle/` 不依赖桌宠项目，资源缺失时也能重新生成。

选择规则维护在 `scripts/AssetBundle/Program.cs` 的 `IsRoot`：

- `models/chicken/` 中可作为桌宠的模型，排除烤鸡、独立羽毛和动画集模型。
- 鸡和蛋的 `vnmclip_c` 动画，以及鸡的动作图。
- `pet_photo_tag` 动作数据和宠物 SVG 图标、摄影/缩放/编辑图标。
- 渲染器按固定路径请求的 BRDF 查找表和蓝噪声纹理。
- 沿 RERL 外部引用递归收集网格、所有羽色材质、纹理、动画骨架及其他编译依赖，保持原路径。

维护工具允许通过游戏搜索路径读取共享包中的依赖；运行程序创建加载器时不传游戏路径，仅从自带包读取，避免本机安装掩盖不完整资源。

游戏模型引用的 `.vgcxdata` GPU morph 数据未随当前游戏安装提供，也不被 VRF 骨骼渲染使用；工具明确排除并记录在 `omittedGpuMorphReferences`。其他缺失依赖、资源解析错误或写入校验失败会中断更新。新包写到临时文件，重新读取并校验后才替换旧快照。

清单包含 `sourceBuild`（来自 `steam.inf`）、提取器版本、文件数、大小、SHA256 和依赖；不保存维护机的绝对安装路径。VPK 和清单均按文件路径排序，同一源快照重复提取可比较哈希。更新时保存 VPK 和清单这两个文件，并检查清单差异。

## 验证与构建

```powershell
dotnet restore tests/RendererRegression/RendererRegression.csproj --configfile Pet3D/NuGet.Config
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --assets
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --framing
dotnet run --project tests/RendererRegression/RendererRegression.csproj -c Release --no-restore -- --toolbar
./scripts/build_release.ps1
```

`--assets` 检查逐文件哈希、依赖闭包和模型/UI 数据，不要求 GPU。其余检查使用项目自带包，检查模型切换、动作取景和界面，详见[回归指南](../tests/RendererRegression/README.md)。首次加入新模型或动画时仍应目视检查截图。

发布脚本在发布前检查清单的包哈希，在发布后核对文件和 EXE 版本，并在 `dist/releases/v<版本>/` 生成 `ChickenDesktopPet3D-v<版本>-win-x64.zip` 和 `SHA256SUMS.txt`。ZIP 包含 EXE 和完整 `Resources/`；校验文件同时记录 ZIP、EXE、VPK 和清单的 SHA256。程序版本统一取自项目文件，详见[版本与发布](DEVELOPMENT.zh-CN.md#版本与发布)。资源包缺失会在构建阶段明确报错，不会生成需要用户自行安装游戏的隐式回退版。

## 独立开发更新

可将 `CHICK_PET_VPK` 设置为候选 `pet_assets.vpk` 的绝对路径并重启程序。路径无效时明确报错，不会回退到游戏或旧包。清除变量后恢复 EXE 旁的资源包。程序不使用 `CHICK_CS2_VPK`。

只更新模型、材质或动画时，可以替换 `Resources/` 的 VPK 与清单而无需重新编译 EXE；新增交互逻辑或不受当前 VRF 支持的资源格式仍需修改程序。保持两个资源文件来自同一快照，替换前退出桌宠。

CS2 游戏内容的权利归 Valve，项目的 MIT 许可仅覆盖项目源代码；来源及第三方说明见[第三方声明](../THIRD_PARTY_NOTICES.md)。
