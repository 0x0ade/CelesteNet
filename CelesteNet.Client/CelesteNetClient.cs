using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClient : IDisposable {

        public readonly CelesteNetClientSettings Settings;

        public readonly DataContext Data;

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;

                _IsAlive = value;
            }
        }

        public CelesteNetClient()
            : this(new CelesteNetClientSettings()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new DataContext();
        }

        public void Start() {
            Logger.Log(LogLevel.CRI, "main", $"Startup");
            IsAlive = true;
        }

        public void Dispose() {
            Logger.Log(LogLevel.CRI, "main", "Shutdown");
        }

    }
}
