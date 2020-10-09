using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public class RWLock : IDisposable {

        public readonly ReaderWriterLockSlim Inner = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly RLock _R;
        private readonly RULock _RU;
        private readonly WLock _W;

        public RWLock() {
            _R = new RLock(Inner);
            _RU = new RULock(Inner);
            _W = new WLock(Inner);
        }

        public RLock R() => _R.Start();
        public RULock RU() => _RU.Start();
        public WLock W() => _W.Start();

        public void Dispose() {
            Inner.Dispose();
        }

        public class RLock : IDisposable {
            public readonly ReaderWriterLockSlim Inner;

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
            public readonly ReaderWriterLockSlim Inner;

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
            public readonly ReaderWriterLockSlim Inner;

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
