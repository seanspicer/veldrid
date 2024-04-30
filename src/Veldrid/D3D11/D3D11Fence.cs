using System;
using System.Threading;

namespace Veldrid.D3D11
{
    internal class D3D11Fence : Fence
    {
        public ManualResetEvent ResetEvent { get; }

        public override bool Signaled => ResetEvent.WaitOne(0);
        public override bool IsDisposed => disposed;

        public override string Name { get; set; }
        private bool disposed;

        public D3D11Fence(bool signaled)
        {
            ResetEvent = new ManualResetEvent(signaled);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                ResetEvent.Dispose();
                disposed = true;
            }
        }

        #endregion

        public void Set()
        {
            ResetEvent.Set();
        }

        public override void Reset()
        {
            ResetEvent.Reset();
        }

        internal bool Wait(ulong nanosecondTimeout)
        {
            ulong timeout = Math.Min(int.MaxValue, nanosecondTimeout / 1_000_000);
            return ResetEvent.WaitOne((int)timeout);
        }
    }
}
