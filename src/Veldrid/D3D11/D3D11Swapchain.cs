using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Veldrid.D3D11
{
    internal class D3D11Swapchain : Swapchain
    {
        public override Framebuffer Framebuffer => _framebuffer;

        public PresentFlags PresentFlags
        {
            get
            {
                if (AllowTearing && _canTear && !SyncToVerticalBlank)
                    return PresentFlags.AllowTearing;

                return PresentFlags.None;
            }
        }

        public IDXGISwapChain DxgiSwapChain { get; private set; }

        public int SyncInterval { get; private set; }

        public override bool IsDisposed => _disposed;

        public override string Name
        {
            get
            {
                unsafe
                {
                    byte* pname = stackalloc byte[1024];
                    int size = 1024 - 1;
                    DxgiSwapChain.GetPrivateData(CommonGuid.DebugObjectName, ref size, new IntPtr(pname));
                    pname[size] = 0;
                    return Marshal.PtrToStringAnsi(new IntPtr(pname));
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                    DxgiSwapChain.SetPrivateData(CommonGuid.DebugObjectName, 0, IntPtr.Zero);
                else
                {
                    IntPtr namePtr = Marshal.StringToHGlobalAnsi(value);
                    DxgiSwapChain.SetPrivateData(CommonGuid.DebugObjectName, value.Length, namePtr);
                    Marshal.FreeHGlobal(namePtr);
                }
            }
        }

        public override bool SyncToVerticalBlank
        {
            get => _vsync;
            set
            {
                _vsync = value;
                SyncInterval = D3D11Util.GetSyncInterval(value);
            }
        }

        public bool AllowTearing
        {
            get => _allowTearing;
            set
            {
                if (_allowTearing == value)
                    return;

                _allowTearing = value;

                if (!_canTear)
                    return;

                recreateSwapchain();
            }
        }

        private readonly D3D11GraphicsDevice _gd;
        private readonly SwapchainDescription _description;
        private readonly PixelFormat? _depthFormat;

        private readonly object _referencedCLsLock = new object();

        private readonly bool _canTear;
        private readonly bool _canCreateFrameLatencyWaitableObject;
        private readonly Format _colorFormat;
        private bool _vsync;
        private D3D11Framebuffer _framebuffer;
        private D3D11Texture _depthTexture;
        private float _pixelScale = 1f;
        private SwapChainFlags _flags;
        private bool _disposed;
        private FrameLatencyWaitHandle _frameLatencyWaitHandle;
        private readonly HashSet<D3D11CommandList> _referencedCLs = new HashSet<D3D11CommandList>();

        private bool _allowTearing;

        private uint _width;
        private uint _height;

        private ID3D11Texture2D backBufferTexture;

        public D3D11Swapchain(D3D11GraphicsDevice gd, ref SwapchainDescription description)
        {
            _gd = gd;
            _description = description;
            _depthFormat = description.DepthFormat;
            SyncToVerticalBlank = description.SyncToVerticalBlank;

            _colorFormat = description.ColorSrgb
                ? Format.B8G8R8A8_UNorm_SRgb
                : Format.B8G8R8A8_UNorm;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory5 = _gd.Adapter.GetParent(out IDXGIFactory5 f).Success ? f : null)
                _canTear = dxgiFactory5?.PresentAllowTearing == true;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory3 = _gd.Adapter.GetParent(out IDXGIFactory3 f).Success ? f : null)
                _canCreateFrameLatencyWaitableObject = dxgiFactory3 != null;

            _width = description.Width;
            _height = description.Height;

            recreateSwapchain();
        }

        #region Disposal

        public override void Dispose()
        {
            if (!_disposed)
            {
                _depthTexture?.Dispose();
                _framebuffer.Dispose();
                DxgiSwapChain.Dispose();

                _disposed = true;
            }
        }

        #endregion

        public override void Resize(uint width, uint height)
        {
            _width = width;
            _height = height;

            lock (_referencedCLsLock)
            {
                foreach (var cl in _referencedCLs) cl.Reset();

                _referencedCLs.Clear();
            }

            bool resizeBuffers = false;

            if (_framebuffer != null)
            {
                resizeBuffers = true;
                if (_depthTexture != null) _depthTexture.Dispose();

                backBufferTexture.Dispose();
                _framebuffer.Dispose();
            }

            uint actualWidth = (uint)(width * _pixelScale);
            uint actualHeight = (uint)(height * _pixelScale);
            if (resizeBuffers) DxgiSwapChain.ResizeBuffers(2, (int)actualWidth, (int)actualHeight, _colorFormat, _flags).CheckError();

            // Get the backbuffer from the swapchain
            backBufferTexture = DxgiSwapChain.GetBuffer<ID3D11Texture2D>(0);

            if (_depthFormat != null)
            {
                var depthDesc = new TextureDescription(
                    actualWidth, actualHeight, 1, 1, 1,
                    _depthFormat.Value,
                    TextureUsage.DepthStencil,
                    TextureType.Texture2D);
                _depthTexture = new D3D11Texture(_gd.Device, ref depthDesc);
            }

            var backBufferVdTexture = new D3D11Texture(
                backBufferTexture,
                TextureType.Texture2D,
                D3D11Formats.ToVdFormat(_colorFormat));

            var desc = new FramebufferDescription(_depthTexture, backBufferVdTexture);
            _framebuffer = new D3D11Framebuffer(_gd.Device, ref desc)
            {
                Swapchain = this
            };
        }

        public void WaitForNextFrameReady()
        {
            _frameLatencyWaitHandle?.WaitOne(1000);
        }

        public void AddCommandListReference(D3D11CommandList cl)
        {
            lock (_referencedCLsLock) _referencedCLs.Add(cl);
        }

        public void RemoveCommandListReference(D3D11CommandList cl)
        {
            lock (_referencedCLsLock) _referencedCLs.Remove(cl);
        }

        private void recreateSwapchain()
        {
            DxgiSwapChain?.Release();
            DxgiSwapChain?.Dispose();
            DxgiSwapChain = null;

            _framebuffer?.Dispose();
            _framebuffer = null;

            _depthTexture?.Dispose();
            _depthTexture = null;

            _frameLatencyWaitHandle?.Dispose();
            _frameLatencyWaitHandle = null;

            // FlipDiscard is only supported on DXGI 1.4+
            bool canUseFlipDiscard;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory4 = _gd.Adapter.GetParent(out IDXGIFactory4 f).Success ? f : null)
                canUseFlipDiscard = dxgiFactory4 != null;

            var swapEffect = canUseFlipDiscard ? SwapEffect.FlipDiscard : SwapEffect.Discard;

            _flags = SwapChainFlags.None;

            if (AllowTearing && _canTear)
                _flags |= SwapChainFlags.AllowTearing;
            else if (_canCreateFrameLatencyWaitableObject && canUseFlipDiscard)
                _flags |= SwapChainFlags.FrameLatencyWaitableObject;

            if (_description.Source is Win32SwapchainSource win32Source)
            {
                var dxgiSCDesc = new SwapChainDescription
                {
                    BufferCount = 2,
                    Windowed = true,
                    BufferDescription = new ModeDescription(
                        (int)_width, (int)_height, _colorFormat),
                    OutputWindow = win32Source.Hwnd,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = swapEffect,
                    BufferUsage = Usage.RenderTargetOutput,
                    Flags = _flags
                };

                using (var dxgiFactory = _gd.Adapter.GetParent<IDXGIFactory>())
                {
                    DxgiSwapChain = dxgiFactory.CreateSwapChain(_gd.Device, dxgiSCDesc);
                    dxgiFactory.MakeWindowAssociation(win32Source.Hwnd, WindowAssociationFlags.IgnoreAltEnter);
                }
            }
            else if (_description.Source is UwpSwapchainSource uwpSource)
            {
                _pixelScale = uwpSource.LogicalDpi / 96.0f;

                // Properties of the swap chain
                var swapChainDescription = new SwapChainDescription1
                {
                    AlphaMode = AlphaMode.Ignore,
                    BufferCount = 2,
                    Format = _colorFormat,
                    Height = (int)(_height * _pixelScale),
                    Width = (int)(_width * _pixelScale),
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.FlipSequential,
                    BufferUsage = Usage.RenderTargetOutput,
                    Flags = _flags
                };

                // Get the Vortice.DXGI factory automatically created when initializing the Direct3D device.
                using (var dxgiFactory = _gd.Adapter.GetParent<IDXGIFactory2>())
                {
                    // Create the swap chain and get the highest version available.
                    using (var swapChain1 = dxgiFactory.CreateSwapChainForComposition(_gd.Device, swapChainDescription)) DxgiSwapChain = swapChain1.QueryInterface<IDXGISwapChain2>();
                }

                var co = new ComObject(uwpSource.SwapChainPanelNative);

                var swapchainPanelNative = co.QueryInterfaceOrNull<ISwapChainPanelNative>();

                if (swapchainPanelNative != null)
                    swapchainPanelNative.SetSwapChain(DxgiSwapChain);
                else
                {
                    var bgPanelNative = co.QueryInterfaceOrNull<ISwapChainBackgroundPanelNative>();
                    if (bgPanelNative != null) bgPanelNative.SetSwapChain(DxgiSwapChain);
                }
            }

            if ((_flags & SwapChainFlags.FrameLatencyWaitableObject) > 0)
            {
                using (var swapChain2 = DxgiSwapChain.QueryInterfaceOrNull<IDXGISwapChain2>())
                {
                    if (swapChain2 != null)
                    {
                        swapChain2.MaximumFrameLatency = 1;
                        _frameLatencyWaitHandle = new FrameLatencyWaitHandle(swapChain2.FrameLatencyWaitableObject);
                    }
                }
            }

            Resize(_width, _height);
        }

        private class FrameLatencyWaitHandle : WaitHandle
        {
            public FrameLatencyWaitHandle(IntPtr ptr)
            {
                SafeWaitHandle = new SafeWaitHandle(ptr, true);
            }
        }
    }
}
