using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using System.Threading.Tasks;
using Chronofoil.Capture.Session;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using SharpDX.Direct3D11;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chronofoil.Capture.Context;

public unsafe class ContextManager : IDisposable
{
    private const string PresentSig = "E8 ?? ?? ?? ?? C6 47 79 00";
    private const int Interval = 5000;

    private delegate void PresentPrototype(nint address);

    private readonly Hook<PresentPrototype> _presentHook;
    private nint _presentPtr;

    private ulong _lastCtx;
    private readonly ContextContainer _contextContainer;
    private string _contextDir;
    private readonly CancellationTokenSource _tokenSource;
    
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly CaptureSessionManager _csm;

    public ContextManager(
        IPluginLog log,
        Configuration config,
        CaptureSessionManager captureSessionManager,
        ISigScanner scanner,
        IGameInteropProvider hooks)
    {
        _log = log;
        _config = config;
        _csm = captureSessionManager;
        _contextContainer = new ContextContainer();
        _tokenSource = new CancellationTokenSource();

        // Don't sigscan, don't hook, don't do anything
        // Only people who know what they're doing should modify the context setting anyways
        // They should know it doesn't support ReShade!
        if (!_config.EnableContext) return;

        _presentPtr = scanner.ScanText(PresentSig);
        _presentHook = hooks.HookFromAddress<PresentPrototype>(_presentPtr, PresentDetour);

        _csm.CaptureSessionStarted += CaptureStarted;
        _csm.CaptureSessionFinished += CaptureFinished;
    }

    private void CaptureStarted(Guid captureId, DateTime startTime)
    {
        if (!_config.EnableContext) return;
        
        lock (_contextContainer)
            _contextContainer.CaptureGuid = captureId;
        _contextDir = Path.Combine(_config.StorageDirectory, $"{captureId}_ctx");
        Directory.CreateDirectory(_contextDir);
        _lastCtx = 0;
        _presentHook.Enable();
    }

    private void CaptureFinished(Guid captureId, DateTime endTime)
    {
        _presentHook.Disable();
    }

    public void Dispose()
    {
        _presentHook?.Dispose();
        _tokenSource?.Cancel();
    }

    private void PresentDetour(nint ptr)
    {
        var ms = (ulong)Environment.TickCount64;
        if (ms - _lastCtx <= (ulong)Interval)
        {
            _presentHook.Original(ptr);
            return;
        }

        var gameDevice = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (gameDevice == null) return;
        var gameSwapChain = gameDevice->SwapChain;
        if (gameSwapChain == null) return;

        var device = new SharpDX.Direct3D11.Device((nint)gameDevice->D3D11Forwarder);
        var deviceContext = device.ImmediateContext;
        var swapChain = new SharpDX.DXGI.SwapChain((nint)gameSwapChain->DXGISwapChain);

        _lastCtx = ms;
        var captureMs = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();

        using var backbuffer = swapChain.GetBackBuffer<Texture2D>(0);
        var width = backbuffer.Description.Width;
        var height = backbuffer.Description.Height;
        using var stagingTexture = GraphicsHelper.CreateStagingTexture(device, width, height);

        using var rt = new RenderTargetView(device, backbuffer);
        using var res = rt.Resource;
        deviceContext.CopyResource(res, stagingTexture);
        var dataBox = deviceContext.MapSubresource(stagingTexture, 0, MapMode.Read, MapFlags.None);

        var rowPitch = dataBox.RowPitch;
        var slicePitch = dataBox.SlicePitch;

        try
        {
            lock (_contextContainer)
            {
                var imageData = new Span<byte>((void*)dataBox.DataPointer, slicePitch);
                _contextContainer.LoadImageData(imageData, width, height, rowPitch);
                _contextContainer.CaptureTime = captureMs;
            }

            Task.Run(RenderContext, _tokenSource.Token);
        }
        catch (Exception e)
        {
            _log.Error(e, "feijfjew");
        }
        finally
        {
            deviceContext?.UnmapSubresource(stagingTexture, 0);
            _presentHook?.Original(ptr);
        }
    }

    private void RenderContext()
    {
        try
        {
            lock (_contextContainer)
            {
                var captureTime = _contextContainer.CaptureTime;

                _contextContainer.Image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var pixelRow = accessor.GetRowSpan(y);
                        for (int x = 0; x < pixelRow.Length; x++)
                            pixelRow[x].A = 255;
                    }
                });

                _contextContainer.Image.SaveAsJpeg(Path.Combine(_contextDir, $"ctx-{captureTime}.jpeg"));
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "[RenderContext] Failed.");
        }
    }
}

internal class ContextContainer
{
    public Guid CaptureGuid { get; set; }
    public ulong CaptureTime { get; set; }
    public Image<Rgba32> Image { get; private set; }

    public ContextContainer()
    {
        Image = null;
    }

    public void LoadImageData(Span<byte> srcSpan, int width, int height, int rowPitch)
    {
        var config = SixLabors.ImageSharp.Configuration.Default.Clone();

        config.PreferContiguousImageBuffers = true;
        Image = new Image<Rgba32>(config, width, height);
        Image.DangerousTryGetSinglePixelMemory(out var mem);
        var tgtSpan = MemoryMarshal.Cast<Rgba32, byte>(mem.Span);

        for (int y = 0; y < height; y++)
        {
            var padding = y * (rowPitch - width * 4);
            var srcIdx = (y * width * 4) + padding;
            var tgtIdx = (y * width * 4);
            srcSpan.Slice(srcIdx, width * 4).CopyTo(tgtSpan.Slice(tgtIdx, width * 4));
        }
    }
}

public static class GraphicsHelper
{
    public static Texture2D CreateStagingTexture(SharpDX.DXGI.Device device, int width, int height)
    {
        var nDevice = new Device(device.NativePointer);
        return CreateStagingTexture(nDevice, width, height);
    }

    public static Texture2D CreateStagingTexture(Device device, int width, int height)
    {
        // For handling of staging resource see
        // http://msdn.microsoft.com/en-US/Library/Windows/Desktop/FF476259(v=vs.85).aspx
        var textureDescription = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Staging,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None,
        };
        return new Texture2D(device, textureDescription);
    }
}