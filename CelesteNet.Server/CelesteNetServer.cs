#define INMODDIR

using Celeste.Mod.CelesteNet.Server.Control;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServer : IDisposable {

        public readonly CelesteNetServerSettings Settings;

        public readonly Frontend Control;
        public readonly ChatServer Chat;

        public bool IsAlive;

        public CelesteNetServer()
            : this(new CelesteNetServerSettings()) {
        }

        public CelesteNetServer(CelesteNetServerSettings settings) {
            Settings = settings;

            Control = new Frontend(this);
            Chat = new ChatServer(this);
        }

        public void Start() {
            Logger.Log(LogLevel.CRI, "main", $"Startup on port {Settings.MainPort}");
            IsAlive = true;

            Control.Start();
            Chat.Start();

            // TODO: WAIT.
            while (IsAlive)
                Thread.Yield();
        }

        public void Dispose() {
            Logger.Log(LogLevel.CRI, "main", "Shutdown");

            Control.Dispose();
        }


        public Stream OpenContent(string path) {
            try {
                string dir = Path.GetFullPath(Settings.ContentRoot);
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS))
                    return File.OpenRead(pathFS);
            } catch {
            }

#if DEBUG
            try {
                string dir = Path.GetFullPath(Path.Combine("..", "..", "..", "Content"));
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS))
                    return File.OpenRead(pathFS);
            } catch {
            }
#endif

            return typeof(CelesteNetServer).Assembly.GetManifestResourceStream("Celeste.Mod.CelesteNet.Server.Content." + path.Replace("/", "."));
        }


        private static void LogHeader(TextWriter w) {
            w.WriteLine("CelesteNet.Server");
            w.WriteLine($"Server v.{typeof(CelesteNetServer).Assembly.GetName().Version}");
            w.WriteLine($"Shared v.{typeof(Logger).Assembly.GetName().Version}");
            w.WriteLine();
        }


        public static void Main(string[] args) {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

#if INMODDIR
            string celestePath = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", ".."));
            if (!File.Exists(Path.Combine(celestePath, "Celeste.exe"))) {
                celestePath = null;
            } else {
                AppDomain.CurrentDomain.AssemblyResolve += (asmSender, asmArgs) => {
                    string name = new AssemblyName(asmArgs.Name).Name;
                    string path = Path.Combine(celestePath, name + ".dll");
                    if (!File.Exists(path))
                        path = Path.Combine(celestePath, name + ".exe");
                    return File.Exists(path) ? Assembly.LoadFrom(path) : null;
                };
            }
#endif

            MainMain(args);
        }

        private static void MainMain(string[] args) {
            LogHeader(Console.Out);
            
            CelesteNetServerSettings settings = new CelesteNetServerSettings();
            string settingsPath = Path.GetFullPath("celestenet-config.yaml");

            if (File.Exists(settingsPath))
                using (Stream stream = File.OpenRead(settingsPath))
                using (StreamReader reader = new StreamReader(stream))
                    YamlHelper.DeserializerUsing(settings).Deserialize(reader, typeof(CelesteNetServerSettings));

            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

            using (Stream stream = File.OpenWrite(settingsPath + ".tmp"))
            using (StreamWriter writer = new StreamWriter(stream))
                YamlHelper.Serializer.Serialize(writer, settings, typeof(CelesteNetServerSettings));

            if (File.Exists(settingsPath))
                File.Delete(settingsPath);
            File.Move(settingsPath + ".tmp", settingsPath);


            bool showHelp = false;
            string logFile = "log-celestenet.txt";
            OptionSet options = new OptionSet {
                {
                    "v|loglevel:",
                    $"Change the log level, ranging from {LogLevel.CRI} ({(int) LogLevel.CRI}) to {LogLevel.DEV} ({(int) LogLevel.DEV}). Defaults to {LogLevel.INF} ({(int) LogLevel.INF}).",
                    v => {
                        if (Enum.TryParse(v, true, out LogLevel level)) {
                            Logger.Level = level;
                        } else {
                            Logger.Level--;
                        }

                        if (Logger.Level < LogLevel.DEV)
                            Logger.Level = LogLevel.DEV;
                        if (Logger.Level > LogLevel.CRI)
                            Logger.Level = LogLevel.CRI;

                        Console.WriteLine($"Log level changed to {Logger.Level}");
                    }
                },

                { "log", "Specify the file to log to.", v => { if (v != null) logFile = v; } },
                { "nolog", "Disable logging to a file.", v => { if (v != null) logFile = null; } },

                { "h|help", "Show this message and exit.", v => showHelp = v != null },
            };

            try {
                options.Parse(args);

            } catch (OptionException e) {
                Console.WriteLine(e.Message);
                Console.WriteLine("Use --help for argument info.");
                return;
            }

            if (showHelp) {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (logFile == null) {
                MainRun(settings);
                return;
            }

            if (File.Exists(logFile))
                File.Delete(logFile);

            using (Stream fileStream = new FileStream(logFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (StreamWriter fileWriter = new StreamWriter(fileStream, Console.OutputEncoding))
            using (LogWriter logWriter = new LogWriter {
                STDOUT = Console.Out,
                File = fileWriter
            }) {
                LogHeader(fileWriter);

                try {
                    Console.SetOut(logWriter);
                    MainRun(settings);

                } finally {
                    if (logWriter.STDOUT != null) {
                        Console.SetOut(logWriter.STDOUT);
                        logWriter.STDOUT = null;
                    }
                }
            }
        }

        private static void MainRun(CelesteNetServerSettings settings) {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            try {
                using (CelesteNetServer server = new CelesteNetServer(settings)) {
                    server.Start();
                }
            } catch (Exception e) {
                CriticalFailureHandler(e);
                return;
            }
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e) {
            if (e.IsTerminating) {
                _CriticalFailureIsUnhandledException = true;
                CriticalFailureHandler(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));

            } else {
                Logger.Log(LogLevel.CRI, "main", "Encountered an UNHANDLED EXCEPTION. Server shutting down.");
                Logger.LogDetailedException(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception"));
            }
        }

        private static bool _CriticalFailureIsUnhandledException;
        public static void CriticalFailureHandler(Exception e) {
            Logger.Log(LogLevel.CRI, "main", "Encountered a CRITICAL FAILURE. Server shutting down.");
            Logger.LogDetailedException(e ?? new Exception("Unknown exception"));

            if (!_CriticalFailureIsUnhandledException)
                Environment.Exit(-1);
        }

    }
}
