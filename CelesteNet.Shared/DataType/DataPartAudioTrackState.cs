using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPartAudioTrackState : DataType<DataPartAudioTrackState> {

        public string Event = "";
        public int Progress;
        public MEP[] Parameters = Dummy<MEP>.EmptyArray;

        public DataPartAudioTrackState() {
        }

        public DataPartAudioTrackState(AudioTrackState state) {
            Event = state.Event;
            Progress = state.Progress;
            Parameters = state.Parameters.ToArray();
        }

        public AudioTrackState ToState()
            => new(Event) {
                Progress = Progress,
                Parameters = new(Parameters)
            };

        public override void Read(CelesteNetBinaryReader reader) {
            Event = reader.ReadNetString();
            Progress = reader.ReadInt32();

            Parameters = new MEP[reader.ReadByte()];
            for (int i = 0; i < Parameters.Length; i++)
                Parameters[i] = new(reader.ReadNetString(), reader.ReadSingle());
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Event);
            writer.Write(Progress);

            writer.Write((byte) Parameters.Length);
            foreach (MEP param in Parameters) {
                writer.WriteNetString(param.Key);
                writer.Write(param.Value);
            }
        }

    }
}
