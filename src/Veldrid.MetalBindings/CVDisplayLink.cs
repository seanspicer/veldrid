// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;

namespace Veldrid.MetalBindings
{
    public struct CVDisplayLink
    {
        private const string CVFramework = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

        public readonly IntPtr NativePtr;
        public static implicit operator IntPtr(CVDisplayLink c) => c.NativePtr;

        public CVDisplayLink(IntPtr ptr) => NativePtr = ptr;

        public static CVDisplayLink CreateWithActiveCGDisplays()
        {
            CVDisplayLinkCreateWithActiveCGDisplays(out CVDisplayLink link);
            return link;
        }

        public void SetOutputCallback(CVDisplayLinkOutputCallbackDelegate callback, IntPtr userData)
        {
            CVDisplayLinkSetOutputCallback(this, callback, userData);
        }

        public void Start()
        {
            CVDisplayLinkStart(this);
        }

        public void Stop()
        {
            CVDisplayLinkStop(this);
        }

        public void Release()
        {
            CVDisplayLinkRelease(this);
        }

        [DllImport(CVFramework)]
        private static extern int CVDisplayLinkCreateWithActiveCGDisplays(out CVDisplayLink displayLink);

        [DllImport(CVFramework)]
        private static extern int CVDisplayLinkSetOutputCallback(CVDisplayLink displayLink, CVDisplayLinkOutputCallbackDelegate callback, IntPtr userData);

        [DllImport(CVFramework)]
        private static extern int CVDisplayLinkStart(CVDisplayLink displayLink);

        [DllImport(CVFramework)]
        private static extern int CVDisplayLinkStop(CVDisplayLink displayLink);

        [DllImport(CVFramework)]
        private static extern int CVDisplayLinkRelease(CVDisplayLink displayLink);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate int CVDisplayLinkOutputCallbackDelegate(CVDisplayLink displayLink, CVTimeStamp* inNow, CVTimeStamp* inOutputTime, long flagsIn, long flagsOut, IntPtr userData);
}
