//@ts-check
import { FrontendStatusPanel } from "../panels/status.js";
import { FrontendCMDPanel } from "../panels/cmd.js";

export class FrontendSync {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;

    /** @type {WebSocket} */
    this.ws = null;
    this.resync = this.resync.bind(this);
    this.onopen = this.onopen.bind(this);
    this.onclose = this.onclose.bind(this);
    this.onerror = this.onerror.bind(this);
    this.onmessage = this.onmessage.bind(this);

    this.state = "invalid";
    this.status = "init";

    /** @type {{resolve: (value?: any) => void; reject: (reason?: any) => void;}[]} */
    this.awaiting = [];

    this.logAllData = false;
  }

  /** @type {string} */
  get state() {
    return this._state;
  }
  set state(value) {
    this._state = value;
    if (this.sp)
      this.sp.render(null);
  }

  /** @type {string} */
  get status() {
    return this._status;
  }
  set status(value) {
    this._status = value;
    if (this.sp)
      this.sp.render(null);
    if (this.cmdp)
      this.cmdp.log(`// Status: ${value}`);
  }

  run(cmd, data) {
    return new Promise((resolve, reject) => {
      this.awaiting.push({
        resolve: resolve,
        reject: reject
      });

      try {
        const ws = this.ws;
        ws.send("cmd");
        ws.send(cmd);
        ws.send(
          typeof(data) == "object" ? JSON.stringify(data) :
          typeof(data) == "undefined" ? "" :
          data
        );
      } catch (e) {
        reject(e);
      }
    });
  }

  resync() {
    /** @type {FrontendCMDPanel} */
    this.cmdp = FrontendCMDPanel["instance"];
    /** @type {FrontendStatusPanel} */
    this.sp = FrontendStatusPanel["instance"];

    if (!this.ws || this.ws.readyState === WebSocket.CLOSED) {
      this.status = "connecting";
      let ws = this.ws = new WebSocket(`ws://${window.location.host}/ws`);
      this.resyncPromise = new Promise(resolve => this.resyncPromiseResolve = resolve);
      ws.onopen = this.onopen;
      ws.onclose = this.onclose;
      ws.onerror = this.onerror;
      ws.onmessage = this.onmessage;
    }

    if (this.resyncTimeout)
      clearTimeout(this.resyncTimeout);
    this.resyncTimeout = setTimeout(this.resync, 500);

    return this.resyncPromise;
  }

  close(reason) {
    this.status = "closing";
    this.state = "invalid";
    this.ws.close(10000, reason);
    this.resync();
  }

  /**
   * @param {Event} e
   */
  onopen(e) {
    this.status = "open";
    console.log("sync open", e);
    this.resyncPromiseResolve();

    this.state = "waitForType";
  }

  /**
   * @param {CloseEvent} e
   */
  onclose(e) {
    this.status = e.reason ? `closed: ${e.reason}` : "closed";
    console.log("sync closed", e);

    for (let p of this.awaiting) {
      p.reject(new Error("Connection closed."));
    }
    this.awaiting = [];
  }

  /**
   * @param {Event} e
   */
  onerror(e) {
    this.status = "error";
    console.log("sync error", e);
  }

  /**
   * @param {MessageEvent} e
   */
  onmessage(e) {
      console.log("sync msg", e);

      switch (this.state) {
        case "waitForType":
          switch (e.data) {
            case "cmd":
              this.state = "waitForCMDID";
              break;

            case "data":
              this.state = "waitForData";
              break;

            default:
              this.close("unknown type");
              break;
          }
          break;


        case "waitForCMDID":
          console.log("sync cmd", e.data);
          // TODO: AAAAAAAAAAAAAAAAAA
          this.state = "waitForCMDID";
          break;


        case "waitForCMDPayload":
          console.log("sync payload", e.data);
          // TODO: AAAAAAAAAAAAAAAAAA
          this.state = "waitForType";
          break;


        case "waitForData":
          const data = JSON.parse(e.data);
          console.log("sync data", data);

          const a = this.awaiting.splice(0, 1)[0];
          if (a) {
            a.resolve(data);
            if (this.logAllData) {
              this.cmdp.log(data);
            }
          } else {
            this.cmdp.log(data);
          }

          this.state = "waitForType";
          break;


        default:
          this.close("unknown state");
          break;
      }
  }

}
