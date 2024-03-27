using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;

namespace Celeste.Mod.CelesteNet
{
    public class CelesteNetBinaryBlobPartWriter : CelesteNetBinaryWriter {

        public readonly DataInternalBlob Blob;
        public readonly MemoryStream Stream;

        public int LastSplitPosition;

        public CelesteNetBinaryBlobPartWriter(DataContext ctx, DataInternalBlob blob, MemoryStream output)
            : base(ctx, null, null, output) {
            Blob = blob;
            Stream = output;
        }

        public CelesteNetBinaryBlobPartWriter(DataContext ctx, DataInternalBlob blob, MemoryStream output, bool leaveOpen)
            : base(ctx, null, null, output, leaveOpen) {
            Blob = blob;
            Stream = output;
        }

        public void SplitBytes() {
            Flush();

            int pos = (int) Stream.Position;
            if (LastSplitPosition == pos)
                return;

            DataInternalBlob.Part part = Blob.PartNext();
            part.Bytes = Stream.GetBuffer();
            part.BytesIndex = LastSplitPosition;
            part.BytesCount = pos - LastSplitPosition;
            LastSplitPosition = pos;
        }

        public override void WriteSizeDummy(int size) {
            SplitBytes();
            Blob.PartNext().SizeDummy = size;
        }

        public override void UpdateSizeDummy() {
            SplitBytes();
            Blob.PartNext().SizeDummy = int.MaxValue;
        }

        public override void WriteNetMappedString(string? text) {
            SplitBytes();
            Blob.PartNext().String = text ?? "";
        }

        public override bool TryGetSlimID(Type type, out int slimID) {
            slimID = -1;
            return false;
        }

    }
}