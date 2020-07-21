using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class SpamContext : IDisposable {

        public readonly ChatModule Chat;

        public readonly Dictionary<string, SpamTimeout> Timeouts = new Dictionary<string, SpamTimeout>();

        public event Action<DataChat, SpamTimeout>? OnSpam;

        public SpamContext(ChatModule chat) {
            Chat = chat;
        }

        public bool IsSpam(DataChat msg) {
            string text = msg.ToString(false, false).ToLowerInvariant().Sanitize();
            lock (Timeouts) {
                if (!Timeouts.TryGetValue(text, out SpamTimeout? entry))
                    Timeouts[text] = entry = new SpamTimeout(this, text);
                if (entry.Add()) {
                    OnSpam?.Invoke(msg, entry);
                    return true;
                }
                return false;
            }
        }

        public void Dispose() {
            // TODO: Actually cancel the remaining tasks.
            lock (Timeouts) {
                Timeouts.Clear();
            }
        }

        public class SpamTimeout {

            public readonly SpamContext Spam;
            public readonly string Text;
            public DateTime Start;

            public DateTime End =>
                Start + TimeSpan.FromSeconds(
                    Spam.Chat.Settings.SpamTimeout +
                    Spam.Chat.Settings.SpamTimeoutAdd * Calc.Clamp(Count - Spam.Chat.Settings.SpamCount, 0, Spam.Chat.Settings.SpamCountMax)
                );

            public TimeSpan Timeout => End - DateTime.UtcNow;

            public bool Spammed;
            public int Count;
            public bool Unspammed;

            public SpamTimeout(SpamContext spam, string text) {
                Spam = spam;
                Text = text;
                Start = DateTime.UtcNow;
            }

            public bool Add() {
                lock (Spam.Timeouts) {
                    if (Count < Spam.Chat.Settings.SpamCountMax) {
                        Count++;

                        Task.Run(async () => {
                            await Task.Delay(TimeSpan.FromSeconds(Spam.Chat.Settings.SpamTimeoutAdd));
                            lock (Spam.Timeouts) {
                                if (Unspammed)
                                    return;
                                Count--;
                                if ((!Spammed || Timeout.Ticks <= 0) && Count <= 0)
                                    Spam.Timeouts.Remove(Text);
                            }
                        });
                    }

                    if (Spammed || Count >= Spam.Chat.Settings.SpamCount) {
                        if (!Spammed)
                            Start = DateTime.UtcNow;
                        Spammed = true;
                        Task.Run(async () => {
                            TimeSpan timeout = Timeout;
                            if (timeout.Ticks > 0)
                                await Task.Delay(timeout);
                            lock (Spam.Timeouts) {
                                if (Unspammed)
                                    return;
                                if (Count <= 0) {
                                    Unspammed = true;
                                    Spam.Timeouts.Remove(Text);
                                }
                            }
                        });
                        return true;
                    }

                    return false;
                }
            }

        }

    }
}
