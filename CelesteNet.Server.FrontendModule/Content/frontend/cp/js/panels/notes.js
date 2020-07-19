//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendNotesPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Notes";
    this.ep = "/notes";

    this.data = "";

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Reload", "refresh",
        () => {
          this.refresh();
        }
      ],

      [
        "Save", "save",
        () => {
          this.save();
        }
      ]
    ];
  }

  async update() {
    this.data = await fetch(this.ep).then(r => r.text());
  }

  async save() {
    this.data = this.elBody.querySelector("textarea").value;
    await fetch(this.ep, {
      method: "post",
      body: this.data
    });
    await this.refresh();
  }

  renderBody(el) {
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

    this.elBody.querySelector("textarea").value = this.data;

    return this.elBody;
  }

}
