namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPartAudioState : DataType<DataPartAudioState> {

        public DataPartAudioTrackState? Music;
        public DataPartAudioTrackState? Ambience;

        public DataPartAudioState() {
        }

        public DataPartAudioState(CelesteAudioState state) {
            Music = state.Music == null ? null : new DataPartAudioTrackState(state.Music);
            Ambience = state.Ambience == null ? null : new DataPartAudioTrackState(state.Ambience);
        }

        public CelesteAudioState ToState()
            => new(Music?.ToState(), Ambience?.ToState());

        protected override void Read(CelesteNetBinaryReader reader) {
            if (reader.ReadBoolean()) {
                Music = new();
                Music.ReadAll(reader);
            } else {
                Music = null;
            }

            if (reader.ReadBoolean()) {
                Ambience = new();
                Ambience.ReadAll(reader);
            } else {
                Ambience = null;
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            if (Music != null) {
                writer.Write(true);
                Music.WriteAll(writer);
            } else {
                writer.Write(false);
            }

            if (Ambience != null) {
                writer.Write(true);
                Ambience.WriteAll(writer);
            } else {
                writer.Write(false);
            }
        }

    }
}
