using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Net;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public const int CONNECTION_TIMEOUT = 3000;

        private Socket tcpSock;
        private EndPoint? udpEP;
        private CelesteNetSendQueue tcpQueue, udpQueue;
        private byte udpRecvContainerCounter = 0, udpSendContainerCounter = 0;

        public Socket TCPSocket => tcpSock;
        public EndPoint? UDPEndpoint => udpEP;
        public readonly OptMap<string> Strings = new OptMap<string>("StringMap");
        public readonly OptMap<Type> SlimMap = new OptMap<Type>("SlimMap");

        public CelesteNetTCPUDPConnection(DataContext data, Socket tcpSock, string uid, int maxQueueSize, float mergeWindow, Action<CelesteNetSendQueue> tcpQueueFlusher, Action<CelesteNetSendQueue> udpQueueFlusher) : base(data) {
            ID = $"TCP/UDP uid '{uid}' EP {tcpSock.RemoteEndPoint}";
            UID = uid;

            // Initialize networking stuff
            this.tcpSock = tcpSock;
            this.tcpSock.ReceiveTimeout = this.tcpSock.SendTimeout = CONNECTION_TIMEOUT;
            tcpQueue = new CelesteNetSendQueue(this, "TCP Queue", maxQueueSize, mergeWindow, tcpQueueFlusher);
            udpQueue = new CelesteNetSendQueue(this, "UDP Queue", maxQueueSize, mergeWindow, udpQueueFlusher);
        }

        protected override void Dispose(bool disposing) {
            udpEP = null;
            tcpQueue.Dispose();
            udpQueue.Dispose();
            try {
                tcpSock.Shutdown(SocketShutdown.Both);
                tcpSock.Close();
            } catch (SocketException) {}
            tcpSock.Dispose();
            base.Dispose(disposing);
        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (udpEP != null && (data.DataFlags & DataFlags.Unreliable) != 0) ? udpQueue : tcpQueue;

        public byte NextUDPContainerID() {
            if (udpEP == null)
                throw new InvalidOperationException("Connection doesn't have a UDP tunnel");
            byte id = udpSendContainerCounter;
            udpSendContainerCounter = unchecked(udpSendContainerCounter++);
            return id;
        }

        public override bool IsConnected => tcpSock.Connected;

        public override string ID { get; }
        public override string UID { get; }

    }
}