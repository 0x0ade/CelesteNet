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
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public TcpClient TCP;
        public NetworkStream TCPStream;
        public BinaryReader TCPReader;
        public BinaryWriter TCPWriter;

        public UdpClient UDP;

        protected MemoryStream BufferStream;
        protected BinaryWriter BufferWriter;

        protected Thread ReadTCPThread;
        protected Thread ReadUDPThread;

        public override bool IsConnected => TCP?.Connected ?? false;

        private static TcpClient GetTCP(string host, int port) {
            TcpClient client = new TcpClient(host, port);

            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 6000);

            return client;
        }

        private static UdpClient GetUDP(string host, int port) {
            UdpClient client = new UdpClient(host, port);

            client.AllowNatTraversal(true);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 6000);

            return client;
        }

        public CelesteNetTCPUDPConnection(DataContext data, string host, int port)
            : this(data, GetTCP(host, port), GetUDP(host, port)) {
        }

        public CelesteNetTCPUDPConnection(DataContext data, TcpClient tcp, UdpClient udp)
            : base(data) {
            TCP = tcp;
            TCPStream = tcp.GetStream();
            TCPReader = new BinaryReader(TCPStream, Encoding.UTF8, true);
            TCPWriter = new BinaryWriter(TCPStream, Encoding.UTF8, true);

            UDP = udp;

            BufferStream = new MemoryStream();
            BufferWriter = new BinaryWriter(BufferStream, Encoding.UTF8);

            StartReadTCP();
            StartReadUDP();
        }

        public void StartReadTCP() {
            if (TCP == null || ReadTCPThread != null)
                return;

            ReadTCPThread = new Thread(ReadTCPLoop) {
                Name = $"{GetType().Name} ReadTCP ({Creator} - {GetHashCode()})",
                IsBackground = true
            };
            ReadTCPThread.Start();
        }

        public void StartReadUDP() {
            if (UDP == null || ReadUDPThread != null)
                return;

            ReadUDPThread = new Thread(ReadUDPLoop) {
                Name = $"{GetType().Name} ReadUDP ({Creator} - {GetHashCode()})",
                IsBackground = true
            };
            ReadUDPThread.Start();
        }

        public override void SendRaw(DataType data) {
            lock (BufferStream) {
                BufferStream.Seek(0, SeekOrigin.Begin);

                int length = Data.Write(BufferWriter, data);
                byte[] raw = BufferStream.GetBuffer();

                if ((data.DataFlags & DataFlags.Update) == DataFlags.Update) {
                    UDP.Send(raw, length);

                } else {
                    TCPWriter.Write(raw, 0, length);
                }
            }
        }

        protected virtual void ReadTCPLoop() {
            try {
                while ((TCP?.Connected ?? false) && IsAlive) {
                    Data.Handle(this, Data.Read(TCPReader));
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudpcon", $"TCP loop error:\n{this}\n{(e is IOException ? e.Message : e.ToString())}");
                ReadTCPThread = null;
                Dispose();
                return;
            }
        }

        protected virtual void ReadUDPLoop() {
            try {
                IPEndPoint remote = (IPEndPoint) UDP.Client.RemoteEndPoint;
                using (MemoryStream stream = new MemoryStream())
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8)) {
                    while (UDP != null && IsAlive) {
                        byte[] raw = UDP.Receive(ref remote);
                        BufferStream.Seek(0, SeekOrigin.Begin);
                        stream.Write(raw, 0, raw.Length);

                        BufferStream.Seek(0, SeekOrigin.Begin);
                        Data.Handle(this, Data.Read(reader));
                    }
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudpcon", $"UDP loop error:\n{this}\n{(e is IOException ? e.Message : e.ToString())}");
                ReadUDPThread = null;
                Dispose();
                return;
            }
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            TCPReader.Dispose();
            TCPWriter.Dispose();
            TCPStream.Dispose();
            TCP.Close();
            UDP?.Close();
            BufferWriter.Dispose();
            BufferStream.Dispose();
        }

        public override string ToString() {
            return $"CelesteNetTCPUDPConnection {TCP?.Client.LocalEndPoint?.ToString() ?? "???"} <-> {TCP?.Client.RemoteEndPoint?.ToString() ?? "???"} / {UDP?.Client.LocalEndPoint?.ToString() ?? "???"} <-> {UDP?.Client.RemoteEndPoint?.ToString() ?? "???"}";
        }

    }
}
