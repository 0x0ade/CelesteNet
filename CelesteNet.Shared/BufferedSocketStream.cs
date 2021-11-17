using System;
using System.IO;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    /*
    This is a helper class primarly used by the new connection layer ConPlus. It
    doesn't do much, it just buffers some data in a buffer and sends that data
    to the underlying socket when the buffer's full. However, the reason there's
    a custom class for this is that this allows for the socket to change, making
    it possible to only use one stream per ConPlus worker
    - Popax21
    */
    public class BufferedSocketStream : Stream {

        private Socket? socket = null;
        private byte[] buffer;
        private int bufferOff = 0;

        public BufferedSocketStream(int bufferSize) {
            buffer = new byte[bufferSize];
        }

        public override void Write(byte[] buffer, int offset, int count) {
            if (socket == null)
                return;

            while (count > 0) {
                int n = Math.Min(buffer.Length - bufferOff, count);
                Buffer.BlockCopy(buffer, offset, this.buffer, this.bufferOff, n);
                this.bufferOff += n;
                count -= n;
                if (this.bufferOff >= this.buffer.Length)
                    Flush();
            }
        }

        public override void WriteByte(byte val) {
            if (socket == null)
                return;
            buffer[bufferOff++] = val;
            if (bufferOff >= buffer.Length)
                Flush();
        }

        public override void Flush() {
            if (bufferOff != 0)
                socket?.Send(buffer, bufferOff, SocketFlags.None);
            bufferOff = 0;
        }

        public Socket? Socket { 
            get => socket;
            set {
                Flush();
                socket = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new System.NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotImplementedException();
        public override void SetLength(long value) => throw new System.NotImplementedException();
        
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new System.NotImplementedException();
        public override long Position { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        
    }
}