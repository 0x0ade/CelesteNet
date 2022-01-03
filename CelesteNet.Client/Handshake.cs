using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Client {
    public static class Handshake {

        public const int TeapotVersion = 1;

        // TODO MonoKickstart is so stupid, it can't even handle string.Split(char)...
        public static Tuple<uint, IConnectionFeature[], T> DoTeapotHandshake<T>(Socket sock, IConnectionFeature[] features, string nameKey) where T : class {
            // Find connection features
            // We don't buffer, as we could read actual packet data
            using NetworkStream netStream = new(sock, false);
            using StreamReader reader = new(netStream);
            using StreamWriter writer = new(netStream);
            // Send the "HTTP" request
            writer.Write($@"
CONNECT /teapot HTTP/1.1
CelesteNet-TeapotVersion: {TeapotVersion}
CelesteNet-ConnectionFeatures: {features.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}
CelesteNet-PlayerNameKey: {nameKey}

Can I have some tea?
".Trim().Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
            writer.Flush();

            // Read the "HTTP" response
            string statusLine = reader.ReadLine();
            string[] statusSegs = statusLine.Split(new[] { ' ' }, 3);
            if (statusSegs.Length != 3)
                throw new InvalidDataException($"Invalid HTTP response status line: '{statusLine}'");
            int statusCode = int.Parse(statusSegs[1]);

            Dictionary<string, string> headers = new();
            for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine()) {
                int split = line.IndexOf(':');
                if (split == -1)
                    throw new InvalidDataException($"Invalid HTTP header: '{line}'");
                headers[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }

            string content = "";
            for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
                content += line + "\n";

            // Parse the "HTTP response"
            if (statusCode != 418)
                throw new ConnectionErrorException($"Server rejected teapot handshake (status {statusCode})", content.Trim());

            uint conToken = uint.Parse(headers["CelesteNet-ConnectionToken"], NumberStyles.HexNumber);
            IConnectionFeature[] conFeatures = headers["CelesteNet-ConnectionFeatures"].Split(new[] { ',' }).Select(n => features.FirstOrDefault(f => f.GetType().FullName == n)).Where(f => f != null).ToArray();

            object boxedSettings = Activator.CreateInstance<T>();
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                string headerName = $"CelesteNet-Settings-{field.Name}";
#pragma warning disable IDE0049 // Simplify Names
                switch (Type.GetTypeCode(field.FieldType)) {
                    case TypeCode.Int16:  field.SetValue(boxedSettings,  Int16.Parse(headers[headerName])); break;
                    case TypeCode.Int32:  field.SetValue(boxedSettings,  Int32.Parse(headers[headerName])); break;
                    case TypeCode.Int64:  field.SetValue(boxedSettings,  Int64.Parse(headers[headerName])); break;
                    case TypeCode.UInt16: field.SetValue(boxedSettings, UInt16.Parse(headers[headerName])); break;
                    case TypeCode.UInt32: field.SetValue(boxedSettings, UInt32.Parse(headers[headerName])); break;
                    case TypeCode.UInt64: field.SetValue(boxedSettings, UInt64.Parse(headers[headerName])); break;
                    case TypeCode.Single: field.SetValue(boxedSettings, Single.Parse(headers[headerName])); break;
                    case TypeCode.Double: field.SetValue(boxedSettings, Double.Parse(headers[headerName])); break;
                }
#pragma warning restore IDE0049
            }

            return new(conToken, conFeatures, (T) boxedSettings);
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