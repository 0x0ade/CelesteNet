using System;
using System.Threading;

namespace Celeste.Mod.CelesteNet
{
    public class RWLock : IDisposable {

        private readonly ReaderWriterLockSlim Inner = new(LockRecursionPolicy.SupportsRecursion);
        private readonly RLock _R;
        private readonly RULock _RU;
        private readonly WLock _W;

        public RWLock() {
            _R = new(Inner);
            _RU = new(Inner);
            _W = new(Inner);
        }

        public RLock R() => _R.Start();
        public RULock RU() => _RU.Start();
        public WLock W() => _W.Start();

        public void Dispose() {
            Inner.Dispose();
        }

        public class RLock : IDisposable {
            private readonly ReaderWriterLockSlim Inner;

            public RLock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public RLock Start() {
                Inner.EnterReadLock();
                return this;
            }

            public void Dispose() {
                Inner.ExitReadLock();
            }
        }

        public class RULock : IDisposable {
            private readonly ReaderWriterLockSlim Inner;

            public RULock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public RULock Start() {
                Inner.EnterUpgradeableReadLock();
                return this;
            }

            public void Dispose() {
                Inner.ExitUpgradeableReadLock();
            }
        }

        public class WLock : IDisposable {
            private readonly ReaderWriterLockSlim Inner;

            public WLock(ReaderWriterLockSlim inner) {
                Inner = inner;
            }

            public WLock Start() {
                Inner.EnterWriteLock();
                return this;
            }

            public void Dispose() {
                Inner.ExitWriteLock();
            }
        }

    }
}
