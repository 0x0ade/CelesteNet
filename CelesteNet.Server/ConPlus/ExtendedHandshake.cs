using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server;

namespace Celeste.Mod.CelesteNet {
    public class ExtendedHandshake : IConnectionFeature {

        public record ConnectionData(string CheckEnv = "", string CheckMAC = "", string CheckDevice = "", string SelfReportBan = "") {
            public readonly IDictionary<string, string> CheckEntries = new Dictionary<string, string>() {
                ["CheckEnv"] = CheckEnv,
                ["CheckMAC"] = CheckMAC,
                ["CheckDevice"] = CheckDevice,
            }.AsReadOnly();

            public bool CheckEntriesValid => CheckEntries.All(e => !e.Value.IsNullOrEmpty());
        }

        public static string? ClientCheck(ConPlusTCPUDPConnection con, ConnectionData conData) {
            if (!conData.SelfReportBan.IsNullOrEmpty()) {
                BanInfo banMe = new() {
                    UID = con.UID,
                    // I had this prefix it with "Auto-ban" but since we don't have separate "reasons" for internal
                    // documentation vs. what is shown to the client, I'd like to hide the fact that this is an extra
                    // "automated" ban happening.
                    Reason = "-> " + conData.SelfReportBan,
                    From = DateTime.UtcNow
                };
                con.Server.UserData.Save(con.UID, banMe);

                Logger.Log(LogLevel.VVV, "frontend", $"Auto-ban of secondary IP: {conData.SelfReportBan} ({con.UID})");

                return con.Server.Settings.MessageClientCheckFailed;
            }

            if (!conData.CheckEntriesValid)
                return con.Server.Settings.MessageClientCheckFailed;

            BanInfo? ban = null;
            bool isBanned = false;
            foreach ((string key, string val) in conData.CheckEntries) {
                if (con.Server.UserData.TryLoad($"{key}#{val}", out ban)) {
                    isBanned = true;
                    break;
                }
            }

            if (isBanned && ban != null && (ban.From == null || ban.From <= DateTime.Now) && (ban.To == null || DateTime.Now <= ban.To))
                return string.Format(con.Server.Settings.MessageBan, "", "", ban.Reason);
            else
                return null;
        }

        public async Task DoHandshake(CelesteNetConnection rawCon, bool isClient) {
            if (isClient)
                throw new InvalidOperationException($"ExtendedHandshake was called with 'isClient == true' in Server context!");

            if (!(rawCon is ConPlusTCPUDPConnection con))
                return;

            if (!con.Server.Settings.ClientChecks)
                return;

            Task<DataClientInfo> clientInfoTask = con.Server.Data.WaitForAsync<DataClientInfo>(400, (dataCon, data) => dataCon == con);

            Logger.Log(LogLevel.VVV, "handshakeFeature", $"Sending DataClientInfoRequest to {con.UID}");
            con.Send(new DataClientInfoRequest {
                Nonce = con.ConnectionToken.ToString(),
                List = new string[] {
                    "UHJvY2Vzc29yQ291bnQ=",
                    "TWFjaGluZU5hbWU="
                },
                MapStrings = new string[] {
                    "U1lTVEVNXFxDdXJyZW50Q29udHJvbFNldFxcQ29udHJvbFxcTmV0d29ya1xcezREMzZFOTcyLUUzMjUtMTFDRS1CRkMxLTA4MDAyQkUxMDMxOH1cXA==", "XFxDb25uZWN0aW9u",
                    "UG5wSW5zdGFuY2VJRA==", "U09GVFdBUkVcXE1pY3Jvc29mdFxcQ3J5cHRvZ3JhcGh5", "TWFjaGluZUd1aWQ=", "aW9yZWcgLXJkMSAtYyBJT1BsYXRmb3JtRXhwZXJ0RGV2aWNl",
                    "SU9QbGF0Zm9ybVVVSUQ=", "L3Zhci9saWIvZGJ1cy9tYWNoaW5lLWlk", "L2V0Yy9tYWNoaW5lLWlk",
                }
            });

            DataClientInfo? clientInfo = await clientInfoTask.ContinueWith(t => t.IsCanceled ? null : t.Result);
            if (clientInfo == null) {
                Logger.Log(LogLevel.VVV, "handshakeFeature", $"Awaiting DataClientInfo timed out for {con.UID}");
                return;
            }

            Logger.Log(LogLevel.VVV, "handshakeFeature", $"Got DataClientInfo for {con.UID} = {clientInfo.ConnInfoA} - {clientInfo.ConnInfoB} - {clientInfo.ConnInfoC} - {clientInfo.ConnInfoD}");

            if (clientInfo.IsValid) {
                ConnectionData conData;
                if (clientInfo.ConnInfoD.Trim().Length > 0) {
                    // self-report
                    conData = new ConnectionData() {
                        SelfReportBan = clientInfo.ConnInfoD.Trim()
                    };
                } else {
                    conData = new ConnectionData() { 
                        CheckDevice = clientInfo.ConnInfoA.Trim(),
                        CheckMAC = clientInfo.ConnInfoB.Trim(),
                        CheckEnv = clientInfo.ConnInfoC.Trim(),
                    };
                }
                con.SetAssociatedData(conData);
            }
        }

        public void Register(CelesteNetConnection con, bool isClient) {
            if (isClient)
                throw new InvalidOperationException($"ExtendedHandshake was called with 'isClient == true' in Server context!");

            // Got nothing to do here
        }

    }
}