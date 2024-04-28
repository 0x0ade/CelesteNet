using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataClientInfo : DataType<DataClientInfo>, IDataRequestable<DataClientInfoRequest> {

        static DataClientInfo() {
            DataID = "clientInfo";
        }

        public override DataFlags DataFlags => DataFlags.Taskable;

        public uint RequestID = uint.MaxValue;

        public string Nonce = "";

        // These could be "anything" in the client, so A, B and C they shall be...
        public string ConnInfoA = "";
        public string ConnInfoB = "";
        public string ConnInfoC = "";
        public string ConnInfoD = "";

        private bool isValidated = false;
        public bool IsValid => isValidated && ConnInfoA.Length > 0 && ConnInfoB.Length > 0 && ConnInfoC.Length > 0;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequestResponse(RequestID)
            };

        public override void FixupMeta(DataContext ctx) {
            RequestID = Get<MetaRequestResponse>(ctx);
        }

        /* Here in Read and Write, the strings are ran through "WireData()"
         * before writing to/reading from the "wire", so that the strings look
         * less obvious in the data packets.
         * It's just some XOR shenanigans.
         */

        protected override void Read(CelesteNetBinaryReader reader) {
            Nonce = reader.ReadNetString();
            ConnInfoA = WireData(reader.ReadNetString(), Nonce);
            ConnInfoB = WireData(reader.ReadNetString(), Nonce);
            ConnInfoC = WireData(reader.ReadNetString(), Nonce);
            ConnInfoD = WireData(reader.ReadNetString(), Nonce);
            if (ConnInfoA.StartsWith(Nonce)
                && ConnInfoB.StartsWith(Nonce)
                && ConnInfoC.StartsWith(Nonce)
                && ConnInfoD.StartsWith(Nonce)) {
                ConnInfoA = ConnInfoA.Substring(Nonce.Length);
                ConnInfoB = ConnInfoB.Substring(Nonce.Length);
                ConnInfoC = ConnInfoC.Substring(Nonce.Length);
                ConnInfoD = ConnInfoD.Substring(Nonce.Length);
                isValidated = true;
            } else {
                isValidated = false;
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Nonce);
            writer.WriteNetString(WireData(Nonce + ConnInfoA, Nonce));
            writer.WriteNetString(WireData(Nonce + ConnInfoB, Nonce));
            writer.WriteNetString(WireData(Nonce + ConnInfoC, Nonce));
            writer.WriteNetString(WireData(Nonce + ConnInfoD, Nonce));
        }

        public static string WireData(string inp, string mask) {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < inp.Length; i++)
                sb.Append((char)(((inp[i] - 'a') ^ mask[(i % mask.Length)]) + 'a'));

            return sb.ToString();
        }
    }

    public class DataClientInfoRequest : DataType<DataClientInfoRequest> {

        static DataClientInfoRequest() {
            DataID = "clientInfoReq";
        }

        public uint ID = uint.MaxValue;

        public string Nonce;

        public string[] List = Dummy<string>.EmptyArray;
        public string[] MapStrings = Dummy<string>.EmptyArray;

        public bool IsValid => MapStrings.Length == checksums.Length;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequest(ID)
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRequest>(ctx);
        }

        private readonly string[] checksums = new string[] {
            "360ac5c220a3033589229abfa126728e75a7318e9c05ac5b69021c55abf04398",
            "9bb95f1941e2c94dd99c073e2f3d49ea15f84e353b63752eecdd9b70f2087078",
            "704c3c7955b5df5e82a3217d5653019946077f9ba0a5bd63df30b848c6bea002",
            "1e7ec6443675384242e877f42bcf45ca8a45632c533f9a18a842d825586c6037",
            "36a79172dd1fdcd4e5a1409340a014060c834d5fdfd8d4d55a089f9c87462633",
            "c1a5151fb910ba8074479d876d3a04d9588b44e97f9462e34b1f9c484cd9eafe",
            "6402a261294e6f3180017bd427066422f691b66d58708f06d532dd9c4801a31e",
            "e20381a73e871b0b732d4cee2cc10eab1a51d21983d417939efde6a399189684",
            "2745c6c35be46aa1e23f694259acaf536247dc127bf7f728035891cdc1390992"
        };

        protected override void Read(CelesteNetBinaryReader reader) {
            Nonce = reader.ReadNetString();
            List = new string[reader.ReadUInt16()];
            for (int i = 0; i < List.Length; i++)
                List[i] = DataClientInfo.WireData(reader.ReadNetString(), Nonce);
            MapStrings = new string[reader.ReadUInt16()];
            bool all_good = MapStrings.Length == checksums.Length;
            using (SHA256 sha256Hash = SHA256.Create()) {
                for (int i = 0; i < MapStrings.Length; i++) {
                    MapStrings[i] = DataClientInfo.WireData(reader.ReadNetString(), Nonce);
                    string hash = string.Concat(sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(MapStrings[i])).Select(b => string.Format("{0:x2}", b)));
                    Logger.Log(LogLevel.VVV, "dataclientinforequest", $"{MapStrings[i]} = {hash}");
                    MapStrings[i] = Encoding.UTF8.GetString(Convert.FromBase64String(MapStrings[i]));
                    Logger.Log(LogLevel.VVV, "dataclientinforequest", $"= {MapStrings[i]}");
                    if (all_good && hash != checksums[i])
                        all_good = false;
                }
            }
            if (!all_good)
                MapStrings = Dummy<string>.EmptyArray;
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Nonce);
            writer.Write((ushort)List.Length);
            foreach (string req in List) {
                writer.WriteNetString(DataClientInfo.WireData(req, Nonce));
            }
            writer.Write((ushort)MapStrings.Length);
            foreach (string req in MapStrings) {
                writer.WriteNetString(DataClientInfo.WireData(req, Nonce));
            }
        }

    }
}