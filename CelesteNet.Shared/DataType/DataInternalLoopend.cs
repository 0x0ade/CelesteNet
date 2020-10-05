using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataInternalLoopend : DataType {

        public Action Action;

        public DataInternalLoopend(Action action) {
            Action = action;
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            throw new NotImplementedException();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            throw new NotImplementedException();
        }

    }
}
