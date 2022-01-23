//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";
import { FrontendPlayersPanel } from "./players.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/**
@typedef {{
  ID: number,
  Name: string,
  Players: number[],
  IsPrivate: boolean
}} ChannelData
 */

export class FrontendChannelsPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Channels";
    this.ep = "/api/channels";
    /** @type {ChannelData[]} */
    this.data = [];
  }

  async update() {
    this.data = await fetch(this.ep).then(r => r.json());

    // @ts-ignore
    this.list = this.data.map(c => el => {
      el = mdcrd.list.item(el => rd$(el)`
        <span>
        <b>${c.Name}</b> <i>(#${c.ID}${c.IsPrivate ? ", private" : ""})</i><br>
        ${el => {
          el = rd$(el)`<ul></ul>`;
          let list = new RDOMListHelper(el);
          for (let pid of c.Players) {
            const player = FrontendPlayersPanel["instance"].data.find(p => p.ID == pid);
            let name = player && player.FullName;
            // TODO: Rerender el on missing player only once! Otherwise render -> refresh -> render -> refresh...
            if (!name) {
              // FrontendPlayersPanel["instance"].refresh().then(() => this.render(null));
              name = "?";
            }
            list.add(pid, el => rd$(el)`<li><b>${name}</b> <i>(#${pid})</i></li>`);
          }
          list.end();
          return el;
        }}
        </span>`
      )(el);

      this.frontend.dom.setContext(el,
        [ "error_outline", `Dissolve ${c.Name}`, () => this.frontend.sync.run("dissolve", c.ID) ]
      );

      return el;
    });
  }

}
