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
        public override Framebuffer Framebuffer => framebuffer;

        public PresentFlags PresentFlags
        {
            get
            {
                if (AllowTearing && canTear && !SyncToVerticalBlank)
                    return PresentFlags.AllowTearing;

                return PresentFlags.None;
            }
        }

        public IDXGISwapChain DxgiSwapChain { get; private set; }

        public int SyncInterval { get; private set; }

        public override bool IsDisposed => disposed;

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
            get => vsync;
            set
            {
                vsync = value;
                SyncInterval = D3D11Util.GetSyncInterval(value);
            }
        }

        public bool AllowTearing
        {
            get => allowTearing;
            set
            {
                if (allowTearing == value)
                    return;

                allowTearing = value;

                if (!canTear)
                    return;

                recreateSwapchain();
            }
        }

        private readonly D3D11GraphicsDevice gd;
        private readonly SwapchainDescription description;
        private readonly PixelFormat? depthFormat;

        private readonly object referencedCLsLock = new object();

        private readonly bool canTear;
        private readonly bool canCreateFrameLatencyWaitableObject;
        private readonly Format colorFormat;
        private bool vsync;
        private D3D11Framebuffer framebuffer;
        private D3D11Texture depthTexture;
        private float pixelScale = 1f;
        private SwapChainFlags flags;
        private bool disposed;
        private FrameLatencyWaitHandle frameLatencyWaitHandle;
        private readonly HashSet<D3D11CommandList> referencedCLs = new HashSet<D3D11CommandList>();

        private bool allowTearing;

        private uint width;
        private uint height;

        private ID3D11Texture2D backBufferTexture;

        public D3D11Swapchain(D3D11GraphicsDevice gd, ref SwapchainDescription description)
        {
            this.gd = gd;
            this.description = description;
            depthFormat = description.DepthFormat;
            SyncToVerticalBlank = description.SyncToVerticalBlank;

            colorFormat = description.ColorSrgb
                ? Format.B8G8R8A8_UNorm_SRgb
                : Format.B8G8R8A8_UNorm;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory5 = this.gd.Adapter.GetParent(out IDXGIFactory5 f).Success ? f : null)
                canTear = dxgiFactory5?.PresentAllowTearing == true;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory3 = this.gd.Adapter.GetParent(out IDXGIFactory3 f).Success ? f : null)
                canCreateFrameLatencyWaitableObject = dxgiFactory3 != null;

            width = description.Width;
            height = description.Height;

            recreateSwapchain();
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                depthTexture?.Dispose();
                framebuffer.Dispose();
                DxgiSwapChain.Dispose();

                disposed = true;
            }
        }

        #endregion

        public override void Resize(uint width, uint height)
        {
            this.width = width;
            this.height = height;

            lock (referencedCLsLock)
            {
                foreach (var cl in referencedCLs) cl.Reset();

                referencedCLs.Clear();
            }

            bool resizeBuffers = false;

            if (framebuffer != null)
            {
                resizeBuffers = true;
                depthTexture?.Dispose();

                backBufferTexture.Dispose();
                framebuffer.Dispose();
            }

            uint actualWidth = (uint)(width * pixelScale);
            uint actualHeight = (uint)(height * pixelScale);
            if (resizeBuffers) DxgiSwapChain.ResizeBuffers(2, (int)actualWidth, (int)actualHeight, colorFormat, flags).CheckError();

            // Get the backbuffer from the swapchain
            backBufferTexture = DxgiSwapChain.GetBuffer<ID3D11Texture2D>(0);

            if (depthFormat != null)
            {
                var depthDesc = new TextureDescription(
                    actualWidth, actualHeight, 1, 1, 1,
                    depthFormat.Value,
                    TextureUsage.DepthStencil,
                    TextureType.Texture2D);
                depthTexture = new D3D11Texture(gd.Device, ref depthDesc);
            }

            var backBufferVdTexture = new D3D11Texture(
                backBufferTexture,
                TextureType.Texture2D,
                D3D11Formats.ToVdFormat(colorFormat));

            var desc = new FramebufferDescription(depthTexture, backBufferVdTexture);
            framebuffer = new D3D11Framebuffer(gd.Device, ref desc)
            {
                Swapchain = this
            };
        }

        public void WaitForNextFrameReady()
        {
            frameLatencyWaitHandle?.WaitOne(1000);
        }

        public void AddCommandListReference(D3D11CommandList cl)
        {
            lock (referencedCLsLock) referencedCLs.Add(cl);
        }

        public void RemoveCommandListReference(D3D11CommandList cl)
        {
            lock (referencedCLsLock) referencedCLs.Remove(cl);
        }

        private void recreateSwapchain()
        {
            DxgiSwapChain?.Release();
            DxgiSwapChain?.Dispose();
            DxgiSwapChain = null;

            framebuffer?.Dispose();
            framebuffer = null;

            depthTexture?.Dispose();
            depthTexture = null;

            frameLatencyWaitHandle?.Dispose();
            frameLatencyWaitHandle = null;

            // FlipDiscard is only supported on DXGI 1.4+
            bool canUseFlipDiscard;

            // previously we had an extension method ("GetParentOrNull") which makes this read a lot better,
            // but it resulted in AOT compilation crashes on iOS, therefore we can only do this inline.
            using (var dxgiFactory4 = gd.Adapter.GetParent(out IDXGIFactory4 f).Success ? f : null)
                canUseFlipDiscard = dxgiFactory4 != null;

            var swapEffect = canUseFlipDiscard ? SwapEffect.FlipDiscard : SwapEffect.Discard;

            flags = SwapChainFlags.None;

            if (AllowTearing && canTear)
                flags |= SwapChainFlags.AllowTearing;
            else if (canCreateFrameLatencyWaitableObject && canUseFlipDiscard)
                flags |= SwapChainFlags.FrameLatencyWaitableObject;

            if (description.Source is Win32SwapchainSource win32Source)
            {
                var dxgiScDesc = new SwapChainDescription
                {
                    BufferCount = 2,
                    Windowed = true,
                    BufferDescription = new ModeDescription(
                        (int)width, (int)height, colorFormat),
                    OutputWindow = win32Source.Hwnd,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = swapEffect,
                    BufferUsage = Usage.RenderTargetOutput,
                    Flags = flags
                };

                using (var dxgiFactory = gd.Adapter.GetParent<IDXGIFactory>())
                {
                    DxgiSwapChain = dxgiFactory.CreateSwapChain(gd.Device, dxgiScDesc);
                    dxgiFactory.MakeWindowAssociation(win32Source.Hwnd, WindowAssociationFlags.IgnoreAltEnter);
                }
            }
            else if (description.Source is UwpSwapchainSource uwpSource)
            {
                pixelScale = uwpSource.LogicalDpi / 96.0f;

                // Properties of the swap chain
                var swapChainDescription = new SwapChainDescription1
                {
                    AlphaMode = AlphaMode.Ignore,
                    BufferCount = 2,
                    Format = colorFormat,
                    Height = (int)(height * pixelScale),
                    Width = (int)(width * pixelScale),
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.FlipSequential,
                    BufferUsage = Usage.RenderTargetOutput,
                    Flags = flags
                };

                // Get the Vortice.DXGI factory automatically created when initializing the Direct3D device.
                using (var dxgiFactory = gd.Adapter.GetParent<IDXGIFactory2>())
                {
                    // Create the swap chain and get the highest version available.
                    using (var swapChain1 = dxgiFactory.CreateSwapChainForComposition(gd.Device, swapChainDescription)) DxgiSwapChain = swapChain1.QueryInterface<IDXGISwapChain2>();
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

            if ((flags & SwapChainFlags.FrameLatencyWaitableObject) > 0)
            {
                using (var swapChain2 = DxgiSwapChain.QueryInterfaceOrNull<IDXGISwapChain2>())
                {
                    if (swapChain2 != null)
                    {
                        swapChain2.MaximumFrameLatency = 1;
                        frameLatencyWaitHandle = new FrameLatencyWaitHandle(swapChain2.FrameLatencyWaitableObject);
                    }
                }
            }

            Resize(width, height);
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
