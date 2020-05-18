using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class RCEndpoint {
        public string Path;
        public string PathHelp;
        public string PathExample;
        public string Name;
        public string Info;

        [JsonIgnore]
        public Action<Frontend, HttpRequestEventArgs> Handle;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RCEndpointAttribute : Attribute {

        public RCEndpoint Data { get; set; } = new RCEndpoint();

        public string Path {
            get => Data.Path;
            set => Data.Path = value;
        }

        public string PathHelp {
            get => Data.PathHelp;
            set => Data.PathHelp = value;
        }

        public string PathExample {
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

        public RCEndpointAttribute(string path, string pathHelp, string pathExample, string name, string info) {
            Path = path;
            PathHelp = pathHelp;
            PathExample = pathExample;
            Name = name;
            Info = info;
        }

    }
}
