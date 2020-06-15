//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendCMDPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "WebSocket CMD";

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Clear", "clear",
        () => {
          this.list = [];
          this.render();
        }
      ]
    ];

    /** @type {[string | ((el: HTMLElement) => HTMLElement), () => void][] | [string | ((el: HTMLElement) => HTMLElement)][]} */
    // @ts-ignore
    this.list = [
      ["// RAW WebSocket command shell."],
      ["// Emphasis on RAW - use this for debugging only!"],
      ["// Send `help` for a list of all cmds."]
    ];
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
          this.run();
        }
      })}
    </div>`
  }

  run(cmd) {
    /** @type {HTMLInputElement} */
    const input = this.elInput.getElementsByTagName("input")[0];
    cmd = cmd || input.value.trim();
    if (!cmd)
      return;

    this.progress += 2;
    this.log("> " + cmd, (cmd => () => {
      if (input.value === cmd) {
        this.run();
      } else {
        input.value = cmd;
      }
    })(cmd));

    let data = "null";
    const indexOfSplit = cmd.indexOf(" ");
    if (indexOfSplit !== -1) {
      data = cmd.slice(indexOfSplit + 1);
      cmd = cmd.slice(0, indexOfSplit);
    }

    try {
      data = JSON.parse(data);
    } catch (e) {
      this.progress -= 2;
      this.log("// Failed parsing data.");
      return;
    }

    this.frontend.sync.run(cmd, data).then(
      data => {
        this.progress -= 2;
        this.log(data);
      },
      () => {
        this.progress -= 2;
        this.render();
      }
    );

    input.value = "";
  }

  /**
   * @param {string} data
   * @param {() => void} [cb]
   */
  log(data, cb) {
    if (typeof(data) == "object")
      return this.log(JSON.stringify(data, null, 2));

    // @ts-ignore
    this.list.push([data, cb]);
    this.render(null);
    this.elBody.scrollTop = this.elBody.scrollHeight;
  }

}
