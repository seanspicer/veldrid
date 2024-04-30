using System;
using System.Runtime.InteropServices;
using Veldrid.MetalBindings;
using static Veldrid.MetalBindings.ObjectiveCRuntime;

namespace Veldrid.OpenGL.EAGL
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EaglContext
    {
        private static ObjCClass s_class = new ObjCClass("EAGLContext");

        public readonly IntPtr NativePtr;

        public Bool8 RenderBufferStorage(UIntPtr target, IntPtr drawable)
        {
            return bool8_objc_msgSend(NativePtr, sel_renderBufferStorage, target, drawable);
        }

        public Bool8 PresentRenderBuffer(UIntPtr target)
        {
            return bool8_objc_msgSend(NativePtr, sel_presentRenderBuffer, target);
        }

        public static EaglContext Create(EaglRenderingAPI api)
        {
            var ret = s_class.Alloc<EaglContext>();
            objc_msgSend(ret.NativePtr, sel_initWithAPI, (uint)api);
            return ret;
        }

        public static Bool8 SetCurrentContext(IntPtr context)
        {
            return bool8_objc_msgSend(s_class, sel_setCurrentContext, context);
        }

        public static EaglContext CurrentContext
            => objc_msgSend<EaglContext>(s_class, sel_currentContext);

        public void Release()
        {
            release(NativePtr);
        }

        private static readonly Selector sel_initWithAPI = "initWithAPI:";
        private static readonly Selector sel_setCurrentContext = "setCurrentContext:";
        private static readonly Selector sel_renderBufferStorage = "renderbufferStorage:fromDrawable:";
        private static readonly Selector sel_presentRenderBuffer = "presentRenderbuffer:";
        private static readonly Selector sel_currentContext = "currentContext";
    }

    internal enum EaglRenderingAPI
    {
        OpenGLES1 = 1,
        OpenGLES2 = 2,
        OpenGLES3 = 3
    }
}
