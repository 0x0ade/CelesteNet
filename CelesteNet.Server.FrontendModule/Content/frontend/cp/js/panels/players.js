//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/**
@typedef {{
  ID: number,
  UID: string,
  Name: string,
  FullName: string,
  DisplayName: string,
  Connection: string,
  ConnectionUID: string,
  TCPDownlinkBpS: number?, TCPDownlinkPpS: number?,
  UDPDownlinkBpS: number?, UDPDownlinkPpS: number?,
  TCPUplinkBpS: number?, TCPUplinkPpS: number?,
  UDPUplinkBpS: number?, UDPUplinkPpS: number?
}} PlayerData
 */

export class FrontendPlayersPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Players";
    this.ep = "/players";
    /** @type {PlayerData[]} */
    this.data = [];
  }

  async update() {
    this.data = await fetch(this.ep).then(r => r.json());

    // @ts-ignore
    this.list = this.data.map(p => el => {
      el = mdcrd.list.item(el => rd$(el)`
        <span>
        <b>${p.FullName}</b> <i>(#${p.ID})</i><br>
        ${p.Name}<br>
        ${p.DisplayName !== p.FullName ? p.UID : this.frontend.censor(p.UID)}<br>
        ${this.frontend.censor(p.Connection)}<br>
        ${el => !p.TCPDownlinkBpS ? rd$(el)`<span></span>` :
          rd$(el)`<span>
            <code>TCP ↓:${` ${p.TCPDownlinkBpS.toFixed(3)} BpS | ${p.TCPDownlinkPpS.toFixed(3)} PpS`}</code><br>
            <code>UDP ↓:${` ${p.UDPDownlinkBpS.toFixed(3)} BpS | ${p.UDPDownlinkPpS.toFixed(3)} PpS`}</code><br>
            <code>TCP ↑:${` ${p.TCPUplinkBpS.toFixed(3)} BpS | ${p.TCPUplinkPpS.toFixed(3)} PpS`}</code><br>
            <code>UDP ↑:${` ${p.UDPUplinkBpS.toFixed(3)} BpS | ${p.UDPUplinkPpS.toFixed(3)} PpS`}</code>
          </span>`}
        </span>`
      )(el);

      this.frontend.dom.setContext(el,
        [ "error_outline", `Kick ${p.FullName}`, () => this.frontend.dialog.kick(p.ID) ],
        [ "gavel", `Ban ${p.FullName}`, () => this.frontend.dialog.ban(p.UID, p.ConnectionUID) ]
      );

      return el;
    });
  }

}
