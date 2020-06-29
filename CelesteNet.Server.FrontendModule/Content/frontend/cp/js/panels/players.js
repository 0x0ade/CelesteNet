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
  Connection: string
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
        ${p.UID}<br>
        ${p.Connection}
        </span>`
      )(el);

      this.frontend.dom.setContext(el,
        [ "error_outline", `Kick ${p.FullName}`, () => this.frontend.sync.run("kick", p.ID) ],
        [ "gavel", `Ban ${p.FullName}` ]
      );

      return el;
    });
  }

}
