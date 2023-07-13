using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Celeste.Mod.CelesteNet {

    [Serializable]
    public class CelesteAudioState {
        public static string[] LayerParameters = new string[10] { "layer0", "layer1", "layer2", "layer3", "layer4", "layer5", "layer6", "layer7", "layer8", "layer9" };

        public CelesteAudioTrackState Music = new CelesteAudioTrackState();

        public CelesteAudioTrackState Ambience = new CelesteAudioTrackState();

        public float? AmbienceVolume;

        public CelesteAudioState() {
        }

        public CelesteAudioState(CelesteAudioTrackState music, CelesteAudioTrackState ambience) {
            if (music != null) {
                Music = music.Clone();
            }

            if (ambience != null) {
                Ambience = ambience.Clone();
            }
        }

        public CelesteAudioState(string music, string ambience) {
            Music.Event = music;
            Ambience.Event = ambience;
        }

        public CelesteAudioState Clone() {
            CelesteAudioState audioState = orig_Clone();
            audioState.AmbienceVolume = AmbienceVolume;
            return audioState;
        }

        public CelesteAudioState orig_Clone() {
            return new CelesteAudioState {
                Music = Music.Clone(),
                Ambience = Ambience.Clone()
            };
        }
    }

    [Serializable]
    public class CelesteAudioTrackState {
        [XmlIgnore]
        private string ev;

        public List<CelesteMEP> Parameters = new List<CelesteMEP>();

        [XmlAttribute]
        public string Event {
            get {
                return ev;
            }
            set {
                if (ev != value) {
                    ev = value;
                    Parameters.Clear();
                }
            }
        }

        [XmlIgnore]
        public int Progress {
            get {
                return (int)GetParam("progress");
            }
            set {
                Param("progress", value);
            }
        }

        public CelesteAudioTrackState() {
        }

        public CelesteAudioTrackState(string ev) {
            Event = ev;
        }

        public CelesteAudioTrackState Layer(int index, float value) {
            return Param(CelesteAudioState.LayerParameters[index], value);
        }

        public CelesteAudioTrackState Layer(int index, bool value) {
            return Param(CelesteAudioState.LayerParameters[index], value);
        }

        public CelesteAudioTrackState SetProgress(int value) {
            Progress = value;
            return this;
        }

        public CelesteAudioTrackState Param(string key, float value) {
            foreach (CelesteMEP parameter in Parameters) {
                if (parameter.Key != null && parameter.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase)) {
                    parameter.Value = value;
                    return this;
                }
            }

            Parameters.Add(new CelesteMEP(key, value));
            return this;
        }

        public CelesteAudioTrackState Param(string key, bool value) {
            return Param(key, value ? 1 : 0);
        }

        public float GetParam(string key) {
            foreach (CelesteMEP parameter in Parameters) {
                if (parameter.Key != null && parameter.Key.Equals(key, StringComparison.InvariantCultureIgnoreCase)) {
                    return parameter.Value;
                }
            }

            return 0f;
        }

        public CelesteAudioTrackState Clone() {
            CelesteAudioTrackState audioTrackState = new CelesteAudioTrackState();
            audioTrackState.Event = Event;
            foreach (CelesteMEP parameter in Parameters) {
                audioTrackState.Parameters.Add(new CelesteMEP(parameter.Key, parameter.Value));
            }

            return audioTrackState;
        }
    }

    [Serializable]
    public class CelesteMEP {
        [XmlAttribute]
        public string Key;

        [XmlAttribute]
        public float Value;

        public CelesteMEP() {
        }

        public CelesteMEP(string key, float value) {
            Key = key;
            Value = value;
        }
    }
}
