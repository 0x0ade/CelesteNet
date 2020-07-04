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
  Name: string,
  Version: string,
  Context: string
}} AssemblyInfo
 */

export class FrontendAssembliesPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Assemblies";
    this.ep = "/asms";
    /** @type {AssemblyInfo[]} */
    this.data = [];
  }

  async update() {
    this.data = await fetch(this.ep).then(r => r.json()).then(r =>
      r.sort((a, b) => {
        a = a.Context || "Default";
        b = b.Context || "Default";
        return a == "Default" ? 1 : b == "Default" ? -1 : a.localeCompare(b);
      })
    );

    // @ts-ignore
    this.list = this.data.map(asm => [
      el => rd$(el)`
      <span>
        <b>${asm.Name}</b>${" " + asm.Version}<br>
        ${asm.Context}
      </span>`
    ]);

    this.frontend.dom.setContext(this.frontend.dom.renderSettingsButton(null),
      [ null, "CelesteNet.Server", () => this.frontend.dialog.settings("CelesteNet.Server") ],
      ...this.data
        .map(asm => asm.Name)
        .filter(name => name.startsWith("CelesteNet.Server.") && name.endsWith("Module"))
        .filter((v, i, s) => s.indexOf(v) === i)
        .map(name => [ null, name, () => this.frontend.dialog.settings(name) ])
    );
  }

}
