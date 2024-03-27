using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Client {
    public static class Handshake {

        private static string ReadLine(this NetworkStream netStream) {
            //Unbuffered "read line" implementation reading every byte one at a time
            //Extremely slow and inefficient, but otherwise we may gobble up binary packet bytes by accident :catresort:
            List<char> lineChars = new List<char>();
            while (true) {
                int b = netStream.ReadByte();
                if (b < 0)
                    throw new EndOfStreamException();
                else if (b == '\n')
                    break;
                else
                    lineChars.Add((char)b);
            }
            if (lineChars.Count > 0 && lineChars[^1] == '\r') lineChars.RemoveAt(lineChars.Count - 1);
            return new string(CollectionsMarshal.AsSpan(lineChars));
        }

        // TODO MonoKickstart is so stupid, it can't even handle string.Split(char)...
        public static Tuple<uint, IConnectionFeature[], T> DoTeapotHandshake<T>(Socket sock, IConnectionFeature[] features, string nameKey, CelesteNetClientOptions options) where T : new() {
            // Find connection features
            // We don't buffer, as we could read actual packet data
            using NetworkStream netStream = new(sock, false);

            using StreamWriter writer = new(netStream);
            // Send the "HTTP" request
            StringBuilder reqBuilder = new($@"
TEAREQ /teapot HTTP/4.2
Connection: keep-alive
CelesteNet-TeapotVersion: {CelesteNetUtils.LoadedVersion}
CelesteNet-ConnectionFeatures: {features.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}
CelesteNet-PlayerNameKey: {nameKey}
");

            foreach (FieldInfo field in typeof(CelesteNetClientOptions).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                switch (Type.GetTypeCode(field.FieldType)) {
                    case TypeCode.Boolean:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                    case TypeCode.Single:
                    case TypeCode.Double: {
                        reqBuilder.AppendLine($"CelesteNet-ClientOptions-{field.Name}: {field.GetValue(options)}");
                    } break;
                }
            }

            writer.Write(reqBuilder.ToString().Trim().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n\r\n");
            writer.Flush();

            // Read the "HTTP" response
            string statusLine = netStream.ReadLine();
            string[] statusSegs = statusLine.Split(new[] { ' ' }, 3);
            if (statusSegs.Length != 3)
                throw new InvalidDataException($"Invalid HTTP response status line: '{statusLine}'");
            int statusCode = int.Parse(statusSegs[1]);

            Dictionary<string, string> headers = new();
            for (string line = netStream.ReadLine(); !string.IsNullOrEmpty(line); line = netStream.ReadLine()) {
                int split = line.IndexOf(':');
                if (split == -1)
                    throw new InvalidDataException($"Invalid HTTP header: '{line}'");
                headers[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }

            string content = "";
            for (string line = netStream.ReadLine(); !string.IsNullOrEmpty(line); line = netStream.ReadLine())
                content += line + "\n";

            // Parse the "HTTP response"
            if (statusCode != 418)
                throw new ConnectionErrorCodeException($"Server rejected teapot handshake (status {statusCode})", statusCode, content.Trim());

            uint conToken = uint.Parse(headers["CelesteNet-ConnectionToken"], NumberStyles.HexNumber);
            IConnectionFeature[] conFeatures = headers["CelesteNet-ConnectionFeatures"].Split(new[] { ',' }).Select(n => features.FirstOrDefault(f => f.GetType().FullName == n)).Where(f => f != null).ToArray();

            T settings = new();
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                string headerName = $"CelesteNet-Settings-{field.Name}";
#pragma warning disable IDE0049 // Simplify Names
                switch (Type.GetTypeCode(field.FieldType)) {
                    case TypeCode.Boolean: field.SetValue(settings, Boolean.Parse(headers[headerName])); break;
                    case TypeCode.Int16:   field.SetValue(settings,   Int16.Parse(headers[headerName])); break;
                    case TypeCode.Int32:   field.SetValue(settings,   Int32.Parse(headers[headerName])); break;
                    case TypeCode.Int64:   field.SetValue(settings,   Int64.Parse(headers[headerName])); break;
                    case TypeCode.UInt16:  field.SetValue(settings,  UInt16.Parse(headers[headerName])); break;
                    case TypeCode.UInt32:  field.SetValue(settings,  UInt32.Parse(headers[headerName])); break;
                    case TypeCode.UInt64:  field.SetValue(settings,  UInt64.Parse(headers[headerName])); break;
                    case TypeCode.Single:  field.SetValue(settings,  Single.Parse(headers[headerName])); break;
                    case TypeCode.Double:  field.SetValue(settings,  Double.Parse(headers[headerName])); break;
                }
#pragma warning restore IDE0049
            }

            return new(conToken, conFeatures, (T) settings);
        }

        public static void DoConnectionHandshake(CelesteNetConnection con, IConnectionFeature[] features, CancellationToken token) {
            // Handshake connection features
            foreach (IConnectionFeature feature in features)
                feature.Register(con, true);
            foreach (IConnectionFeature feature in features)
                feature.DoHandshake(con, true).Wait(token);
        }

    }
}