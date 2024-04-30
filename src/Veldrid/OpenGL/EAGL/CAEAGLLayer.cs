using System;
using System.Runtime.InteropServices;
using Veldrid.MetalBindings;
using static Veldrid.MetalBindings.ObjectiveCRuntime;

namespace Veldrid.OpenGL.EAGL
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct CaeaglLayer
    {
        public readonly IntPtr NativePtr;

        public static CaeaglLayer New()
        {
            return MTLUtil.AllocInit<CaeaglLayer>("CAEAGLLayer");
        }

        public CGRect Frame
        {
            get => CGRect_objc_msgSend(NativePtr, sel_frame);
            set => objc_msgSend(NativePtr, sel_setFrame, value);
        }

        public Bool8 Opaque
        {
            get => bool8_objc_msgSend(NativePtr, sel_isOpaque);
            set => objc_msgSend(NativePtr, sel_setOpaque, value);
        }

        public void RemoveFromSuperlayer()
        {
            objc_msgSend(NativePtr, sel_removeFromSuperlayer);
        }

        public void Release()
        {
            release(NativePtr);
        }

        private static readonly Selector sel_frame = "frame";
        private static readonly Selector sel_setFrame = "setFrame:";
        private static readonly Selector sel_isOpaque = "isOpaque";
        private static readonly Selector sel_setOpaque = "setOpaque:";
        private static readonly Selector sel_removeFromSuperlayer = "removeFromSuperlayer";
    }
}
