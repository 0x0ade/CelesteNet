using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public interface IConnectionFeature {
        void Register(CelesteNetConnection con);
        Task DoHandShake(CelesteNetConnection con);
    }
}