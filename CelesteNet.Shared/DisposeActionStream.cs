using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet
{
    // Based off of Everest's DisposeActionStream.
    public sealed class DisposeActionStream : Stream {

        // I'm overcomplicating this. -ade

        public readonly Stream Inner;

        public readonly Action Action;

        public DisposeActionStream(Stream inner, Action action) {
            Inner = inner;
            Action = action;
        }

        public override bool CanRead => Inner.CanRead;

        public override bool CanSeek => Inner.CanSeek;

        public override bool CanWrite => Inner.CanWrite;

        public override long Length => Inner.Length;

        public override long Position {
            get => Inner.Position;

            set => Inner.Position = value;
        }

        public override void Flush() => Inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);

        public override void SetLength(long value) => Inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);

        public override IAsyncResult BeginRead(byte[] array, int offset, int count, AsyncCallback? callback, object? state) => Inner.BeginRead(array, offset, count, callback, state);

        public override int EndRead(IAsyncResult asyncResult) => Inner.EndRead(asyncResult);

        public override int ReadByte() => Inner.ReadByte();

        public override IAsyncResult BeginWrite(byte[] array, int offset, int count, AsyncCallback? callback, object? state) => Inner.BeginWrite(array, offset, count, callback, state);

        public override void EndWrite(IAsyncResult asyncResult) => Inner.EndWrite(asyncResult);

        public override void WriteByte(byte value) => Inner.WriteByte(value);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken) => Inner.FlushAsync(cancellationToken);

        public override void Close() {
            Action?.Invoke();
            Inner.Close();
        }

        protected override void Dispose(bool disposing) {
            Action?.Invoke();
            Inner.Dispose();
        }

    }
}
