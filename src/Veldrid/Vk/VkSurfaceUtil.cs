using System;
using Veldrid.Android;
using Veldrid.MetalBindings;
using Vulkan;
using Vulkan.Android;
using Vulkan.Wayland;
using Vulkan.Xlib;
using static Vulkan.VulkanNative;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal static unsafe class VkSurfaceUtil
    {
        internal static VkSurfaceKHR CreateSurface(VkGraphicsDevice gd, VkInstance instance, SwapchainSource swapchainSource)
        {
            // TODO a null GD is passed from VkSurfaceSource.CreateSurface for compatibility
            //      when VkSurfaceInfo is removed we do not have to handle gd == null anymore
            bool doCheck = gd != null;

            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME}");

            switch (swapchainSource)
            {
                case XlibSwapchainSource xlibSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_XLIB_SURFACE_EXTENSION_NAME}");
                    return CreateXlib(instance, xlibSource);

                case WaylandSwapchainSource waylandSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WAYLAND_SURFACE_EXTENSION_NAME}");
                    return CreateWayland(instance, waylandSource);

                case Win32SwapchainSource win32Source:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_WIN32_SURFACE_EXTENSION_NAME}");
                    return CreateWin32(instance, win32Source);

                case AndroidSurfaceSwapchainSource androidSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VK_KHR_ANDROID_SURFACE_EXTENSION_NAME}");
                    return CreateAndroidSurface(instance, androidSource);

                case NSWindowSwapchainSource nsWindowSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME))
                            return CreateNSWindowSurface(gd, instance, nsWindowSource, hasMetalExtension);
                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME}");
                    }

                    return CreateNSWindowSurface(gd, instance, nsWindowSource, false);

                case NSViewSwapchainSource nsViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME))
                            return CreateNSViewSurface(gd, instance, nsViewSource, hasMetalExtension);
                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME}");
                    }

                    return CreateNSViewSurface(gd, instance, nsViewSource, false);

                case UIViewSwapchainSource uiViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VK_EXT_METAL_SURFACE_EXTENSION_NAME);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME))
                            return CreateUIViewSurface(gd, instance, uiViewSource, hasMetalExtension);
                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VK_MVK_MACOS_SURFACE_EXTENSION_NAME}, {CommonStrings.VK_MVK_IOS_SURFACE_EXTENSION_NAME}");
                    }

                    return CreateUIViewSurface(gd, instance, uiViewSource, false);

                default:
                    throw new VeldridException("The provided SwapchainSource cannot be used to create a Vulkan surface.");
            }
        }

        private static VkSurfaceKHR CreateWin32(VkInstance instance, Win32SwapchainSource win32Source)
        {
            var surfaceCI = VkWin32SurfaceCreateInfoKHR.New();
            surfaceCI.hwnd = win32Source.Hwnd;
            surfaceCI.hinstance = win32Source.Hinstance;
            var result = vkCreateWin32SurfaceKHR(instance, ref surfaceCI, null, out var surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateXlib(VkInstance instance, XlibSwapchainSource xlibSource)
        {
            var xsci = VkXlibSurfaceCreateInfoKHR.New();
            xsci.dpy = (Display*)xlibSource.Display;
            xsci.window = new Window { Value = xlibSource.Window };
            var result = vkCreateXlibSurfaceKHR(instance, ref xsci, null, out var surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateWayland(VkInstance instance, WaylandSwapchainSource waylandSource)
        {
            var wsci = VkWaylandSurfaceCreateInfoKHR.New();
            wsci.display = (wl_display*)waylandSource.Display;
            wsci.surface = (wl_surface*)waylandSource.Surface;
            var result = vkCreateWaylandSurfaceKHR(instance, ref wsci, null, out var surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateAndroidSurface(VkInstance instance, AndroidSurfaceSwapchainSource androidSource)
        {
            IntPtr aNativeWindow = AndroidRuntime.ANativeWindow_fromSurface(androidSource.JniEnv, androidSource.Surface);

            var androidSurfaceCI = VkAndroidSurfaceCreateInfoKHR.New();
            androidSurfaceCI.window = (ANativeWindow*)aNativeWindow;
            var result = vkCreateAndroidSurfaceKHR(instance, ref androidSurfaceCI, null, out var surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR CreateNSWindowSurface(VkGraphicsDevice gd, VkInstance instance, NSWindowSwapchainSource nsWindowSource, bool hasExtMetalSurface)
        {
            var nswindow = new NSWindow(nsWindowSource.NSWindow);
            return CreateNSViewSurface(gd, instance, new NSViewSwapchainSource(nswindow.contentView), hasExtMetalSurface);
        }

        private static VkSurfaceKHR CreateNSViewSurface(VkGraphicsDevice gd, VkInstance instance, NSViewSwapchainSource nsViewSource, bool hasExtMetalSurface)
        {
            var contentView = new NSView(nsViewSource.NSView);

            if (!CAMetalLayer.TryCast(contentView.layer, out var metalLayer))
            {
                metalLayer = CAMetalLayer.New();
                contentView.wantsLayer = true;
                contentView.layer = metalLayer.NativePtr;
            }

            if (hasExtMetalSurface)
            {
                var surfaceCI = new VkMetalSurfaceCreateInfoEXT();
                surfaceCI.sType = VkMetalSurfaceCreateInfoEXT.VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT;
                surfaceCI.pLayer = metalLayer.NativePtr.ToPointer();
                VkSurfaceKHR surface;
                var result = gd.CreateMetalSurfaceEXT(instance, &surfaceCI, null, &surface);
                CheckResult(result);
                return surface;
            }
            else
            {
                var surfaceCI = VkMacOSSurfaceCreateInfoMVK.New();
                surfaceCI.pView = contentView.NativePtr.ToPointer();
                var result = vkCreateMacOSSurfaceMVK(instance, ref surfaceCI, null, out var surface);
                CheckResult(result);
                return surface;
            }
        }

        private static VkSurfaceKHR CreateUIViewSurface(VkGraphicsDevice gd, VkInstance instance, UIViewSwapchainSource uiViewSource, bool hasExtMetalSurface)
        {
            var uiView = new UIView(uiViewSource.UIView);

            if (!CAMetalLayer.TryCast(uiView.layer, out var metalLayer))
            {
                metalLayer = CAMetalLayer.New();
                metalLayer.frame = uiView.frame;
                metalLayer.opaque = true;
                uiView.layer.addSublayer(metalLayer.NativePtr);
            }

            if (hasExtMetalSurface)
            {
                var surfaceCI = new VkMetalSurfaceCreateInfoEXT();
                surfaceCI.sType = VkMetalSurfaceCreateInfoEXT.VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT;
                surfaceCI.pLayer = metalLayer.NativePtr.ToPointer();
                VkSurfaceKHR surface;
                var result = gd.CreateMetalSurfaceEXT(instance, &surfaceCI, null, &surface);
                CheckResult(result);
                return surface;
            }
            else
            {
                var surfaceCI = VkIOSSurfaceCreateInfoMVK.New();
                surfaceCI.pView = uiView.NativePtr.ToPointer();
                var result = vkCreateIOSSurfaceMVK(instance, ref surfaceCI, null, out var surface);
                return surface;
            }
        }
    }
}
