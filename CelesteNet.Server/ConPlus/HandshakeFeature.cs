using System;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server;

namespace Celeste.Mod.CelesteNet {
    public class ExtendedHandshake : IConnectionFeature {

        public async Task DoHandshake(CelesteNetConnection con, bool isClient) {
            if (isClient)
                throw new InvalidOperationException($"ExtendedHandshake was called with 'isClient == true' in Server context!");

            ConPlusTCPUDPConnection? Con = (ConPlusTCPUDPConnection?)con;

            if (Con == null) {
                Logger.Log(LogLevel.VVV, "handshakeFeature", $"Con was null");
                return;
            }

            if (!Con.Server.Settings.ClientChecks) {
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();

            string Nonce = Con.ConnectionToken.ToString();

            Con.Server.Data.WaitFor<DataClientInfo>(400, (con, data) => {
                if (Con != con)
                    return false;

                if (data.IsValid) {
                    if (data.ConnInfoD.Trim().Length > 0) {
                        // self-report
                        Con.ConnFeatureData.Add(ConnFeatureUtils.CHECK_ENTRY_BAN, data.ConnInfoD);

                    } else {
                        Con.ConnFeatureData.Add(ConnFeatureUtils.CHECK_ENTRY_DEV, data.ConnInfoA);
                        Con.ConnFeatureData.Add(ConnFeatureUtils.CHECK_ENTRY_MAC, data.ConnInfoB);
                        Con.ConnFeatureData.Add(ConnFeatureUtils.CHECK_ENTRY_ENV, data.ConnInfoC);
                    }
                }

                Logger.Log(LogLevel.VVV, "handshakeFeature", $"Got DataClientInfo for {Con?.UID} = {data.ConnInfoA} - {data.ConnInfoB} - {data.ConnInfoC} - {data.ConnInfoD}");
                cts.Cancel();
                return true;
            }, () => {
                Logger.Log(LogLevel.VVV, "handshakeFeature", $"Awaiting DataClientInfo timed out for {Con?.UID}");
                cts.Cancel();
            });

            Logger.Log(LogLevel.VVV, "handshakeFeature", $"Sending DataClientInfoRequest to {Con?.UID}");
            Con?.Send(new DataClientInfoRequest {
                Nonce = Nonce,
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

            // if we need hold back on establishing a session
            if (!cts.Token.IsCancellationRequested) {
                try {
                    await Task.Delay(400, cts.Token);
                } catch (TaskCanceledException ex) {
                    Logger.Log(LogLevel.VVV, "handshakeFeature", $"Delay skipped by TaskCanceledException: {ex.Message}");
                }
            }
        }

        public void Register(CelesteNetConnection con, bool isClient) {
            if (isClient)
                throw new InvalidOperationException($"ExtendedHandshake was called with 'isClient == true' in Server context!");

            // Got nothing to do here
        }
    }
}