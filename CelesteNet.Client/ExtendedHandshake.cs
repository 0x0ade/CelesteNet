using System;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {

    public class ExtendedHandshake : IConnectionFeature {

        public Task DoHandshake(CelesteNetConnection con, bool isClient) {
            if (!isClient)
                throw new InvalidOperationException($"HandshakeFeature was called with 'isClient == false' in Client context!");

            // Handler for DataClientInfoRequest is implemented in CelesteNetClient

            return Task.Run(() => {
                Logger.Log(LogLevel.VVV, "handshakeFeature", $"Connection uses ExtendedHandshake");
            });
        }

        public void Register(CelesteNetConnection con, bool isClient) {
            if (!isClient)
                throw new InvalidOperationException($"HandshakeFeature was called with 'isClient == false' in Client context!");
        }
    }
}