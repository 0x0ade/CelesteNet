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
    Name: string,
    Reason: string,
    From: number,
    To: number
  },
  Kicks: {
    Reason: string,
    From: number
  }[]
}} UserInfo
 */

export class FrontendPlayersPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Accounts";
    this.ep = "/userinfos?from=0&count=100000";
    /** @type {UserInfo[]} */
    this.data = [];

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Reload", "refresh",
        () => {
          this.refresh();
        }
      ],

      [
        "Toggle Clutter", this.frontend.settings.accountsClutter ? "visibility" : "visibility_off",
        () => {
          this.frontend.settings.accountsClutter = !this.frontend.settings.accountsClutter;
          this.frontend.settings.save();
          this.actions[1][1] = this.frontend.settings.accountsClutter ? "visibility" : "visibility_off";
          this.refresh();
        }
      ]
    ];
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
    this.list = this.data.filter(p => this.frontend.settings.accountsClutter || p.Ban || (p.Kicks && p.Kicks.length) || (p.Tags && p.Tags.length)).map(p => el => {
      el = mdcrd.list.item(el => {
        el = rd$(el)`<span></span>`;
        const list = new RDOMListHelper(el);
        list.add("name", el => rd$(el)`<span><b>${p.Name || "?"}</b>${(p.Discrim ? "#" + p.Discrim : "") + " "} <i>(${p.Name ? p.UID : this.frontend.censor(p.UID)})</i></span>`);
        if (p.Key)
          list.add("key", el => rd$(el)`<span><br><b>Key: </b>${"#" + this.frontend.censor(p.Key)}</span>`);
        if (p.Tags && p.Tags.length > 0)
          list.add("tags", el => rd$(el)`<span><br><b>Tags: </b>${p.Tags.join(", ")}</span>`);
        if (p.Ban)
          list.add("ban", el => rd$(el)`<span><br><b>Ban: </b>${(p.Ban.Name ?? "?") + ": " + this.frontend.utils.datetime(p.Ban.From) + ": " + p.Ban.Reason}</span>`);
        if (p.Kicks && p.Kicks.length) {
          list.add("kicks", el => rd$(el)`<span><br><b>Kicks: </b>${p.Kicks.length}</span>`);
          const kick = p.Kicks[p.Kicks.length - 1];
          list.add("lastkick", el => rd$(el)`<span><br><b>Last Kick: </b>${this.frontend.utils.datetime(kick.From) + ": " + kick.Reason}</span>`);
        }
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
