using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public interface IConnectionFeature {
        void Register(CelesteNetConnection con, bool isClient);
        Task DoHandShake(CelesteNetConnection con, bool isClient);
    }
}