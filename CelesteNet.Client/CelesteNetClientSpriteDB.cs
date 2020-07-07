using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;

using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public static class CelesteNetClientSpriteDB {

        public static void Load() {
            if (GFX.SpriteBank != null) {

            }

            On.Monocle.Sprite.CloneInto += OnSpriteCloneInto;
            On.Monocle.SpriteData.Add += OnSpriteDataAdd;
        }

        public static void Unload() {
            On.Monocle.Sprite.CloneInto -= OnSpriteCloneInto;
            On.Monocle.SpriteData.Add -= OnSpriteDataAdd;
        }

        private static Sprite OnSpriteCloneInto(On.Monocle.Sprite.orig_CloneInto orig, Sprite self, Sprite clone) {
            new DynamicData(clone).Set("CelesteNetSpriteID", self.GetID());
            return orig(self, clone);
        }

        private static void OnSpriteDataAdd(On.Monocle.SpriteData.orig_Add orig, SpriteData self, System.Xml.XmlElement xml, string overridePath) {
            self.Sprite.SetID(xml.Name);
            orig(self, xml, overridePath);
        }

        public static void SetID(this Sprite self, string value)
            => new DynamicData(self).Set("CelesteNetSpriteID", value);

        public static string GetID(this Sprite self)
            => new DynamicData(self).Get<string>("CelesteNetSpriteID");

    }
}
