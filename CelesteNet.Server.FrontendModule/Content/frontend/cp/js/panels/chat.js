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
  PlayerID: number,
  Color: string,
  Text: string
}} ChatData
 */

export class FrontendChatPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Chat";
    this.ep = "/chatlog";

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Fetch Log", "history",
        () => {
          this.refresh();
        }
      ],

      [
        "Clear", "clear",
        () => {
          this.list = [];
          this.render();
        }
      ]
    ];

    /** @type {[string | ((el: HTMLElement) => HTMLElement), () => void][] | [string | ((el: HTMLElement) => HTMLElement)][]} */
    this.list = [];
    /** @type {ChatData[]} */
    this.data = [];

    frontend.sync.register("chat", data => this.log(data.Text, data.Color, data));
  }

  async refresh() {
    await super.refresh();
    this.elBody.scrollTop = this.elBody.scrollHeight;
  }

  async update() {
    this.data = await fetch(this.ep + "?count=100").then(r => r.json());
    // @ts-ignore
    this.list = this.data.map(data => this.createEntry(data.Text, data.Color, data));
  }

  render(el) {
    return this.el = rd$(el || this.el)`
    <div class="panel" ${rd.toggleClass("panelType", "panel-" + this.id)}=${true}>
      ${el => this.renderHeader(el)}
      ${mdcrd.progress(this.progress)}
      ${el => this.renderBody(el)}
      ${el => this.renderInput(el)}
    </div>`;
  }

  renderInput(el) {
    // Render input only once.
    if (this.elInput)
      return this.elInput;

    return this.elInput = rd$(el || this.elInput)`
    <div class="panel-input">
      ${mdcrd.textField("", "", null, e => {
        if (e.keyCode === 13) {
          this.send();
        }
      })}
    </div>`
  }

  send(text) {
    /** @type {HTMLInputElement} */
    const input = this.elInput.getElementsByTagName("input")[0];
    text = text || input.value.trim();
    if (!text)
      return;

    this.progress += 2;
    this.render();

    this.frontend.sync.run("chat", text).then(
      data => {
        this.progress -= 2;
        this.render();
      },
      () => {
        this.progress -= 2;
        this.render();
      }
    );

    input.value = "";
  }

  /**
   * @param {string} text
   * @param {string} [color]
   * @param {ChatData} [data]
   * @returns {(el: HTMLElement) => HTMLElement}
   */
  createEntry(text, color, data) {
    return el => {
      el = mdcrd.list.item(text)(el);
      if (color && color.toLowerCase() !== "#ffffff")
        el.style.color = color;
      else
        el.style.color = "#000000";

      let opts = [];

      if (data) {
        opts = [
          ...opts,
          [ "delete", `Delete #${data.ID}`, () => this.frontend.sync.run("chatedit", {
            ID: data.ID,
            Color: "#ee2233",
            Text: "*deleted*"
          }) ]
        ];

        if (data.PlayerID && data.PlayerID !== this.frontend.MAX_INT) {
          const player = FrontendPlayersPanel["instance"].data.find(p => p.ID == data.PlayerID);
          // TODO: Rerender el on missing player only once! Otherwise render -> refresh -> render -> refresh...
          if (!player)
            FrontendPlayersPanel["instance"].refresh();
          const name = player && player.FullName || ("#" + data.PlayerID);

          opts = [
            ...opts,
            [ "error_outline", `Kick ${name}` ],
            [ "gavel", `Ban ${name}` ]
          ];
        }
      }

      this.frontend.dom.setContext(el, ...opts);

      return el;
    };
  }

  /**
   * @param {string} text
   * @param {string} [color]
   * @param {ChatData} [data]
   */
  log(text, color, data) {
    // @ts-ignore
    this.list.push(this.createEntry(text, color, data));
    this.render(null);
    this.elBody.scrollTop = this.elBody.scrollHeight;
  }

}
