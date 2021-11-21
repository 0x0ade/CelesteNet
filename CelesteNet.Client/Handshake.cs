using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet.Client {
    public static class Handshake {

        public const int TeapotVersion = 1;
        public const int TeapotTimeout = 5000;

        // TODO MonoKickstart is so stupid, it can't even handle string.Split(char)...
        public static (int conToken, IConnectionFeature[] conFeatures, int maxPacketSize, float mergeWindow, float heartbeatInterval) DoTeapotHandshake(Socket sock, IConnectionFeature[] features, string nameKey) {
            // Find connection features
            // We don't buffer, as this causes some weirdness 
            // (either because we read packet data, or MonoKickstart decides to hang)
            using (NetworkStream netStream = new NetworkStream(sock, false))
            using (StreamReader reader = new StreamReader(netStream))
            using (StreamWriter writer = new StreamWriter(netStream)) {
                netStream.ReadTimeout = netStream.WriteTimeout = TeapotTimeout;

                // Send the "HTTP" request
                writer.Write($@"
CONNECT /teapot HTTP/1.1
CelesteNet-TeapotVersion: {TeapotVersion}
CelesteNet-ConnectionFeatures: {features.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}
CelesteNet-PlayerNameKey: {nameKey}

I want some tea!
".Trim().Replace("\n", "\r\n") + "\r\n");
                writer.Flush();

                // Read the "HTTP" response
                string statusLine = reader.ReadLine();
                string[] statusSegs = statusLine.Split(new[]{' '}, 3);
                if (statusSegs.Length != 3)
                    throw new InvalidDataException($"Invalid HTTP response status line: '{statusLine}'");
                int statusCode = int.Parse(statusSegs[1]);
                    
                Dictionary<string, string> headers = new Dictionary<string, string>();
                for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine()) {
                    string[] lineSegs = (line!).Split(new[]{':'}, 2).Select(s => s.Trim()).ToArray()!;
                    if (lineSegs.Length < 2)
                        throw new InvalidDataException($"Invalid HTTP header: '{line}'");
                    headers[lineSegs[0]] = lineSegs[1];
                }

                string content = "";
                for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
                    content += line + "\n";

                // Parse the "HTTP response"
                if (statusCode != 418)
                    throw new ConnectionErrorException($"Server rejected teapot handshake (status {statusCode})", content.Trim());
                
                int conToken = int.Parse(headers["CelesteNet-ConnectionToken"]);
                IConnectionFeature[] conFeatures = headers["CelesteNet-ConnectionFeatures"].Split(new[]{','}).Select(n => features.FirstOrDefault(f => f.GetType().FullName == n)).Where(f => f != null).ToArray();
                int maxPacketSize = int.Parse(headers["CelesteNet-MaxPacketSize"]);
                float mergeWindow = float.Parse(headers["CelesteNet-MergeWindow"]);
                float heartbeatInterval = float.Parse(headers["CelesteNet-HeartbeatInterval"]);

                return (conToken, conFeatures, maxPacketSize, mergeWindow, heartbeatInterval);
            }
        }

        public static void DoConnectionHandshake(CelesteNetConnection con, IConnectionFeature[] features) {
            // Handshake connection features
            foreach (IConnectionFeature feature in features)
                feature.Register(con, true);
            foreach (IConnectionFeature feature in features)
                feature.DoHandShake(con, true).Wait();
        }

    }
}