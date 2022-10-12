namespace Celeste.Mod.CelesteNet {

    public class CommandInfo {
        public string ID = "";
        public string AliasTo = "";
        public bool Auth = false;
        public bool AuthExec = false;
        public CompletionType FirstArg = CompletionType.None;
    }

    public enum CompletionType : byte {
        None = 0,
        Command = 1,
        Channel = 2,
        Player = 3,
        Emote = 4
    }
}
