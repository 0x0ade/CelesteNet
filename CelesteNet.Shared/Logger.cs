using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.CelesteNet
{
    // Based off of Everest's Logger.
    public static class Logger {

        public static LogLevel Level = LogLevel.INF;
        public static bool LogCelesteNetTag = false;

        private static TextWriter? _Writer;
        public static TextWriter Writer {
            get => _Writer ?? Console.Out;
            set => _Writer = value;
        }

        /// <summary>
        /// Log a string to the console and to log.txt
        /// </summary>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        public static void Log(string tag, string str)
            => Log(LogLevel.VVV, tag, str);

        /// <summary>
        /// Log a string to the console and to log.txt
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        public static void Log(LogLevel level, string tag, string str) {
            if (level < Level)
                return;

            TextWriter writer = Writer;
            lock (writer) {
                writer.Write("(");
                writer.Write(DateTime.Now);
                writer.Write(LogCelesteNetTag ? ") [CelesteNet] [" : ") [");
                writer.Write(level.ToString());
                Writer.Write("] [");
                writer.Write(tag);
                writer.Write("] ");
                writer.WriteLine(str);
            }
        }

        /// <summary>
        /// Log a string to the console and to log.txt, including a call stack trace.
        /// </summary>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        public static void LogDetailed(string tag, string str) {
            lock (Writer) {
                Log(LogLevel.VVV, tag, str);
                Writer.WriteLine(new StackTrace(1, true).ToString());
            }
        }
        /// <summary>
        /// Log a string to the console and to log.txt, including a call stack trace.
        /// </summary>
        /// <param name="level">The log level.</param>
        /// <param name="tag">The tag, preferably short enough to identify your mod, but not too long to clutter the log.</param>
        /// <param name="str">The string / message to log.</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void LogDetailed(LogLevel level, string tag, string str) {
            lock (Writer) {
                Log(level, tag, str);
                Writer.WriteLine(new StackTrace(1, true).ToString());
            }
        }

        /// <summary>
        /// Print the exception to the console, including extended loading / reflection data useful for mods.
        /// </summary>
        public static void LogDetailedException(/*this*/ Exception? e, string? tag = null) {
            lock (Writer) {
                if (e == null)
                    return;

                TextWriter w = Writer;

                if (tag == null) {
                    w.WriteLine("--------------------------------");
                    w.WriteLine("Detailed exception log:");
                }
                for (Exception? e_ = e; e_ != null; e_ = e_.InnerException) {
                    w.WriteLine("--------------------------------");
                    w.WriteLine(e_.GetType().FullName + ": " + e_.Message + "\n" + e_.StackTrace);

                    switch (e_) {
                        case ReflectionTypeLoadException rtle:
                            if (rtle.Types != null)
                                for (int i = 0; i < rtle.Types.Length; i++)
                                    w.WriteLine("ReflectionTypeLoadException.Types[" + i + "]: " + rtle.Types[i]);
                            if (rtle.LoaderExceptions != null)
                                for (int i = 0; i < rtle.LoaderExceptions.Length; i++)
                                    LogDetailedException(rtle.LoaderExceptions[i], tag + (tag == null ? "" : ", ") + "rtle:" + i);
                            break;

                        case TypeLoadException tle:
                            w.WriteLine("TypeLoadException.TypeName: " + tle.TypeName);
                            break;

                        case BadImageFormatException bife:
                            w.WriteLine("BadImageFormatException.FileName: " + bife.FileName);
                            break;
                    }
                }
            }
        }

    }
    public enum LogLevel {
        DEV = -1,
        VVV,
        DBG,
        INF,
        WRN,
        ERR,
        CRI
    }
}
