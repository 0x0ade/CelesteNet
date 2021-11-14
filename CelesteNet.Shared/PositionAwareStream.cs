using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Monocle;

namespace Celeste.Mod.CelesteNet {
    public class PositionAwareStream : Stream {

        public readonly Stream Inner;
        private long _Position;

        public PositionAwareStream(Stream inner) {
            Inner = inner;
        }

        public void ResetPosition()
            => _Position = 0;

        public override bool CanRead => Inner.CanRead;

        public override bool CanSeek => Inner.CanSeek;

        public override bool CanWrite => Inner.CanWrite;

        public override long Length => Inner.Length;

        public override bool CanTimeout => Inner.CanTimeout;

        public override long Position {
            get => _Position;
            set => Inner.Position = _Position = value;
        }

        public override void Flush() {
            Inner.Flush();
        }

        public override int ReadByte() {
            int read = Inner.ReadByte();
            if (read >= 0)
                _Position++;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            int read = Inner.Read(buffer, offset, count);
            if (read > 0)
                _Position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) {
            switch (origin) {
                case SeekOrigin.Begin:
                    _Position = offset;
                    break;
                case SeekOrigin.Current:
                    _Position += offset;
                    break;
            }
            return Inner.Seek(offset, origin);
        }

        public override void SetLength(long value) {
            Inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count) {
            Inner.Write(buffer, offset, count);
            _Position += count;
        }

        public override void WriteByte(byte value) {
            Inner.WriteByte(value);
            _Position++;
        }

        public override void Close() {
            base.Close();
            Inner.Close();
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            Inner.Dispose();
        }

    }

    public class PositionAwareStream<T> : PositionAwareStream where T : Stream {

        public readonly new T Inner;

        public PositionAwareStream(T inner)
            : base(inner) {
            Inner = inner;
        }
    }
}
