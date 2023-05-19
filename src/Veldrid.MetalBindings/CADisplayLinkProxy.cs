// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;

namespace Veldrid.MetalBindings
{
    public struct CADisplayLinkProxy
    {
        public readonly IntPtr NativePtr;
        public static implicit operator IntPtr(CADisplayLinkProxy c) => c.NativePtr;

        public CADisplayLinkProxy(IntPtr ptr) => NativePtr = ptr;

        public static CADisplayLinkProxy InitWithCallback(IntPtr callback)
        {
            return createDisplayLinkProxy_iOS(callback);
        }

        [DllImport("@rpath/metal_mono_workaround.framework/metal_mono_workaround", EntryPoint = "createDisplayLinkProxy")]
        private static extern CADisplayLinkProxy createDisplayLinkProxy_iOS(IntPtr callback);
    }
}
