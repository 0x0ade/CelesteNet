//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendAssembliesPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Assemblies";
    this.ep = "/asms";
  }

  async update() {
    this.list = await fetch(this.ep)
      .then(r => r.json())
      .then(r =>
        r.sort((a, b) => {
          a = a.Context || "Default";
          b = b.Context || "Default";
          return a == "Default" ? 1 : b == "Default" ? -1 : a.localeCompare(b);
        }).map(asm => [
          el => rd$(el)`
          <span>
            <b>${asm.Name}</b>${" " + asm.Version}<br>
            ${asm.Context}
          </span>`
        ])
      );
  }

}
