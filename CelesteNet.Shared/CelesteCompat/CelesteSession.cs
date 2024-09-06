using System;
using System.Xml.Serialization;

namespace Celeste.Mod.CelesteNet {
    [Serializable]
    public class CelesteSession {

        // Thankfully to deal with sessions on the server-side there's only the inner class Counter we need.
        // Nothing else from Celeste.Session has been copied, as you can see.

        [Serializable]
        public class Counter {
            [XmlAttribute("key")]
            public string Key;

            [XmlAttribute("value")]
            public int Value;
        }

        public enum CoreModes {
            None,
            Hot,
            Cold
        }
    }
}