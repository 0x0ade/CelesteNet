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
  TCPPingMs: number, UDPPingMs: number?,
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
    this.ep = "/api/players";
    /** @type {PlayerData[]} */
    this.data = [];
    this.input = null;
    frontend.sync.register("sess_join", data => this.playerJoin(data));
    frontend.sync.register("sess_leave", data => this.playerLeave(data));
  }

  playerJoin(data) {
    this.data.push(data);
    this.rebuildList();
    this.render(null);
  }

  playerLeave(data) {
    let idx = this.data.findIndex(p => p.ID == data.ID);
    if (idx != -1)
      this.data.splice(idx, 1);
    this.rebuildList();
    this.render(null);
  }

  render(el) {
    return this.el = rd$(el || this.el)`
    <div class="panel" ${rd.toggleClass("panelType", "panel-" + this.id)}=${true}>
      ${el => this.renderHeader(el)}
      ${mdcrd.progress(this.progress)}
      ${el => this.renderInput(el)}
      ${el => this.renderBody(el)}
    </div>`;
  }

  renderInput(el) {
    // Render input only once.
    if (this.elInput)
      return this.elInput;

    return this.elInput = rd$(el || this.elInput)`
      <div class="panel-input">
      ${mdcrd.textField("", "", null, () => { this.refresh(); })}
      ${mdcrd.iconButton("Clear", "clear", () => { this.input.value = ""; this.refresh(); })}
      </div>`;
  }

  async update() {
    this.data = await fetch(this.ep).then(r => r.json());
    this.rebuildList();
  }

  rebuildList() {
    // @ts-ignore
    this.input = this.elInput.getElementsByTagName("input")[0];
    let filter = this.input.value.trim().toLowerCase();

    this.list = this.data.filter(p => filter == "" || p.FullName.toLowerCase().indexOf(filter) >= 0).map(p => el => {
      el = mdcrd.list.item(el => rd$(el)`
        <span>
        <b>${p.FullName}</b> <i>(#${p.ID})</i><br>
        ${p.Name}<br>
        ${p.DisplayName !== p.FullName ? p.UID : this.frontend.censor(p.UID)}<br>
        ${this.frontend.censor(p.Connection)}<br>
        ${el => !p.TCPDownlinkBpS ? rd$(el)`<span></span>` :
          rd$(el)`<span>
            <code>Ping:${` ${p.TCPPingMs ? `${p.TCPPingMs}ms` : '-'} TCP | ${p.UDPPingMs ? `${p.UDPPingMs}ms` : '-'} UDP`}</code><br>
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
