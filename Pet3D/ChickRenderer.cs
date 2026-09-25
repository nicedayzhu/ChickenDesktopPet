using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using Vector3 = System.Numerics.Vector3;
using TextRenderer = ValveResourceFormat.Renderer.TextRenderer;

namespace ChickenDesktopPet3D;

internal sealed class ChickRenderer : IDisposable
{
    public const int Resolution = 512;

    private readonly ConcurrentQueue<(string Name, bool Loop)> requests = new();
    private readonly ConcurrentQueue<float> orbitRequests = new();
    private readonly ConcurrentQueue<string> appearanceRequests = new();
    private volatile bool stopRequested;
    private int pendingFrame;

    public volatile bool LowPower;
    internal (Vector3 Center, float Distance) CameraFraming { get; private set; }

    public event Action<byte[]>? FrameReady;
    public event Action<Exception>? Failed;
    public event Action<IReadOnlyList<PetAppearance>>? AppearancesReady;
    public event Action<PetAppearance, IReadOnlyCollection<string>>? AppearanceChanged;
    public event Action<string, Exception>? AppearanceFailed;
    public event Action<string>? ActionChanged;

    public void Play(string name, bool loop = false) => requests.Enqueue((name, loop));

    public void Orbit(float degrees) => orbitRequests.Enqueue(degrees);

    public void SelectAppearance(string id) => appearanceRequests.Enqueue(id);

    public void FrameConsumed() => Interlocked.Exchange(ref pendingFrame, 0);

    public void Dispose()
    {
        stopRequested = true;
    }

    public void RunOnCurrentThread()
    {
        if (stopRequested) return;
        try
        {
            ErrorLog.Trace("renderer thread started");
            var path = Environment.GetEnvironmentVariable("CHICK_CS2_VPK");
            if (string.IsNullOrWhiteSpace(path))
            {
                var game = GameFolderLocator.FindSteamGameByAppId(730);
                path = game is null ? null : Path.Combine(game.Value.GamePath, "game", "csgo", "pak01_dir.vpk");
            }
            if (path is null || !File.Exists(path))
                throw new FileNotFoundException("未找到 CS2 的 pak01_dir.vpk。请安装 CS2，或设置 CHICK_CS2_VPK 环境变量。", path);
            ErrorLog.Trace("VPK path found");

            var native = new NativeWindowSettings
            {
                APIVersion = GLEnvironment.RequiredVersion,
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                ClientSize = new OpenTK.Mathematics.Vector2i(Resolution, Resolution),
                StartVisible = false,
                StartFocused = false,
                Title = "CS2 chick renderer",
            };
            var gameSettings = new GameWindowSettings { UpdateFrequency = 60 };
            using var window = new RenderWindow(this, path, gameSettings, native);
            ErrorLog.Trace("render window constructed");
            window.Run();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            Failed?.Invoke(ex);
        }
    }

    private sealed class RenderWindow : GameWindow
    {
        private readonly ChickRenderer owner;
        private readonly Package package = new();
        private readonly GameFileLoader loader;
        private readonly RendererContext context;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly byte[] pixels = new byte[Resolution * Resolution * 4];
        private ValveResourceFormat.Renderer.Renderer? renderer;
        private Framebuffer? main;
        private Framebuffer? output;
        private TextRenderer? textRenderer;
        private ModelSceneNode? chick;
        private PetCatalog? catalog;
        private PetAppearance? appearance;
        private Dictionary<string, string> actionClips = new(StringComparer.Ordinal);
        private Vector3 cameraCenter;
        private (Vector3 Center, float Distance) defaultFraming;
        private readonly Dictionary<string, (Vector3 Center, float Distance)> actionFraming = new();
        private float cameraOffset;
        private float yaw = MathF.Atan2(.75f, 1f);
        private float targetYaw = MathF.Atan2(.75f, 1f);
        private string currentAction = "idle";
        private double actionEndsAt = double.PositiveInfinity;
        private int renderedFrames;
        private bool firstUpdateLogged;

        public RenderWindow(ChickRenderer owner, string vpkPath, GameWindowSettings settings, NativeWindowSettings native)
            : base(settings, native)
        {
            this.owner = owner;
            package.Read(vpkPath);
            loader = new GameFileLoader(package, vpkPath);
            context = new RendererContext(loader, NullLogger.Instance);
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            ErrorLog.Trace("GL window OnLoad");
            GLEnvironment.Initialize(context.Logger);
            GLEnvironment.SetDefaultRenderState(context);
            renderer = new ValveResourceFormat.Renderer.Renderer(context);
            // A portrait lens keeps framing tight without strong perspective
            // distortion or excessive empty space around the animated bounds.
            renderer.Camera.FieldOfView = 35;
            renderer.Camera.NearPlane = .01f;
            renderer.Camera.SetViewportSize(Resolution, Resolution);
            textRenderer = new TextRenderer(context, renderer.Camera);
            textRenderer.Load();

            main = Framebuffer.Prepare("ChickMain", Resolution, Resolution, 4, ImageFormat.RGBA16161616F, ImageFormat.D32);
            main.Initialize();
            output = Framebuffer.Prepare("ChickTransparent", Resolution, Resolution, 0, ImageFormat.RGBA8888, null);
            output.ClearMask = ClearBufferMask.ColorBufferBit;
            output.ClearColor = new OpenTK.Mathematics.Color4(0, 0, 0, 0);
            output.Initialize();
            renderer.Initialize();
            renderer.MainFramebuffer = main;
            renderer.Postprocess.Load(main.NumSamples);
            renderer.Postprocess.FullScreenGamma = 2.01f;
            renderer.Postprocess.ExposureCompensation = -0.4f;
            renderer.LoadRendererResources();
            renderer.Scene.ShowToolsMaterials = true;

            using (var stream = typeof(ChickRenderer).Assembly.GetManifestResourceStream("ChickLighting")
                ?? throw new InvalidDataException("未找到内置环境光照资源"))
            using (var environment = new Resource { FileName = "vrf_default_cubemap.vtex_c" })
            {
                environment.Read(stream);
                ValveResourceFormat.Renderer.Renderer.LoadDefaultLighting(renderer.Scene, environment);
            }
            catalog = new PetCatalog(package, loader);
            if (catalog.Appearances.Count == 0)
                throw new FileNotFoundException("CS2 VPK 中没有可用的鸡宠物模型");
            owner.AppearancesReady?.Invoke(catalog.Appearances);
            var initialId = PetCatalog.DefaultAppearanceId;
            while (owner.appearanceRequests.TryDequeue(out var requestedId)) initialId = requestedId;
            if (!TrySwitchAppearance(initialId))
            {
                var fallback = catalog.Find(PetCatalog.DefaultAppearanceId) ?? catalog.Appearances[0];
                if (!TrySwitchAppearance(fallback.Id))
                    throw new InvalidDataException("无法加载默认鸡宠物模型");
            }
            renderer.Scene.PostProcessInfo.AddPostProcessVolume(new ScenePostProcessVolume(renderer.Scene)
            {
                // Bloom spreads light beyond the feathers into the transparent
                // window. On dark desktops that reads as a square, glowing panel.
                HasBloom = false,
                IsMaster = true,
            });
            renderer.Scene.Initialize();
            renderer.Camera.SetViewportSize(Resolution, Resolution);
            UpdateCamera();
            ErrorLog.Trace("scene loaded");
        }

        private static readonly string[] ActionNames =
            ["idle", "idle2", "squat", "walk", "react", "react2", "trick", "trick2", "feed", "sleep"];

        private bool TrySwitchAppearance(string id)
        {
            var selected = catalog?.Find(id);
            if (renderer is null || catalog is null || selected is null)
            {
                owner.AppearanceFailed?.Invoke(id, new FileNotFoundException($"当前 CS2 版本没有宠物外观 {id}"));
                return false;
            }
            if (appearance?.Id == selected.Id) return true;

            ModelSceneNode? next = null;
            try
            {
                if (appearance?.ModelId == selected.ModelId && chick is not null)
                {
                    chick.SetMaterialGroup(selected.Skin ?? "default");
                    SmoothFeatherEdges(chick);
                    appearance = selected;
                    owner.AppearanceChanged?.Invoke(selected, actionClips.Keys.ToArray());
                    return true;
                }

                using var resource = loader.LoadFileCompiled(selected.ModelPath)
                    ?? throw new FileNotFoundException("模型资源已从 CS2 中移除", selected.ModelPath);
                if (resource.DataBlock is not Model model)
                    throw new InvalidDataException($"模型资源无法读取：{selected.ModelPath}");
                next = new ModelSceneNode(renderer.Scene, model, skin: selected.Skin, isWorldPreview: false);
                if (!next.HasMeshes)
                    throw new InvalidDataException($"模型没有可绘制网格：{selected.ModelPath}");
                SmoothFeatherEdges(next);
                PoseBounds? nextBounds = null;
                try { nextBounds = new PoseBounds(model, next.AnimationController, loader); }
                catch (Exception ex) { ErrorLog.Trace($"using authored framing bounds: {ex.Message}"); }
                var nextClips = LoadActions(next, selected);
                // The renderer initializes bone buffers while loading the model's
                // referenced animation set. Keep only the clips this pet can use.
                var playableClips = nextClips.Values.ToHashSet(StringComparer.Ordinal);
                foreach (var name in next.Animations.Keys.Where(name => !playableClips.Contains(name)).ToArray())
                    next.Animations.Remove(name);
                ErrorLog.Trace($"animation state {selected.Id}: {next.Animations.Count} clips, skinning={next.IsAnimated}");

                renderer.Scene.Add(next, true);
                var previous = chick;
                chick = next;
                next = null;
                appearance = selected;
                actionClips = nextClips;
                if (previous is not null)
                {
                    renderer.Scene.Remove(previous, true);
                    previous.Delete();
                    RefreshLightingBindings();
                }

                var bounds = chick.BoundingBox;
                cameraCenter = bounds.Center;
                cameraOffset = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)) * 3;
                defaultFraming = (cameraCenter, cameraOffset);
                actionFraming.Clear();
                if (nextBounds is not null)
                {
                    var everyday = actionClips.Where(x => x.Key is not ("feed" or "trick" or "trick2"))
                        .Select(x => chick.Animations[x.Value]).Distinct().ToArray();
                    defaultFraming = nextBounds.MeasureFraming(chick.AnimationController,
                        everyday, bounds, cameraCenter, renderer.Camera.GetFOV(), false);
                    foreach (var action in actionClips.Where(x => x.Key is "feed" or "trick" or "trick2"))
                        actionFraming[action.Key] = nextBounds.MeasureFraming(chick.AnimationController,
                            [chick.Animations[action.Value]], bounds, cameraCenter, renderer.Camera.GetFOV(), true);
                    (cameraCenter, cameraOffset) = defaultFraming;
                }
                UpdateCamera();
                SetAction("idle", true);
                ErrorLog.Trace($"selected {selected.Id}: {actionClips.Count} actions");
                owner.AppearanceChanged?.Invoke(selected, actionClips.Keys.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                next?.Delete();
                ErrorLog.Write(new InvalidDataException($"无法切换到外观 {id}", ex));
                owner.AppearanceFailed?.Invoke(id, ex);
                return false;
            }
        }

        private void RefreshLightingBindings()
        {
            var scene = renderer!.Scene;
            // Add/Remove only mark the octree dirty. Unlike Initialize(), the
            // regular scene update does not bind lighting to newly added nodes.
            // Rebuild spatial queries first, then assign the same lighting as at
            // startup before the next update uploads per-instance visibility.
            scene.UpdateOctrees();
            foreach (var node in scene.AllNodes)
            {
                node.EnvMaps.Clear();
                node.LightProbeBinding = null;
            }
            scene.CalculateLightProbeBindings();
            scene.CalculateEnvironmentMaps();
            scene.UpdateBuffers();
            // UpdateOctrees cleared this flag, but the instance buffers still
            // need rebuilding with the new node IDs and lighting bindings.
            scene.DynamicOctree.Dirty = true;
        }

        private static void SmoothFeatherEdges(ModelSceneNode node)
        {
            // Alpha-tested feather cards need stronger coverage in a transparent window.
            foreach (var material in node.RenderableMeshes.SelectMany(mesh => mesh.DrawCallsOpaque)
                .Select(draw => draw.Material).Where(material => material.IsAlphaTest).Distinct())
            {
                material.FloatParams["g_flAntiAliasedEdgeStrength"] = 0.85f;
            }
        }

        private Dictionary<string, string> LoadActions(ModelSceneNode node, PetAppearance selected)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var action in ActionNames)
            {
                foreach (var path in catalog!.ClipCandidates(selected, action))
                {
                    try
                    {
                        using var resource = loader.LoadFileCompiled(path);
                        if (resource?.DataBlock is not AnimationClip clip) continue;
                        Animation animation = new ClipAnimation(clip);
                        if (!node.AnimationController.IsPlayable(animation)) continue;
                        if (!node.Animations.ContainsKey(animation.Name)) node.AddAnimations([animation]);
                        result[action] = animation.Name;
                        break;
                    }
                    catch (Exception ex) { ErrorLog.Trace($"skipped animation {path}: {ex.Message}"); }
                }
            }

            if (!result.ContainsKey("idle"))
            {
                var embeddedIdle = node.Animations.FirstOrDefault(item =>
                    item.Key.Contains("idle", StringComparison.OrdinalIgnoreCase));
                if (embeddedIdle.Key is not null) result["idle"] = embeddedIdle.Key;
            }
            if (selected.Kind == PetKind.Egg && !result.ContainsKey("trick"))
            {
                var hatch = node.Animations.FirstOrDefault(item =>
                    item.Key.Contains("hatch", StringComparison.OrdinalIgnoreCase));
                if (hatch.Key is not null) result["trick"] = hatch.Key;
            }
            return result;
        }

        private void SetAction(string name, bool loop)
        {
            if (chick is null) return;
            if (!actionClips.TryGetValue(name, out var clip) ||
                !chick.Animations.TryGetValue(clip, out Animation? animation))
            {
                if (name == "idle")
                {
                    (cameraCenter, cameraOffset) = defaultFraming;
                    chick.SetAnimation(null);
                    currentAction = "idle";
                    actionEndsAt = double.PositiveInfinity;
                    owner.ActionChanged?.Invoke("idle");
                }
                return;
            }
            currentAction = name;
            (cameraCenter, cameraOffset) = actionFraming.GetValueOrDefault(name, defaultFraming);
            chick.AnimationController.Looping = loop;
            chick.SetAnimationByName(clip, 0.16f);
            actionEndsAt = loop ? double.PositiveInfinity : clock.Elapsed.TotalSeconds + Math.Max(animation.Duration, 0.5f);
            owner.ActionChanged?.Invoke(name);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            if (!firstUpdateLogged) { ErrorLog.Trace("first update"); firstUpdateLogged = true; }
            if (owner.stopRequested) { Close(); return; }
            if (renderer is null || textRenderer is null) return;

            string? nextAppearance = null;
            while (owner.appearanceRequests.TryDequeue(out var requestedAppearance)) nextAppearance = requestedAppearance;
            if (nextAppearance is not null) TrySwitchAppearance(nextAppearance);
            while (owner.requests.TryDequeue(out var request))
                SetAction(request.Name, request.Loop);
            while (owner.orbitRequests.TryDequeue(out var degrees))
                targetYaw += MathF.PI * degrees / 180f;
            var cameraBlend = 1f - MathF.Exp(-(float)Math.Min(args.Time, .05) * 8f);
            yaw += (targetYaw - yaw) * cameraBlend;
            if (clock.Elapsed.TotalSeconds >= actionEndsAt)
                SetAction("idle", true);

            renderer.Update(new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer,
                Timestep = (float)Math.Min(args.Time, .05),
            });
            UpdateCamera();
            // Refresh culling after orbiting so draw calls use the current view.
            renderer.Scene.CollectSceneDrawCalls(renderer.Camera, renderer.Camera.ViewFrustum);
        }

        private void UpdateCamera()
        {
            if (renderer is null || chick is null) return;
            var outward = Vector3.Normalize(new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), .32f));
            renderer.Camera.SetLocation(cameraCenter + outward * cameraOffset);
            renderer.Camera.LookAt(cameraCenter);
            renderer.Camera.RecalculateMatrices();
        }

        protected override unsafe void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            if (renderedFrames == 0) ErrorLog.Trace("first render");
            if (renderer is null || main is null || output is null) return;
            renderedFrames++;
            var divisor = appearance?.Kind == PetKind.Static ? 4 : owner.LowPower
                ? (currentAction is "idle" or "idle2" or "sleep" ? 4 : 2)
                : (currentAction is "idle" or "idle2" or "sleep" ? 2 : 1);
            if (renderedFrames % divisor != 0) return;
            if (Interlocked.CompareExchange(ref owner.pendingFrame, 1, 0) != 0) return;

            try
            {
                using var renderState = context.RenderState.Scope();
                main.Bind(FramebufferTarget.Framebuffer);
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(main.ClearMask);
                // Alpha-to-coverage already turns fur opacity into sample coverage.
                // Store opaque alpha in each surviving MSAA sample, so foreground
                // feathers cannot punch translucent seams through the body below.
                // Preserve normal alpha blending for any future translucent model.
                var coverageOnly = chick is not null &&
                    chick.RenderableMeshes.All(mesh => mesh.DrawCallsBlended.Count == 0);
                if (coverageOnly) GL.Enable(EnableCap.SampleAlphaToOne);
                try { renderer.DrawMainScene(); }
                finally { GL.Disable(EnableCap.SampleAlphaToOne); }
                output.BindAndClear();
                renderer.PostprocessRender(main, output, flipY: true);
                output.Bind(FramebufferTarget.ReadFramebuffer);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                fixed (byte* address = pixels)
                    GL.ReadPixels(0, 0, Resolution, Resolution, PixelFormat.Bgra, PixelType.UnsignedByte, (nint)address);
                // WPF's Pbgra32 layered window requires every RGB channel to be
                // no brighter than alpha. Source 2 postprocessing leaves color in
                // fully transparent pixels; the desktop compositor can show it as
                // a faint square glow against dark wallpaper.
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = pixels[i + 3];
                    if (alpha == 0)
                    {
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                    }
                    else if (alpha < 255)
                    {
                        pixels[i] = Math.Min(pixels[i], alpha);
                        pixels[i + 1] = Math.Min(pixels[i + 1], alpha);
                        pixels[i + 2] = Math.Min(pixels[i + 2], alpha);
                    }
                }
                owner.CameraFraming = (cameraCenter, Vector3.Distance(renderer.Camera.Location, cameraCenter));
                if (owner.FrameReady is { } onFrame) onFrame(pixels);
                else owner.FrameConsumed();
            }
            catch
            {
                owner.FrameConsumed();
                throw;
            }
        }

        protected override void OnUnload()
        {
            output?.Delete();
            main?.Delete();
            renderer?.Dispose();
            loader.Dispose();
            package.Dispose();
            base.OnUnload();
        }
    }

}
