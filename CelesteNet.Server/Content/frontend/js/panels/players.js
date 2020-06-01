//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendPlayersPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Players";
    this.ep = "/players";
  }

  async update() {
    this.list = await fetch(this.ep)
      .then(r => r.json())
      .then(r => r.map(p => [
        el => rd$(el)`
        <span>
        <b>#${p.ID}${": "}</b>${p.FullName}<br>
        ${p.Name}<br>
        ${p.Connection}
        </span>`
      ]));
  }

}
