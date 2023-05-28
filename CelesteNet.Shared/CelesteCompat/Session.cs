using System;
using System.Xml.Serialization;

namespace Celeste.Mod.CelesteNet {
    [Serializable]
    public class CelesteSession {
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
