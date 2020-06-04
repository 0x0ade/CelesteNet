//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

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

    frontend.sync.register("chat", data => this.log(data.Text, data.Color, data.ID));
  }

  async update() {
    this.list = await fetch(this.ep)
      .then(r => r.json())
      .then(r => r.map(data => this.createEntry(data.Text, data.Color, data.ID)));
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
   * @param {number} [id]
   */
  createEntry(text, color, id) {
    return el => {
      el = mdcrd.list.item(text)(el);
      if (color && color.toLowerCase() !== "#ffffff")
        el.style.color = color;
      return el;
    };
  }

  /**
   * @param {string} text
   * @param {string} [color]
   * @param {number} [id]
   */
  log(text, color, id) {
    // @ts-ignore
    this.list.push(this.createEntry(text, color, id));
    this.render(null);
    this.elBody.scrollTop = this.elBody.scrollHeight;
  }

}
