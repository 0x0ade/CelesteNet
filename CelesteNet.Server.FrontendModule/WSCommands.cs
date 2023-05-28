using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCommands {
        public readonly Dictionary<string, WSCMD> All = new Dictionary<string, WSCMD>();

        public WSCommands(FrontendWebSocket ws) {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(WSCMD).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                WSCMD? cmd = (WSCMD?) Activator.CreateInstance(type);
                if (cmd == null)
                    throw new Exception($"Cannot create instance of WSCMD {type.FullName}");
                cmd.WS = ws;
                Logger.Log(LogLevel.VVV, "wscmds", $"Found command: {cmd.ID.ToLowerInvariant()} ({type.FullName})");
                All[cmd.ID.ToLowerInvariant()] = cmd;
            }
        }

        public WSCMD? Get(string id)
            => All.TryGetValue(id, out WSCMD? cmd) ? cmd : null;

        public T? Get<T>(string id) where T : WSCMD
            => All.TryGetValue(id, out WSCMD? cmd) ? (T) cmd : null;
    }

    public abstract class WSCMD {
#pragma warning disable CS8618 // Fully initialized after construction.
        public FrontendWebSocket WS;
#pragma warning restore CS8618
        public Frontend Frontend => WS.Frontend;
        public virtual string ID => GetType().Name.Substring(5);
        public virtual Type? InputType { get; } = null;
        public abstract bool MustAuth { get; }
        public virtual bool MustAuthExec => false;
        public abstract object? Run(object? input);
    }

    public abstract class WSCMD<TInput> : WSCMD {
        public override Type InputType => typeof(TInput);
        public override object? Run(object? input) {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            return Run((TInput) input);
        }
        public abstract object? Run(TInput input);
    }
}
