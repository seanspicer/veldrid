// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal class MTLCADisplayLink : IMTLDisplayLink
    {
        public event Action Callback;

        private static readonly Dictionary<IntPtr, MTLCADisplayLink> s_aotDisplayLinks = new Dictionary<IntPtr, MTLCADisplayLink>();

        private readonly CADisplayLinkProxy _displayLink;
        private readonly ProxyCallbackDelegate _callbackDelegate;
        private readonly IntPtr _callbackDelegateFuncPtr;

        public MTLCADisplayLink()
        {
            _callbackDelegate = OnCallback;
            _callbackDelegateFuncPtr = Marshal.GetFunctionPointerForDelegate<ProxyCallbackDelegate>(_callbackDelegate);
            _displayLink = CADisplayLinkProxy.InitWithCallback(_callbackDelegateFuncPtr);

            lock (s_aotDisplayLinks)
            {
                s_aotDisplayLinks.Add(_displayLink.NativePtr, this);
            }
        }

        // Xamarin AOT requires native callbacks be static.
        [MonoPInvokeCallback(typeof(ProxyCallbackDelegate))]
        private static void OnCallback(IntPtr proxy)
        {
            lock (s_aotDisplayLinks)
            {
                if (s_aotDisplayLinks.TryGetValue(proxy, out MTLCADisplayLink displayLink))
                {
                    displayLink.Callback?.Invoke();
                }
            }
        }

        public void Dispose()
        {
            lock (s_aotDisplayLinks)
            {
                s_aotDisplayLinks.Remove(_displayLink.NativePtr);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ProxyCallbackDelegate(IntPtr proxy);
    }
}
