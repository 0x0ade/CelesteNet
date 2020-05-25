//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
import mdcrd from "../utils/mdcrd.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendAuth {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;
    this.key = "";
  }

  async reauth() {
    let firstTry = true;
    let pass = "";
    while (true) {
      if (!firstTry)
        pass = await this.popup(pass);
      firstTry = false;

      let data;
      try {
        data = await fetch("/auth", {
          method: "post",
          body: JSON.stringify(pass)
        }).then(r => r.json());
      } catch (e) {
        console.error("auth err", e);
        this.frontend.snackbar({ text: `Failed to log in: ${e}` });
        continue;
      }

      if (!data || !data.Key || data.Error) {
        pass = "";
        console.error("auth err", data.Error);
        this.frontend.snackbar({ text: data.Info || `Failed to log in: ${data.Error || "Unknown error."}` });
        continue;
      }

      console.log("auth success", data.Info);
      this.frontend.snackbar({ text: data.Info || "Logged in." });
      this.key = data.Key;
      break;
    }
  }

  popup(value) {
    const row = (label, body) => el => rd$(el)`<span class="row"><span class="label">${label}</span><span class="body">${body}</span></span>`;
    const group = (...items) => el => {
      el = rd$(el)`<ul class="settings-group"></ul>`;

      let list = new RDOMListHelper(el);
      for (let i in items) {
        list.add(i, items[i]);
      }
      list.end();

      return el;
    }

    let el = this.elPopup = mdcrd.dialog({
      title: "Log in",
      body: el => rd$(el)`
      <div>
        ${group(
          row("Password:", el => {
            el = mdcrd.textField("", "", null, e => {
              if (e.keyCode === 13) {
                this.elPopup["MDCDialog"].close();
              }
            })(el);
            const input = el.querySelector("input");
            input.id = "password";
            input.type = "password";
            return el;
          }),

          row("Cookies are being used to keep you logged in."),
        )}
      </div>`,
      defaultButton: "yes",
      buttons: ["OK"],
    })(this.elPopup);

    if (value)
      el.querySelector("input").value = value;

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<string>} */
    let dialog = el["MDCDialog"];

    dialog.escapeKeyAction = "";
    dialog.scrimClickAction = "";
    dialog.open();

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(el.querySelector("input").value)));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    return dialog;
  }

}
