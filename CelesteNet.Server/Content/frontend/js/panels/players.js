//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
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
  Name: string,
  FullName: string,
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
    this.list = this.data.map(p => [
      el => rd$(el)`
      <span>
      <b>#${p.ID}${": "}</b>${p.FullName}<br>
      ${p.Name}<br>
      ${p.Connection}
      </span>`
    ]);
  }

}
