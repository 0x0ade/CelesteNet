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
    public class DataTCPHTTPTeapot : DataType<DataTCPHTTPTeapot> {

        // Handled specially by CelesteNetTCPUDPConnection.

        public override void Read(BinaryReader reader) {
            throw new NotSupportedException();
        }

        public override void Write(BinaryWriter writer) {
            throw new NotSupportedException();
        }

        public override DataTCPHTTPTeapot CloneT()
            => new DataTCPHTTPTeapot {
            };

    }
}
