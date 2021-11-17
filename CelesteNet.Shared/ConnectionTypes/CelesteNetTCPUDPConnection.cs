using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public const int CONNECTION_TIMEOUT = 3000;

        public readonly Socket TCPSocket;
        public readonly StringMap TCPStrings = new StringMap("TCP StringMap");
        private CelesteNetSendQueue tcpQueue;

        public CelesteNetTCPUDPConnection(DataContext data, Socket tcpSock, string uid, float mergeWindow, Action<CelesteNetSendQueue> tcpQueueFlusher) : base(data) {
            ID = $"TCP/UDP uid '{uid}' EP {tcpSock.RemoteEndPoint}";
            UID = uid;

            // Initialize TCP stuff
            TCPSocket = tcpSock;
            TCPSocket.ReceiveTimeout = TCPSocket.SendTimeout = CONNECTION_TIMEOUT;
            tcpQueue = new CelesteNetSendQueue(this, "TCP Queue", mergeWindow, tcpQueueFlusher);
        }

        protected override void Dispose(bool disposing) {
            try {
                TCPSocket.Shutdown(SocketShutdown.Both);
                TCPSocket.Close();
            } catch (SocketException) {}
            TCPSocket.Dispose();
            tcpQueue.Dispose();
            base.Dispose(disposing);
        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => tcpQueue;

        public override bool IsConnected => TCPSocket.Connected;

        public override string ID { get; }
        public override string UID { get; }

    }
}