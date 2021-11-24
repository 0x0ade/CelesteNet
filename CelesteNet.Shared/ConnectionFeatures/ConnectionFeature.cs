using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public interface IConnectionFeature {
        void Register(CelesteNetConnection con, bool isClient);
        Task DoHandshake(CelesteNetConnection con, bool isClient);
    }
}