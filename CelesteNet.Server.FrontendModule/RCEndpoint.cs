using Newtonsoft.Json;
using System;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class RCEndpoint {
        public bool Auth;
        public string Path = "";
        public string? PathHelp;
        public string? PathExample;
        public string Name = "";
        public string Info = "";

        [JsonIgnore]
        public Action<Frontend, HttpRequestEventArgs> Handle = (f, c) => { };
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RCEndpointAttribute : Attribute {

        public RCEndpoint Data { get; set; } = new();

        public bool Auth {
            get => Data.Auth;
            set => Data.Auth = value;
        }

        public string Path {
            get => Data.Path;
            set => Data.Path = value;
        }

        public string? PathHelp {
            get => Data.PathHelp;
            set => Data.PathHelp = value;
        }

        public string? PathExample {
            get => Data.PathExample;
            set => Data.PathExample = value;
        }

        public string Name {
            get => Data.Name;
            set => Data.Name = value;
        }

        public string Info {
            get => Data.Info;
            set => Data.Info = value;
        }

        public RCEndpointAttribute() {
        }

        public RCEndpointAttribute(bool auth, string path, string? pathHelp, string? pathExample, string name, string info) {
            Auth = auth;
            Path = path;
            PathHelp = pathHelp;
            PathExample = pathExample;
            Name = name;
            Info = info;
        }

    }
}
