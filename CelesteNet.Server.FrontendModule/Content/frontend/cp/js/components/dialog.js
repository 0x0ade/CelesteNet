//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/** @type {typeof import("monaco-editor")} */
const monaco = window["monaco"]; // monaco-editor

export class FrontendDialog {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;
  }

  async start() {
  }

  settingsCP() {
    const row = (body) => el => rd$(el)`<span class="row">${body}</span>`;

    const form = (id, body) => el => {
      el = rd$(el)`<form>${body}</form>`;
      el.id = id;
      return el;
    }

    const fieldset = (...items) => el => {
      el = rd$(el)`<fieldset class="settings-group"></fieldset>`;

      let list = new RDOMListHelper(el);
      for (let i in items) {
        list.add(i, items[i]);
      }
      list.end();

      return el;
    }

    const input = (formid, id, gen) => el => {
      el = gen(el);
      el.id = id;
      let input = el.querySelector("input");
      let label = el.querySelector("label");
      input.id = `${id}-input`;
      label.setAttribute("for", input.id);
      input.setAttribute("form", formid);
      label.setAttribute("form", formid);
      return el;
    }

    const s = this.frontend.settings;

    let el = this.elSettingsCP = mdcrd.dialog({
      title: "Settings: Control Panel",
      body: el => rd$(el)`
      <div>
        ${form("settings-form", fieldset(
          row(input("settings-form", "setting-sensitive", mdcrd.checkbox("Show sensitive data", s.sensitive))),
          row(input("settings-form", "setting-minimizeServerMsgs", mdcrd.checkbox("Collapse join messages (MOTD)", s.minimizeServerMsgs)))
        ))}
      </div>`,
      defaultButton: "yes",
      buttons: ["Cancel", "OK"],
    })(this.elSettingsCP);

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<string>} */
    let dialog = el["MDCDialog"];

    dialog.open();

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"]), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      action => {
        if (action !== "1")
          return;
        s.sensitive = el.querySelector("#setting-sensitive input")["checked"];
        s.minimizeServerMsgs = el.querySelector("#setting-minimizeServerMsgs input")["checked"];
        s.save();
        this.frontend.dom.render();
      }
    );

    return dialog;
  }

  settings(module) {
    if (!module)
      module = "CelesteNet.Server";

    /** @type {import("monaco-editor").editor.IStandaloneCodeEditor} */
    let editor;

    let el = this.elPopupSettings = mdcrd.dialog({
      title: `Settings: ${module}`,
      body: el => {
        el = rd$(el)`<div id="settings-container" style="width: 100%; height: calc(100% - 25px);"></div>`;

        editor = el["MonacoEditor"];
        if (!editor) {
          el["MonacoEditor"] = editor = monaco.editor.create(el, {
            value: "",
            language: "yaml"
          });
          window.addEventListener("resize", () => editor.layout());
        }
        setTimeout(() => editor.layout(), 0);

        editor.setValue("# Loading...");
        fetch(`/api/settings?module=${encodeURIComponent(module)}`).then(r => r.text()).then(
          value => editor.setValue(value),
          e => editor.setValue(`# Error: ${e}`)
        );

        return el;
      },
      defaultButton: "yes",
      buttons: ["Cancel", "OK"],
    })(this.elPopupSettings);

    el.classList.add("dialog-fullscreen");

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<string>} */
    let dialog = el["MDCDialog"];

    dialog.escapeKeyAction = "";
    dialog.scrimClickAction = "";

    dialog.open();

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"].action), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      action => {
        if (action !== "1")
          return;
        fetch(`/api/settings?module=${encodeURIComponent(module)}`, {
          method: "post",
          body: editor.getValue()
        }).then(r => r.json()).then(r => {
          if (!r || r.Error) {
            this.frontend.snackbar({ text: `Failed to change settings${r ? ": " + r.Error : "."}` });
          } else {
            this.frontend.snackbar({ text: `Changed settings successfully.` });
          }
        }, e => {
          this.frontend.snackbar({ text: `Failed to change settings: ${e}` });
        });
      }
    );

    return dialog;
  }

  kick(id) {
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

    let el = this.elPopupKick = mdcrd.dialog({
      title: "Kick",
      body: el => rd$(el)`
      <div>
        ${group(
          row(`Player ID: ${id}`),
        )}

        ${group(
          row("Reason:", el => {
            el = mdcrd.textField("", "", null, e => {
              if (e.keyCode === 13) {
                this.elPopupKick["MDCDialog"].close("0");
              }
            })(el);
            const input = el.querySelector("input");
            input.id = "kick-reason";
            return el;
          }),
        )}
      </div>`,
      defaultButton: "yes",
      buttons: ["OK"],
    })(this.elPopupKick);

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<string>} */
    let dialog = el["MDCDialog"];

    dialog.open();

    // Blame the Material Design Web Components checkbox stealing focus for this horrible hack.
    setTimeout(() => el.querySelector("input#kick-reason")["focus"](), 200);

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && el.querySelector("input#kick-reason")["value"]), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      kick => {
        if (!kick || !(kick = kick.trim()))
          return;
        this.frontend.sync.run("kickwarn", { ID: id, Reason: kick });
      }
    );

    return dialog;
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
    }), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      ban => {
        if (!ban || !(ban.reason = ban.reason.trim()))
          return;
        this.frontend.sync.run("ban", { UIDs: ban.uids, Reason: ban.reason });
      }
    );

    return dialog;
  }

  banExt(opts) {
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

    let optKeys = Array.from(Object.getOwnPropertyNames(opts));
    const emptyStrArr = Array.from([""]);

    let el = this.elPopupBanExt = mdcrd.dialog({
      title: "Ban Ext",
      body: el => rd$(el)`
      <div>
        ${group(
          row( el => {
            el = mdcrd.dropdown("primary", optKeys, optKeys[0])(el);
            return el;
          }),
          row( el => {
            el = mdcrd.dropdown("secondary", emptyStrArr.concat(optKeys), "")(el);
            return el;
          }),
        )}

        ${group(
          row("Reason:", el => {
            el = mdcrd.textField("", "", null, e => {
              if (e.keyCode === 13) {
                this.elPopupBanExt["MDCDialog"].close("0");
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
    })(this.elPopupBanExt);

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<{ uids: string[], reason: string }>} */
    let dialog = el["MDCDialog"];

    dialog.open();

    // Blame the Material Design Web Components checkbox stealing focus for this horrible hack.
    //setTimeout(() => el.querySelector("input#ban-reason")["focus"](), 200);

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && {
        selection: Array.from(el.querySelectorAll(".mdc-select__selected-text")).map(e => opts[e.value]),
        reason: el.querySelector("input#ban-reason")["value"],
    }), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      ban => {
        if (!ban || !(ban.reason = ban.reason.trim()))
          return;
        this.frontend.sync.run("banext", { ConnInfo: ban.selection, Reason: ban.reason });
      }
    );

    return dialog;
  }

}
