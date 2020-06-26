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
    public class DataPartAudioState : DataType<DataPartAudioState> {

        public DataPartAudioTrackState? Music;
        public DataPartAudioTrackState? Ambience;

        public DataPartAudioState() {
        }

        public DataPartAudioState(AudioState state) {
            Music = state.Music == null ? null : new DataPartAudioTrackState(state.Music);
            Ambience = state.Ambience == null ? null : new DataPartAudioTrackState(state.Ambience);
        }

        public AudioState ToState()
            => new AudioState(Music?.ToState(), Ambience?.ToState());

        public override void Read(DataContext ctx, BinaryReader reader) {
            if (reader.ReadBoolean()) {
                Music = new DataPartAudioTrackState();
                Music.Read(ctx, reader);
            } else {
                Music = null;
            }

            if (reader.ReadBoolean()) {
                Ambience = new DataPartAudioTrackState();
                Ambience.Read(ctx, reader);
            } else {
                Ambience = null;
            }
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            if (Music != null) {
                writer.Write(true);
                Music.Write(ctx, writer);
            } else {
                writer.Write(false);
            }

            if (Ambience != null) {
                writer.Write(true);
                Ambience.Write(ctx, writer);
            } else {
                writer.Write(false);
            }
        }

    }
}
