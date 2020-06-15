using IL.Monocle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class FrontendWebSocket : WebSocketBehavior {

        // Each connection creates one instance of this.

        public Frontend Frontend;

        public string SessionKey;

        public WSCommands Commands;

        private IPEndPoint CurrentEndPoint;

        private new EState State = EState.Invalid;
        private WSCMD CurrentCommand;

        public FrontendWebSocket() {
        }

        public FrontendWebSocket(Frontend frontend) {
            Frontend = frontend;
        }

        private void Close(string reason) {
            State = EState.Invalid;
            Context.WebSocket.Close(CloseStatusCode.Normal, reason);
        }

        private void RunCommand(object input) {
            try {
                object output = CurrentCommand.Run(input);

                using (MemoryStream ms = new MemoryStream()) {
                    using (StreamWriter sw = new StreamWriter(ms, Encoding.UTF8, 1024, true))
                    using (JsonTextWriter jtw = new JsonTextWriter(sw))
                        Frontend.Serializer.Serialize(jtw, output);

                    ms.Seek(0, SeekOrigin.Begin);

                    Send("data");
                    using (StreamReader sr = new StreamReader(ms, Encoding.UTF8, false, 1024, true))
                        Send(sr.ReadToEnd());
                }

                State = EState.WaitForType;
            } catch (Exception e) {
                Logger.Log(LogLevel.ERR, "frontend-ws", e.ToString());
                Close("error on cmd run");
            }
        }

        protected override void OnOpen() {
            base.OnOpen();
            Logger.Log(LogLevel.INF, "frontend-ws", $"Opened connection: {Context.UserEndPoint}");
            CurrentEndPoint = Context.UserEndPoint;
            Commands = new WSCommands(this);
            State = EState.WaitForType;
            CurrentCommand = null;
        }

        protected override void OnClose(CloseEventArgs e) {
            base.OnClose(e);
            Logger.Log(LogLevel.VVV, "frontend-ws", $"Closed connection: {CurrentEndPoint} - {e.Reason}");
        }

        protected override void OnError(WebSocketSharp.ErrorEventArgs e) {
            base.OnError(e);
            Logger.Log(LogLevel.ERR, "frontend-ws", $"Errored connection: {CurrentEndPoint} - {e.Message} - {e.Exception}");
        }

        protected override void OnMessage(MessageEventArgs c) {
            switch (State) {
                case EState.WaitForType:
                    switch (c.Data) {
                        case "cmd":
                            State = EState.WaitForCMDID;
                            break;

                        case "data":
                            State = EState.WaitForData;
                            break;

                        default:
                            Close("unknown type");
                            break;
                    }
                    break;


                case EState.WaitForCMDID:
                    Logger.Log(LogLevel.DEV, "frontend-ws", $"CMD: {Context.UserEndPoint} - {c.Data}");
                    WSCMD cmd = Commands.Get(c.Data);
                    if (cmd == null) {
                        Close("unknown cmd");
                        break;
                    }

                    if (cmd.Auth && !Frontend.CurrentSessionKeys.Contains(SessionKey)) {
                        Close("unauthorized");
                        break;
                    }

                    CurrentCommand = cmd;
                    State = EState.WaitForCMDPayload;
                    break;


                case EState.WaitForCMDPayload:
                    object input = null;

                    try {
                        using (MemoryStream ms = new MemoryStream(c.RawData))
                        using (StreamReader sr = new StreamReader(ms, Encoding.UTF8, false, 1024, true))
                        using (JsonTextReader jtr = new JsonTextReader(sr))
                            input =
                                CurrentCommand.InputType != null ? Frontend.Serializer.Deserialize(jtr, CurrentCommand.InputType) :
                                Frontend.Serializer.Deserialize<dynamic>(jtr);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.ERR, "frontend-ws", e.ToString());
                        Close("error on cmd data parse");
                        break;
                    }

                    RunCommand(input);
                    break;


                case EState.WaitForData:
                    // TODO: Handle receiving responses!
                    State = EState.WaitForType;
                    break;


                default:
                    Close("unknown state");
                    break;
            }
        }

        enum EState {
            Invalid = -1,

            WaitForType,

            WaitForCMDID,
            WaitForCMDPayload,

            WaitForData,
        }

    }
}
