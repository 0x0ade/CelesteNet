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
  UID: string,
  Name: string,
  Discrim: string,
  Tags: string[],
  Key: string,
  Ban: {
    Reason: string,
    From: number,
    To: number
  }
}} UserInfo
 */

export class FrontendPlayersPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Accounts";
    this.ep = "/userinfos";
    /** @type {UserInfo[]} */
    this.data = [];
  }

  async update() {
    this.data = (await fetch(this.ep).then(r => r.json())).sort((a, b) => {
      if (!a.Name && b.Name)
        return 1;
      if (a.Name && !b.Name)
        return -1;
      return a.Name.localeCompare(b.Name);
    });
    // @ts-ignore
    this.list = this.data.map(p => el => {
      el = mdcrd.list.item(el => {
        el = rd$(el)`<span></span>`;
        const list = new RDOMListHelper(el);
        list.add("name", el => rd$(el)`<span><b>${p.Name || "?"}</b>${(p.Discrim ? "#" + p.Discrim : "") + " "} <i>(${p.UID})</i></span>`);
        if (p.Key)
          list.add("key", el => rd$(el)`<span><br><b>Key: </b>${"#" + p.Key}</span>`);
        if (p.Tags && p.Tags.length > 0)
          list.add("tags", el => rd$(el)`<span><br><b>Tags: </b>${p.Tags.join(", ")}</span>`);
        if (p.Ban)
          list.add("ban", el => rd$(el)`<span><br><b>Ban: </b>${this.frontend.utils.datetime(p.Ban.From) + ": " + p.Ban.Reason}</span>`);
        list.end();
        return el;
      })(el);

      this.frontend.dom.setContext(el,
        [ "gavel", p.Ban ? `Unban ${p.Name || p.UID}` : `Ban ${p.Name || p.UID}`, () => {
          if (!p.Ban) {
            this.frontend.dialog.ban(p.UID);
          } else {
            this.frontend.sync.run("unban", p.UID);
          }
        } ]
      );

      return el;
    });
  }

}
