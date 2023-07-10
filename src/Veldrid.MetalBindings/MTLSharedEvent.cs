// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using static Veldrid.MetalBindings.ObjectiveCRuntime;

namespace Veldrid.MetalBindings
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MTLSharedEvent
    {
        public readonly IntPtr NativePtr;
        public MTLSharedEvent(IntPtr ptr) => NativePtr = ptr;

        public ulong signaledValue
        {
            get => objc_msgSend<ulong>(NativePtr, sel_signaledValue);
            set => objc_msgSend(NativePtr, sel_setSignaledValue, value);
        }

        private static readonly Selector sel_signaledValue = "signaledValue";
        private static readonly Selector sel_setSignaledValue = "setSignaledValue:";
    }
}
