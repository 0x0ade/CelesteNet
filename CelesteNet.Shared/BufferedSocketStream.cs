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

        private Socket? socket = null;
        private byte[] recvBuffer, sendBuffer;
        private int recvAvail = 0, recvBufferOff = 0, sendBufferOff = 0;

        public BufferedSocketStream(int bufferSize) {
            recvBuffer = new byte[bufferSize];
            sendBuffer = new byte[bufferSize];
        }

        public override int Read(byte[] buffer, int offset, int count) {
            lock (recvBuffer) {
                if (socket == null)
                    return 0;

                int numRead = 0;
                while (numRead < count) {
                    if (recvAvail <= 0 || recvBufferOff >= recvBuffer.Length) {
                        socket.Poll(-1, SelectMode.SelectRead);
                        recvAvail = socket.Receive(recvBuffer, recvBuffer.Length, SocketFlags.None);
                        recvBufferOff = 0;
                        if (recvAvail <= 0)
                            break;
                    }

                    int n = Math.Min(recvAvail, count-numRead);
                    Buffer.BlockCopy(recvBuffer, recvBufferOff, buffer, offset, n);
                    recvBufferOff += n;
                    recvAvail -= n;
                    offset += n;
                    numRead += n;
                }
                return numRead;
            }
        }

        public override void Write(byte[] buffer, int offset, int count) {
            lock (sendBuffer) {
                if (socket == null)
                    return;

                while (count > 0) {
                    int n = Math.Min(sendBuffer.Length - sendBufferOff, count);
                    Buffer.BlockCopy(buffer, offset, sendBuffer, sendBufferOff, n);
                    sendBufferOff += n;
                    count -= n;
                    if (sendBufferOff >= sendBuffer.Length)
                        Flush();
                }
            }
        }

        public override void WriteByte(byte val) {
            lock (sendBuffer) {
                if (socket == null)
                    return;
                sendBuffer[sendBufferOff++] = val;
                if (sendBufferOff >= sendBuffer.Length)
                    Flush();
            }
        }

        public override void Flush() {
            lock (sendBuffer) {
                if (sendBufferOff != 0 && socket != null) {
                    while (true) {
                        try {
                            socket.Send(sendBuffer, sendBufferOff, SocketFlags.None);
                            break;
                        } catch (SocketException se) {
                            if (se.SocketErrorCode == SocketError.TryAgain && socket.Poll(-1, SelectMode.SelectWrite))
                                continue;
                            throw;
                        }
                    }
                }
                sendBufferOff = 0;
            }
        }

        public Socket? Socket { 
            get => socket;
            set {
                if (value?.Blocking ?? false)
                    throw new ArgumentException("Only non-blocking sockets are supported");
                lock (recvBuffer)
                lock (sendBuffer)
                    recvBufferOff = recvAvail = sendBufferOff = 0;
                socket = value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotImplementedException();
        public override void SetLength(long value) => throw new System.NotImplementedException();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new System.NotImplementedException();
        public override long Position { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

    }
}