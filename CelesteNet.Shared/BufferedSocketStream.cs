using System;
using System.IO;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    /*
    This is a helper class primarly used by the new connection layer ConPlus. It
    doesn't do much, it just buffers some data in a buffer and sends that data
    to the underlying socket when the buffer's full. However, the reason there's
    a custom class for this is that this allows for the socket to change, making
    it possible to only use one stream per ConPlus worker. Also MonoKickstart is
    extremly broken, and BufferedStream just kinda doesn't work...
    -Popax21
    */
    public class BufferedSocketStream : Stream {

        private Socket? _Socket = null;
        private readonly byte[] RecvBuffer, SendBuffer;
        private int RecvAvail = 0, RecvBufferOff = 0, SendBufferOff = 0;

        public BufferedSocketStream(int bufferSize) {
            RecvBuffer = new byte[bufferSize];
            SendBuffer = new byte[bufferSize];
        }

        public override int Read(byte[] buffer, int offset, int count) {
            lock (RecvBuffer) {
                if (_Socket == null)
                    return 0;

                int numRead = 0;
                while (numRead < count) {
                    if (RecvAvail <= 0 || RecvBufferOff >= RecvBuffer.Length) {
                        _Socket.Poll(-1, SelectMode.SelectRead);
                        RecvAvail = _Socket.Receive(RecvBuffer, RecvBuffer.Length, SocketFlags.None);
                        RecvBufferOff = 0;
                        if (RecvAvail <= 0)
                            break;
                    }

                    int n = Math.Min(RecvAvail, count-numRead);
                    Buffer.BlockCopy(RecvBuffer, RecvBufferOff, buffer, offset, n);
                    RecvBufferOff += n;
                    RecvAvail -= n;
                    offset += n;
                    numRead += n;
                }
                return numRead;
            }
        }

        public override void Write(byte[] buffer, int offset, int count) {
            lock (SendBuffer) {
                if (_Socket == null)
                    return;

                while (count > 0) {
                    int n = Math.Min(SendBuffer.Length - SendBufferOff, count);
                    Buffer.BlockCopy(buffer, offset, SendBuffer, SendBufferOff, n);
                    SendBufferOff += n;
                    count -= n;
                    if (SendBufferOff >= SendBuffer.Length)
                        Flush();
                }
            }
        }

        public override void WriteByte(byte val) {
            lock (SendBuffer) {
                if (_Socket == null)
                    return;
                SendBuffer[SendBufferOff++] = val;
                if (SendBufferOff >= SendBuffer.Length)
                    Flush();
            }
        }

        public override void Flush() {
            lock (SendBuffer) {
                if (SendBufferOff != 0 && _Socket != null) {
                    while (true) {
                        try {
                            _Socket.Send(SendBuffer, SendBufferOff, SocketFlags.None);
                            break;
                        } catch (SocketException se) {
                            if (se.SocketErrorCode == SocketError.TryAgain && _Socket.Poll(-1, SelectMode.SelectWrite))
                                continue;
                            throw;
                        }
                    }
                }
                SendBufferOff = 0;
            }
        }

        public Socket? Socket {
            get => _Socket;
            set {
                if (value?.Blocking ?? false)
                    throw new ArgumentException("Only non-blocking sockets are supported");
                lock (RecvBuffer)
                lock (SendBuffer)
                    RecvBufferOff = RecvAvail = SendBufferOff = 0;
                _Socket = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    }
}