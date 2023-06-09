// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal unsafe class MTLCVDisplayLink : IMTLDisplayLink
    {
        public event Action Callback;

        private CVDisplayLink _displayLink;
        private CVDisplayLinkOutputCallbackDelegate _cvDisplayLinkCallbackHandler;

        public MTLCVDisplayLink()
        {
            _cvDisplayLinkCallbackHandler = OnCallback;
            _displayLink = CVDisplayLink.CreateWithActiveCGDisplays();
            _displayLink.SetOutputCallback(_cvDisplayLinkCallbackHandler, IntPtr.Zero);
            _displayLink.Start();
        }

        public void UpdateActiveMonitor(int x, int y, int w, int h)
        {
            _displayLink.UpdateActiveMonitor(x, y, w, h);
        }

        public double GetActualOutputVideoRefreshPeriod()
        {
            return _displayLink.GetActualOutputVideoRefreshPeriod();
        }

        private int OnCallback(CVDisplayLink displaylink, CVTimeStamp* innow, CVTimeStamp* inoutputtime, long flagsin, long flagsout, IntPtr userdata)
        {
            Callback?.Invoke();
            return 0;
        }

        public void Dispose()
        {
            _displayLink.Release();
        }
    }
}
