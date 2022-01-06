using System;
using System.IO;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public sealed class PacketDumper {
        public enum TransportType {
            None, TCP, UDP
        }

        private int nextDumpIdx = 0;

        public PacketDumper(CelesteNetServer server) {
            Server = server;
            if (!Directory.Exists(Server.Settings.PacketDumperDirectory))
                Directory.CreateDirectory(Server.Settings.PacketDumperDirectory);
        }

        public void DumpPacket(CelesteNetConnection con, TransportType transport, string descr, ReadOnlySpan<byte> rawData) {
            if (Server.Settings.PacketDumperMaxDumps <= 0)
                return;            

            int dumpIdx = Interlocked.Increment(ref nextDumpIdx);

            // Delete old dumps
            if (dumpIdx >= Server.Settings.PacketDumperMaxDumps) {
                int oldDumpIdx = dumpIdx - Server.Settings.PacketDumperMaxDumps;
                File.Delete($"{Server.Settings.PacketDumperDirectory}/{oldDumpIdx}.yaml");
                File.Delete($"{Server.Settings.PacketDumperDirectory}/{oldDumpIdx}.bin");
            }

            // Create dump
            using (FileStream stream = File.Open($"{Server.Settings.PacketDumperDirectory}/{dumpIdx}.yaml", FileMode.Create))
            using (TextWriter writer = new StreamWriter(stream))
                YamlHelper.Serializer.Serialize(writer, new {
                    Time = DateTime.Now,
                    TransportType = transport,
                    Description = descr
                });
            using (FileStream stream = File.Open($"{Server.Settings.PacketDumperDirectory}/{dumpIdx}.bin", FileMode.Create))
                stream.Write(rawData);
        }

        public CelesteNetServer Server { get; }
    }
}