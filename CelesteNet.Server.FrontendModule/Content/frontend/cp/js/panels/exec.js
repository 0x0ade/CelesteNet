//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";
import { FrontendCMDPanel } from "../panels/cmd.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendExecPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Run C#";
    this.ep = "/api/exec";

    this.data = "";

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Run", "play_arrow",
        () => {
          this.send();
        }
      ]
    ];
  }

  async update() {
  }

  async send() {
    this.data = this.elBody.querySelector("textarea").value;
    let result = await fetch(this.ep, {
      method: "post",
      body: this.data
    }).then(r => r.text());
    /** @type {FrontendCMDPanel} */
    const cmdp = FrontendCMDPanel["instance"];
    if (cmdp)
      cmdp.log(`// EXEC: ${result}`);
  }

  renderBody(el) {
    let first = !this.elBody;
    this.elBody = rd$(el || this.elBody)`
    <div class="panel-text">
      <label class="mdc-text-field mdc-text-field--outlined mdc-text-field--textarea mdc-text-field--no-label">
        <span class="mdc-text-field__resizer">
          <textarea class="mdc-text-field__input" rows="8" cols="40" aria-label="Label"></textarea>
        </span>
        <span class="mdc-notched-outline">
          <span class="mdc-notched-outline__leading"></span>
          <span class="mdc-notched-outline__trailing"></span>
        </span>
      </label>
    </div>`;

    let textarea = this.elBody.querySelector("textarea");
    textarea.value = this.data;

    if (first) {
      textarea.addEventListener("keypress", e => {
        if (!e.repeat && e.ctrlKey && e.code === "Enter")
          this.send();
      });
    }

    return this.elBody;
  }

}
