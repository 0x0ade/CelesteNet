using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientSession : EverestModuleSession {
        /// <summary>
        /// Overrides the InGame.Interactions setting. null = use setting (do not override). Default is null.
        /// </summary>
        public bool? InteractionsOverride { get; set; } = null;

        /// <summary>
        /// Whether to use interactions in this session
        /// </summary>
        public bool UseInteractions => InteractionsOverride ?? CelesteNetClientModule.Settings?.InGame?.Interactions ?? false;
    }
}
