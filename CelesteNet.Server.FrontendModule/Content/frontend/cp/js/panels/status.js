//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { DateTime } from "../../../js/deps/luxon.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendStatusPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Status";
    this.ep = "/status";

    this.data = {
      Alive: false,
      StartupTime: 0,
      PlayerCounter: 0,
      Registered: 0,
      Banned: 0,
      Connections: 0,
      PlayersByCon: 0,
      PlayersByID: 0,
      PlayerRefs: 0,
    };

    /** @type {[string | ((el: HTMLElement) => HTMLElement), () => void][] | [string | ((el: HTMLElement) => HTMLElement)][]} */
    this.list = [
      [
        el => rd$(el)`<span>
          <b>Control Panel Sync</b><br>
          ${this.frontend.sync.status || "init"}<br>
          <code>${this.frontend.sync.state || "invalid"}</code>
        </span>`
      ]

    ];

    for (let key in this.data) {
      // @ts-ignore
      this.list.push([
        el => rd$(el)`<span>
          <b>${key}</b>:${" " + (
            key === "StartupTime" ? DateTime.fromMillis(this.data[key]).setLocale("en-GB").toFormat("yyyy-MM-dd HH:mm:ss") :
            this.data[key]
          )}
        </span>`
      ]);
    }
  }

  async update() {
    const dataPrev = this.data;
    let data;

    try {
      data = await fetch(this.ep)
        .then(r => r.json());
      data.Alive = true;
    } catch (e) {
      console.error(e);
      data = data || dataPrev;
      if (data)
        data.Alive = false;
    }

    this.data = data || dataPrev;
  }

  render(el) {
    const progressReal = this.progress;

    if (progressReal === 0) {
      const state = this.frontend.sync.status;
      this.progress =
        (state.startsWith("closed") || state.startsWith("error")) ? 1 :
        (state.startsWith("connecting")) ? 2 :
        (state.startsWith("open")) ? 0 :
        -2;
    }

    el = super.render(el);

    this.progress = progressReal;

    return el;
  }

}
