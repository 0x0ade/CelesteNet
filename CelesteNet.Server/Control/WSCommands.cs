using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.Helpers;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public partial class WSCommands {
        public readonly Dictionary<string, WSCMD> All = new Dictionary<string, WSCMD>();

        public WSCommands(FrontendWebSocket ws) {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(WSCMD).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                WSCMD cmd = (WSCMD) Activator.CreateInstance(type);
                cmd.WS = ws;
                Logger.Log(LogLevel.VVV, "wscmds", $"Found command: {cmd.ID.ToLowerInvariant()} ({type.FullName})");
                All[cmd.ID.ToLowerInvariant()] = cmd;
            }
        }

        public WSCMD Get(string id)
            => All.TryGetValue(id, out WSCMD cmd) ? cmd : null;

        public T Get<T>(string id) where T : WSCMD
            => All.TryGetValue(id, out WSCMD cmd) ? (T) cmd : null;
    }

    public abstract class WSCMD {
        public FrontendWebSocket WS;
        public Frontend Frontend => WS?.Frontend;
        public virtual string ID => GetType().Name.Substring(5);
        public virtual Type InputType { get; } = null;
        public abstract object Run(object input);
    }

    public abstract class WSCMD<TInput> : WSCMD {
        public override Type InputType => typeof(TInput);
        public override object Run(object input) {
            return Run((TInput) input);
        }
        public abstract object Run(TInput input);
    }
}
