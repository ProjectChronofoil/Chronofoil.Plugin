using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Chronofoil.Capture.Session;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Chronofoil.Capture.Context;

public unsafe class ContextManager : IDisposable
{
    private const string PresentSig = "E8 ?? ?? ?? ?? C6 46 79 00 EB 40";
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

        var gameDevice = Device.Instance();
        if (gameDevice == null) return;
        var gameSwapChain = gameDevice->SwapChain;
        if (gameSwapChain == null) return;

        var device = (ID3D11Device*)gameDevice->D3D11Forwarder;
        ID3D11DeviceContext* deviceContext;
        device->GetImmediateContext(&deviceContext);
        var swapChain = (IDXGISwapChain*)gameSwapChain->DXGISwapChain;

        _lastCtx = ms;
        var captureMs = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();

        ID3D11Texture2D* backbuffer;
        var iid = IID.IID_ID3D11Texture2D;
        swapChain->GetBuffer(0, &iid, (void**)&backbuffer);

        D3D11_TEXTURE2D_DESC backbufferDesc;
        backbuffer->GetDesc(&backbufferDesc);
        var width = (int)backbufferDesc.Width;
        var height = (int)backbufferDesc.Height;

        var stagingTexture = GraphicsHelper.CreateStagingTexture(device, width, height);

        deviceContext->CopyResource((ID3D11Resource*)stagingTexture, (ID3D11Resource*)backbuffer);

        D3D11_MAPPED_SUBRESOURCE mappedResource;
        deviceContext->Map((ID3D11Resource*)stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mappedResource);

        var rowPitch = (int)mappedResource.RowPitch;
        var slicePitch = (int)mappedResource.DepthPitch;
        if (slicePitch == 0)
            slicePitch = rowPitch * height;

        try
        {
            lock (_contextContainer)
            {
                var imageData = new Span<byte>(mappedResource.pData, slicePitch);
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
            deviceContext->Unmap((ID3D11Resource*)stagingTexture, 0);
            stagingTexture->Release();
            backbuffer->Release();
            deviceContext->Release();
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

public static unsafe class GraphicsHelper
{
    public static ID3D11Texture2D* CreateStagingTexture(ID3D11Device* device, int width, int height)
    {
        // For handling of staging resource see
        // http://msdn.microsoft.com/en-US/Library/Windows/Desktop/FF476259(v=vs.85).aspx
        var textureDescription = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            BindFlags = 0,
            CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0,
        };

        ID3D11Texture2D* stagingTexture;
        device->CreateTexture2D(&textureDescription, null, &stagingTexture);
        return stagingTexture;
    }
}