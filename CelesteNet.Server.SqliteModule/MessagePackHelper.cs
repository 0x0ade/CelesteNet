using System;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Sqlite {
    public static class MessagePackHelper {

        public static readonly MessagePackSerializerOptions Options = ContractlessStandardResolver.Options
            .WithResolver(CelesteNetMessagePackResolver.Instance);

    }

    public class CelesteNetMessagePackResolver : IFormatterResolver {

        public static readonly IFormatterResolver Instance = new CelesteNetMessagePackResolver();

        private static readonly IFormatterResolver[] Resolvers = {
            ContractlessStandardResolver.Instance
        };

        private static readonly Dictionary<Type, object> Formatters = new() {
            { typeof(Color), new ColorMessagePackFormatter() }
        };

        private CelesteNetMessagePackResolver() {
        }

        public IMessagePackFormatter<T>? GetFormatter<T>()
            => Cache<T>.Formatter;

        private static class Cache<T> {

            public static IMessagePackFormatter<T>? Formatter;

            static Cache() {
                if (Formatters.TryGetValue(typeof(T), out object? fo)) {
                    Formatter = (IMessagePackFormatter<T>) fo;
                    return;
                }

                foreach (IFormatterResolver resolver in Resolvers) {
                    if (resolver.GetFormatter<T>() is IMessagePackFormatter<T> f) {
                        Formatter = f;
                        return;
                    }
                }

                Formatter = ContractlessStandardResolver.Instance.GetFormatter<T>();
            }

        }

    }

    public class ColorMessagePackFormatter : IMessagePackFormatter<Color> {

        public Color Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
            => new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());

        public void Serialize(ref MessagePackWriter writer, Color value, MessagePackSerializerOptions options) {
            writer.Write(value.R);
            writer.Write(value.G);
            writer.Write(value.B);
            writer.Write(value.A);
        }

    }
}
