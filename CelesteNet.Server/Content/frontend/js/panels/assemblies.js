//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
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
  }

  async update() {
    this.list = await fetch("/asms")
      .then(r => r.json())
      .then(r => r.map(asm => [
        el => rd$(el)`
        <span>
          <b>${asm.Name}</b>${" " + asm.Version}
        </span>`
      ]));
  }

}
