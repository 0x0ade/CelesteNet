using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class FrontendWebSocket : WebSocketBehavior {

        // Each connection creates one instance of this.

        public Frontend Frontend;

        public string SessionKey = "";
        public bool IsAuthorized => Frontend.CurrentSessionKeys.Contains(SessionKey);
        public bool IsAuthorizedExec => Frontend.CurrentSessionExecKeys.Contains(SessionKey);

        public WSCommands Commands;

        public IPEndPoint? CurrentEndPoint { get; private set; }

        private EState State = EState.Invalid;
        private WSCMD? CurrentCommand;

        public readonly object MessageLock = new();

#pragma warning disable CS8618 // Fully initialized after construction.
        public FrontendWebSocket() {
#pragma warning restore CS8618
            Commands = new(this);
        }

        private void Close(string reason) {
            State = EState.Invalid;
            Close(CloseStatusCode.Normal, reason);
        }

        public void SendRawString(string data) {
            Send(data);
        }

        public void SendRawObject(object data) {
            using MemoryStream ms = new();
            using (StreamWriter sw = new(ms, CelesteNetUtils.UTF8NoBOM, 1024, true))
            using (JsonTextWriter jtw = new(sw))
                Frontend.Serializer.Serialize(jtw, data);

            ms.Seek(0, SeekOrigin.Begin);

            using StreamReader sr = new(ms, Encoding.UTF8, false, 1024, true);
            Send(sr.ReadToEnd());
        }

        public void SendCommand(string command, object data) { 
            lock (MessageLock) 
                try { 
                    SendRawString("cmd");
                    SendRawString(command);
                    SendRawObject(data);
                } catch (Exception e) {
                    Logger.Log(LogLevel.VVV, "frontend", $"Failed sending command:\n{CurrentEndPoint}\n{e}");
                }
        }

        private void RunCommand(object? input) {
            if (CurrentCommand == null)
                throw new Exception("Cannot run no command.");
            if (Frontend == null)
                throw new Exception("Not ready.");

            try {
                object? output = CurrentCommand.Run(input);

                using MemoryStream ms = new();
                using (StreamWriter sw = new(ms, CelesteNetUtils.UTF8NoBOM, 1024, true))
                using (JsonTextWriter jtw = new(sw))
                    Frontend.Serializer.Serialize(jtw, output);

                ms.Seek(0, SeekOrigin.Begin);

                Send("data");
                using StreamReader sr = new(ms, Encoding.UTF8, false, 1024, true);
                Send(sr.ReadToEnd());

                State = EState.WaitForType;
            } catch (Exception e) {
                Logger.Log(LogLevel.ERR, "frontend-ws", e.ToString());
                Close("error on cmd run");
            }
        }

        protected override void OnOpen() {
            base.OnOpen();
            Logger.Log(LogLevel.INF, "frontend-ws", $"Opened connection: {UserEndPoint}");
            CurrentEndPoint = UserEndPoint;
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
            if (Frontend == null)
                throw new Exception("Not ready.");

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
                    Logger.Log(LogLevel.DEV, "frontend-ws", $"CMD: {UserEndPoint} - {c.Data}");
                    WSCMD? cmd = Commands.Get(c.Data);
                    if (cmd == null) {
                        Close("unknown cmd");
                        break;
                    }

                    if (cmd.MustAuth && !IsAuthorized) {
                        Close("unauthorized");
                        break;
                    }

                    if (cmd.MustAuthExec && !IsAuthorizedExec) {
                        Close("unauthorized");
                        break;
                    }

                    CurrentCommand = cmd;
                    State = EState.WaitForCMDPayload;
                    break;


                case EState.WaitForCMDPayload:
                    object? input = null;

                    try {
                        using MemoryStream ms = new(c.RawData);
                        using StreamReader sr = new(ms, Encoding.UTF8, false, 1024, true);
                        using JsonTextReader jtr = new(sr);
                        input =
                            CurrentCommand?.InputType != null ? Frontend.Serializer.Deserialize(jtr, CurrentCommand.InputType) :
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
