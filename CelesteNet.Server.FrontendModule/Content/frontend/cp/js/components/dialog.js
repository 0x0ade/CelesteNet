//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

export class FrontendDialog {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;
  }

  async start() {
  }

  ban(...uids) {
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

    let el = this.elPopupBan = mdcrd.dialog({
      title: "Ban",
      body: el => rd$(el)`
      <div>
        ${group(
          row("UIDs:"),
          ...(uids.filter(uid => uid).map(uid => el => {
            el = mdcrd.checkbox(uid, true)(el);
            const input = el.querySelector("input");
            input.classList.add("ban-uid");
            input.value = uid;
            return el;
          }))
        )}

        ${group(
          row("Reason:", el => {
            el = mdcrd.textField("", "", null, e => {
              if (e.keyCode === 13) {
                this.elPopupBan["MDCDialog"].close("0");
              }
            })(el);
            const input = el.querySelector("input");
            input.id = "ban-reason";
            return el;
          }),
        )}
      </div>`,
      defaultButton: "yes",
      buttons: ["OK"],
    })(this.elPopupBan);

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<{ uids: string[], reason: string }>} */
    let dialog = el["MDCDialog"];

    dialog.open();

    // Blame the Material Design Web Components checkbox stealing focus for this horrible hack.
    setTimeout(() => el.querySelector("input#ban-reason")["focus"](), 200);

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && {
        uids: Array.from(el.querySelectorAll("input.ban-uid")).map(el => el["checked"] ? el["value"] : null).filter(uid => uid),
        reason: el.querySelector("input#ban-reason")["value"],
    })));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      ban => {
        if (!ban || !(ban.reason = ban.reason.trim()))
          return;
        for (let uid of ban.uids)
          this.frontend.sync.run("ban", { UID: uid, Reason: ban.reason });
      }
    );

    return dialog;
  }

}
