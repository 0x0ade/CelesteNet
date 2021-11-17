using Celeste.Mod.CelesteNet.DataTypes;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public const int CONNECTION_TIMEOUT = 3000;

        private Socket tcpSocket;

        public CelesteNetTCPUDPConnection(DataContext data, Socket tcpSock, string uid) : base(data) {
            tcpSocket = tcpSock;
            tcpSocket.ReceiveTimeout = tcpSocket.SendTimeout = CONNECTION_TIMEOUT;
            ID = $"TCP/UDP uid '{uid}' EP {tcpSocket.RemoteEndPoint}";
            UID = uid;
        }


        protected override void Dispose(bool disposing) {
            tcpSocket.Close();
            tcpSocket.Close();
            base.Dispose(disposing);
        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => null;

        public override bool IsConnected => tcpSocket.Connected;

        public override string ID { get; }
        public override string UID { get; }

    }
}