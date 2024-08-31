namespace Celeste.Mod.CelesteNet.DataTypes
{
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

        protected override void Read(CelesteNetBinaryReader reader) {
            Event = reader.ReadNetString();
            Progress = reader.ReadInt32();

            Parameters = new MEP[reader.ReadByte()];
            for (int i = 0; i < Parameters.Length; i++)
                Parameters[i] = new(reader.ReadNetString(), reader.ReadSingle());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
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
