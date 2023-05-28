using MC = Mono.Cecil;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Linq;
using System.Security.Cryptography;

namespace Celeste.Mod.CelesteNet.Client {
    public static class CelesteNetClientUtils {

        public static float GetScreenScale(this Level level)
            => level.Zoom * ((320f - level.ScreenPadding * 2f) / 320f);

        public static Vector2 WorldToScreen(this Level level, Vector2 pos) {
            Camera cam = level.Camera;
            if (cam == null)
                return pos;

            pos -= cam.Position;

            Vector2 size = new(320f, 180f);
            Vector2 sizeScaled = size / level.ZoomTarget;
            Vector2 offs = level.ZoomTarget != 1f ? (level.ZoomFocusPoint - sizeScaled / 2f) / (size - sizeScaled) * size : Vector2.Zero;
            float scale = level.GetScreenScale();

            pos += new Vector2(level.ScreenPadding, level.ScreenPadding * 0.5625f);

            pos -= offs;
            pos *= scale;
            pos += offs;

            pos *= 6f; // 1920 / 320

            if (SaveData.Instance?.Assists.MirrorMode ?? false)
                pos.X = 1920f - pos.X;

            return pos;
        }

        public static bool GetClampedScreenPos(Vector2 worldPos, Level level, out Vector2 outPos, float marginX, float marginY, float offsetX = 0f, float offsetY = 0f) {
            return GetClampedScreenPos(worldPos, level, out outPos, marginX, marginY, marginX, marginY, offsetX, offsetY);
        }

        public static bool GetClampedScreenPos(Vector2 worldPos, Level level, out Vector2 outPos, float marginLeft, float marginTop, float marginRight, float marginBottom, float offsetX = 0f, float offsetY = 0f) {
            if (level == null) {
                outPos = Vector2.Zero;
                return false;
            }

            worldPos.X += offsetX;
            worldPos.Y += offsetY;

            Vector2 posScreen = level.WorldToScreen(worldPos);
            outPos = posScreen.Clamp(
                marginLeft, marginTop,
                1920f - marginRight, 1080f - marginBottom
            );
            return outPos.Equals(posScreen);
        }

        public static AudioState ToAudioState(this CelesteAudioState audioState) {
            if (audioState == null) return null;
            return new AudioState() {
                Music = audioState.Music.ToAudioTrackState(),
                Ambience = audioState.Ambience.ToAudioTrackState(),
                AmbienceVolume = audioState.AmbienceVolume
            };
        }

        public static AudioTrackState ToAudioTrackState(this CelesteAudioTrackState audioTrackState) {
            if (audioTrackState == null) return null;
            return new AudioTrackState() {
                Event = audioTrackState.Event,
                Progress = audioTrackState.Progress,
                Parameters = audioTrackState.Parameters.Select(mep => mep.ToMEP()).ToList()
            };
        }

        public static MEP ToMEP(this CelesteMEP mep) {
            if (mep == null) return null;
            return new MEP() {
                Key = mep.Key,
                Value = mep.Value
            };
        }

        public static CelesteAudioState FromAudioState(this AudioState audioState) {
            if (audioState == null) return null;
            return new CelesteAudioState() {
                Music = audioState.Music.FromAudioTrackState(),
                Ambience = audioState.Ambience.FromAudioTrackState(),
                AmbienceVolume = audioState.AmbienceVolume
            };
        }

        public static CelesteAudioTrackState FromAudioTrackState(this AudioTrackState audioTrackState) {
            if (audioTrackState == null) return null;
            return new CelesteAudioTrackState() {
                Event = audioTrackState.Event,
                Progress = audioTrackState.Progress,
                Parameters = audioTrackState.Parameters.Select(mep => mep.FromMEP()).ToList()
            };
        }

        public static CelesteMEP FromMEP(this MEP mep) {
            if (mep == null) return null;
            return new CelesteMEP() {
                Key = mep.Key,
                Value = mep.Value
            };
        }

        public static CelesteEntityID FromEntityID(this EntityID eid) {
            return new CelesteEntityID() {
                ID = eid.ID,
                Key = eid.Key
            };
        }

        public static CelesteSession.Counter FromSessionCounter(this Session.Counter counter) {
            if (counter == null) return null;
            return new CelesteSession.Counter() {
                Key = counter.Key,
                Value = counter.Value
            };
        }

        public static EntityID ToEntityID(this CelesteEntityID eid) {
            return new EntityID() {
                ID = eid.ID,
                Key = eid.Key
            };
        }

        public static Session.Counter ToSessionCounter(this CelesteSession.Counter counter) {
            if (counter == null) return null;
            return new Session.Counter() {
                Key = counter.Key,
                Value = counter.Value
            };
        }

        public static PlayerInventory ToPlayerInventory(this CelestePlayerInventory inv) {
            return new PlayerInventory() {
                Backpack = inv.Backpack,
                Dashes = inv.Dashes,
                DreamDash = inv.DreamDash,
                NoRefills = inv.NoRefills
            };
        }

        public static CelestePlayerInventory FromPlayerInventory(this PlayerInventory inv) {
            return new CelestePlayerInventory() {
                Backpack = inv.Backpack,
                Dashes = inv.Dashes,
                DreamDash = inv.DreamDash
            };
        }

        private readonly static FieldInfo f_Player_wasDashB =
            typeof(Player).GetField("wasDashB", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool GetWasDashB(this Player self)
            => (bool) f_Player_wasDashB.GetValue(self);

        private readonly static FieldInfo f_Level_updateHair =
            typeof(Level).GetField("updateHair", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool GetUpdateHair(this Level self)
            => (bool) f_Level_updateHair.GetValue(self);

        private readonly static FieldInfo f_TrailManager_shapshots =
            typeof(TrailManager).GetField("snapshots", BindingFlags.NonPublic | BindingFlags.Instance);

        public static TrailManager.Snapshot[] GetSnapshots(this TrailManager self)
            => (TrailManager.Snapshot[]) f_TrailManager_shapshots.GetValue(self);

        private delegate IntPtr _AsPointer<T>(ref T value);
        private static readonly Dictionary<Type, Delegate> _AsPointerCache = new();
        private static MethodInfo _AsPointerHelper;
        public static IntPtr AsPointer<T>(ref T value) {
            Delegate cached;
            lock (_AsPointerCache)
                _AsPointerCache.TryGetValue(typeof(T), out cached);
            if (cached != null)
                return (cached as _AsPointer<T>)(ref value);

            if (_AsPointerHelper == null) {
                Assembly asm;

                const string @namespace = "Celeste.Mod.CelesteNet.Client";
                const string @name = "AsPointerHelper";
                const string @fullName = @namespace + "." + @name;

                using (ModuleDefinition module = ModuleDefinition.CreateModule(
                    @fullName,
                    new ModuleParameters() {
                        Kind = ModuleKind.Dll,
                        ReflectionImporterProvider = MMReflectionImporter.Provider
                    }
                )) {

                    TypeDefinition type = new(
                        @namespace,
                        @name,
                        MC.TypeAttributes.Public | MC.TypeAttributes.Abstract | MC.TypeAttributes.Sealed
                    ) {
                        BaseType = module.TypeSystem.Object
                    };
                    module.Types.Add(type);

                    MethodDefinition method = new(@name,
                        MC.MethodAttributes.Public | MC.MethodAttributes.Static | MC.MethodAttributes.HideBySig,
                        module.TypeSystem.Int32
                    );
                    GenericParameter genParam = new("T", method);
                    method.GenericParameters.Add(genParam);
                    method.Parameters.Add(new("value", MC.ParameterAttributes.None, new ByReferenceType(genParam)));
                    type.Methods.Add(method);

                    ILProcessor il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Conv_U);
                    il.Emit(OpCodes.Ret);

                    asm = ReflectionHelper.Load(module);
                }

                _AsPointerHelper = asm.GetType(@fullName).GetMethod(@name);
            }

            _AsPointer<T> generated = _AsPointerHelper.MakeGenericMethod(typeof(T)).CreateDelegate<_AsPointer<T>>() as _AsPointer<T>;
            lock (_AsPointerCache)
                _AsPointerCache[typeof(T)] = generated;
            return generated(ref value);
        }

        private delegate ref T _AsRef<T>(IntPtr value);
        private static readonly Dictionary<Type, Delegate> _AsRefCache = new();
        private static MethodInfo _AsRefHelper;
        public static ref T AsRef<T>(IntPtr value) {
            Delegate cached;
            lock (_AsRefCache)
                _AsRefCache.TryGetValue(typeof(T), out cached);
            if (cached != null)
                return ref (cached as _AsRef<T>)(value);

            if (_AsRefHelper == null) {
                Assembly asm;

                const string @namespace = "Celeste.Mod.CelesteNet.Client";
                const string @name = "AsRefHelper";
                const string @fullName = @namespace + "." + @name;

                using (ModuleDefinition module = ModuleDefinition.CreateModule(
                    @fullName,
                    new ModuleParameters() {
                        Kind = ModuleKind.Dll,
                        ReflectionImporterProvider = MMReflectionImporter.Provider
                    }
                )) {

                    TypeDefinition type = new(
                        @namespace,
                        @name,
                        MC.TypeAttributes.Public | MC.TypeAttributes.Abstract | MC.TypeAttributes.Sealed
                    ) {
                        BaseType = module.TypeSystem.Object
                    };
                    module.Types.Add(type);

                    MethodDefinition method = new(@name,
                        MC.MethodAttributes.Public | MC.MethodAttributes.Static | MC.MethodAttributes.HideBySig,
                        module.TypeSystem.Int32
                    );
                    GenericParameter genParam = new("T", method);
                    method.GenericParameters.Add(genParam);
                    method.Parameters.Add(new("value", MC.ParameterAttributes.None, new ByReferenceType(module.TypeSystem.Int32)));
                    method.Body.Variables.Add(new(new ByReferenceType(genParam)));
                    type.Methods.Add(method);

                    ILProcessor il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Stloc_0);
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ret);

                    asm = ReflectionHelper.Load(module);
                }

                _AsRefHelper = asm.GetType(@fullName).GetMethod(@name);
            }

            _AsRef<T> generated = _AsRefHelper.MakeGenericMethod(typeof(T)).CreateDelegate<_AsRef<T>>() as _AsRef<T>;
            lock (_AsRefCache)
                _AsRefCache[typeof(T)] = generated;
            return ref generated(value);
        }

        private static readonly FieldInfo f_StateMachine_begins =
            typeof(StateMachine).GetField("begins", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_StateMachine_updates =
            typeof(StateMachine).GetField("updates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_StateMachine_ends =
            typeof(StateMachine).GetField("ends", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo f_StateMachine_coroutines =
            typeof(StateMachine).GetField("coroutines", BindingFlags.Instance | BindingFlags.NonPublic);
        public static int AddState(this StateMachine machine, Func<int> onUpdate, Func<IEnumerator> coroutine = null, Action begin = null, Action end = null) {
            Action[] begins = (Action[]) f_StateMachine_begins.GetValue(machine);
            Func<int>[] updates = (Func<int>[]) f_StateMachine_updates.GetValue(machine);
            Action[] ends = (Action[]) f_StateMachine_ends.GetValue(machine);
            Func<IEnumerator>[] coroutines = (Func<IEnumerator>[]) f_StateMachine_coroutines.GetValue(machine);
            int nextIndex = begins.Length;
            Array.Resize(ref begins, begins.Length + 1);
            Array.Resize(ref updates, begins.Length + 1);
            Array.Resize(ref ends, begins.Length + 1);
            Array.Resize(ref coroutines, coroutines.Length + 1);
            f_StateMachine_begins.SetValue(machine, begins);
            f_StateMachine_updates.SetValue(machine, updates);
            f_StateMachine_ends.SetValue(machine, ends);
            f_StateMachine_coroutines.SetValue(machine, coroutines);
            machine.SetCallbacks(nextIndex, onUpdate, coroutine, begin, end);
            return nextIndex;
        }

    }
}
