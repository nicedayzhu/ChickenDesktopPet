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
    private volatile bool stopRequested;
    private int pendingFrame;

    public volatile bool LowPower;

    public event Action<byte[]>? FrameReady;
    public event Action<Exception>? Failed;

    public void Play(string name, bool loop = false) => requests.Enqueue((name, loop));

    public void Orbit(float degrees) => orbitRequests.Enqueue(degrees);

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
        private Vector3 cameraCenter;
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
            using var modelResource = loader.LoadFileCompiled("models/chicken/chick.vmdl")
                ?? throw new FileNotFoundException("CS2 VPK 中没有 models/chicken/chick.vmdl_c");
            var model = modelResource.DataBlock as Model
                ?? throw new InvalidDataException("小鸡模型资源无法读取");
            chick = new ModelSceneNode(renderer.Scene, model, isWorldPreview: true);
            // The game's default edge strength is tuned for an opaque viewport. At desktop-pet
            // size it exposes individual feather cards; stronger coverage smooths their edges.
            foreach (var material in chick.RenderableMeshes.SelectMany(mesh => mesh.DrawCallsOpaque)
                .Select(draw => draw.Material).Where(material => material.IsAlphaTest).Distinct())
                material.FloatParams["g_flAntiAliasedEdgeStrength"] = 0.85f;
            var selectedAnimations = new List<Animation>();
            foreach (var clipName in ClipNames)
            {
                using var clipResource = loader.LoadFileCompiled(clipName);
                if (clipResource?.DataBlock is AnimationClip clip)
                    selectedAnimations.Add(new ClipAnimation(clip));
            }
            chick.AddAnimations(selectedAnimations);
            ErrorLog.Trace($"loaded {selectedAnimations.Count} selected clips");
            renderer.Scene.Add(chick, true);
            renderer.Scene.PostProcessInfo.AddPostProcessVolume(new ScenePostProcessVolume(renderer.Scene)
            {
                // Bloom spreads light beyond the feathers into the transparent
                // window. On dark desktops that reads as a square, glowing panel.
                HasBloom = false,
                IsMaster = true,
            });
            renderer.Scene.Initialize();

            var bounds = chick.BoundingBox;
            cameraCenter = bounds.Center;
            cameraOffset = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)) * 0.9f;
            renderer.Camera.SetViewportSize(Resolution, Resolution);
            UpdateCamera();
            SetAction("idle", true);
            ErrorLog.Trace("scene loaded");
        }

        private static string? Clip(string name) => name switch
        {
            "idle" => "animation/anims/chicken/world/chick_idle01.vnmclip",
            "idle2" => "animation/anims/chicken/world/chick_idle02.vnmclip",
            "squat" => "animation/anims/chicken/world/chick_squat_loop04.vnmclip",
            "walk" => "animation/anims/chicken/world/chick_walk.vnmclip",
            "react" => "animation/anims/chicken/world/chick_react01.vnmclip",
            "react2" => "animation/anims/chicken/world/chick_react02.vnmclip",
            "trick" => "animation/anims/chicken/world/chick_trick01.vnmclip",
            "trick2" => "animation/anims/chicken/world/chick_trick03.vnmclip",
            "feed" => "animation/anims/chicken/ui/chickbaby_feed02.vnmclip",
            "sleep" => "animation/anims/chicken/world/chick_sleep_loop01.vnmclip",
            _ => null,
        };

        private static readonly string[] ClipNames =
        [
            "animation/anims/chicken/world/chick_idle01.vnmclip",
            "animation/anims/chicken/world/chick_idle02.vnmclip",
            "animation/anims/chicken/world/chick_squat_loop04.vnmclip",
            "animation/anims/chicken/world/chick_walk.vnmclip",
            "animation/anims/chicken/world/chick_react01.vnmclip",
            "animation/anims/chicken/world/chick_react02.vnmclip",
            "animation/anims/chicken/world/chick_trick01.vnmclip",
            "animation/anims/chicken/world/chick_trick03.vnmclip",
            "animation/anims/chicken/ui/chickbaby_feed02.vnmclip",
            "animation/anims/chicken/world/chick_sleep_loop01.vnmclip",
        ];

        private void SetAction(string name, bool loop)
        {
            if (chick is null) return;
            var clip = Clip(name);
            if (clip is null || !chick.Animations.TryGetValue(clip, out Animation? animation)) return;
            currentAction = name;
            chick.SetAnimationByName(clip, 0.16f);
            actionEndsAt = loop ? double.PositiveInfinity : clock.Elapsed.TotalSeconds + Math.Max(animation.Duration, 0.5f);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            if (!firstUpdateLogged) { ErrorLog.Trace("first update"); firstUpdateLogged = true; }
            if (owner.stopRequested) { Close(); return; }
            if (renderer is null || textRenderer is null) return;

            while (owner.requests.TryDequeue(out var request))
                SetAction(request.Name, request.Loop);
            while (owner.orbitRequests.TryDequeue(out var degrees))
                targetYaw += MathF.PI * degrees / 180f;
            var cameraBlend = 1f - MathF.Exp(-(float)Math.Min(args.Time, .05) * 8f);
            yaw += (targetYaw - yaw) * cameraBlend;
            UpdateCamera();
            if (clock.Elapsed.TotalSeconds >= actionEndsAt)
                SetAction("idle", true);

            renderer.Update(new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer,
                Timestep = (float)Math.Min(args.Time, .05),
            });
        }

        private void UpdateCamera()
        {
            if (renderer is null) return;
            var radius = cameraOffset * 1.25f;
            renderer.Camera.SetLocation(cameraCenter + new Vector3(MathF.Cos(yaw) * radius,
                MathF.Sin(yaw) * radius, cameraOffset * .4f));
            renderer.Camera.LookAt(cameraCenter - new Vector3(0, 0, 1f));
        }

        protected override unsafe void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            if (renderedFrames == 0) ErrorLog.Trace("first render");
            if (renderer is null || main is null || output is null) return;
            renderedFrames++;
            var divisor = owner.LowPower
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
                renderer.DrawMainScene();
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
