//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendEndpointsPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Endpoints";
    this.ep = "/api/eps";
  }

  async update() {
    this.list = await fetch(this.ep)
      .then(r => r.json())
      .then(r => r.map(ep => [
        el => rd$(el)`<span>
          <b>${ep.Name}</b>
          <a href=${"/api" + (ep.PathExample ? `${ep.Path}${ep.PathExample}` : ep.Path)}>
            <code>${"/api" + (ep.PathHelp ? `${ep.Path}${ep.PathHelp}` : ep.Path)}</code>
          </a><br>
          ${ep.Info}
        </span>`
      ]));
  }

}
