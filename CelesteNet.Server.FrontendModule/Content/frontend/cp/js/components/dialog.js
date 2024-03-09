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

  kick(fullname, id) {
    const input = (id, gen) => el => {
      el = gen(el);
      el.id = id;
      let input = el.querySelector("input");
      let label = el.querySelector("label");
      input.id = `${id}-kick-input`;
      if (label)
        label.setAttribute("for", input.id);
      return el;
    }
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
      title: `Kick ${fullname} (#${id})`,
      body: el => rd$(el)`
      <div>
        ${group(
            row(`Player ID: ${id}`),
          )
        }

        ${group(
            row("Reason:",
              input("kick-reason",
                mdcrd.textField("", "", null, e => {
                  if (e.keyCode === 13) {
                    this.elPopupKick["MDCDialog"].close("0");
                  }
                }))
              ),
            row(
              input("kick-quiet", mdcrd.checkbox("Quiet (no broadcast)", false))
            )
          )
        }
      </div>`,
      defaultButton: "yes",
      buttons: ["OK"],
    })(this.elPopupKick);

    document.body.appendChild(el);

    /** @type {import("@material/dialog").MDCDialog & Promise<string>} */
    let dialog = el["MDCDialog"];

    dialog.open();

    // Blame the Material Design Web Components checkbox stealing focus for this horrible hack.
    setTimeout(() => el.querySelector("#kick-reason input")["focus"](), 200);

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && {
      reason: el.querySelector("#kick-reason input")["value"],
      quiet: el.querySelector("#kick-quiet input")["checked"],
    }), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      kick => {
        if (!kick || !(kick.reason = kick.reason.trim()))
          return;
        this.frontend.sync.run("kickwarn", { ID: id, Reason: kick.reason, Quiet: kick.quiet });
      }
    );

    return dialog;
  }

  ban(fullname, id, ...uids) {
    const input = (id, cls, val, gen) => el => {
      el = gen(el);
      el.id = id;
      if (cls)
        el.classList.add(cls);
      let input = el.querySelector("input");
      let label = el.querySelector("label");
      input.id = `${id}-ban-input`;
      input.value = val;
      if (label)
        label.setAttribute("for", input.id);
      return el;
    }
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
      title: `Ban ${fullname} ${id > 0 ? `(#${id})` : ""}`,
      body: el => rd$(el)`
      <div>
        ${group(
          row("UIDs:"),
          ...(uids.filter(uid => uid).map(uid => input(
              `ban-uid-${uids.indexOf(uid)}`,
              "ban-uid",
              uid,
              mdcrd.checkbox(uid, true),
            ))
          )
        )}

        ${group(
          row("Reason:", input(
            "ban-reason",
            "",
            "",
            mdcrd.textField("", "", null, e => {
                if (e.keyCode === 13) {
                  this.elPopupBan["MDCDialog"].close("0");
                }
              })
            )
          ),
          row(input("ban-quiet", "", "", mdcrd.checkbox("Quiet (no broadcast)", false))),
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
    setTimeout(() => el.querySelector("#ban-reason input")["focus"](), 200);

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && {
        uids: Array.from(el.querySelectorAll(".ban-uid input")).map(el => el["checked"] ? el["value"] : null).filter(uid => uid),
        reason: el.querySelector("#ban-reason input")["value"],
        quiet: el.querySelector("#ban-quiet input")["checked"],
    }), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      ban => {
        if (!ban || !(ban.reason = ban.reason.trim()))
          return;
        this.frontend.sync.run("ban", { UIDs: ban.uids, Reason: ban.reason, Quiet: ban.quiet });
      }
    );

    return dialog;
  }

  banExt(fullname, id, opts, connectionUID) {
    const input = (id, gen) => el => {
      el = gen(el);
      el.id = id;
      let input = el.querySelector("input");
      let label = el.querySelector("label");
      input.id = `${id}-banext-input`;
      if (label)
        label.setAttribute("for", input.id);
      return el;
    }
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

    let el = this.elPopupBanExt = mdcrd.dialog({
      title: `Ban ${fullname} ${id > 0 ? `(#${id})` : ""}`,
      body: el => rd$(el)`
      <div>
        ${group(
          row( el => {
            let optKeys = opts.map(e => e[0] + '#' + e[1]);
            el = mdcrd.dropdown("connInfo", optKeys, 0)(el);
            el.id = "connInfo-select";
            return el;
          }),
          row(input("banext-connUID", mdcrd.checkbox(`Also ban Conn UID: ${connectionUID}`, true))),
        )}

        ${group(
          row("Reason:", input("banext-reason", mdcrd.textField("", "", null, e => {
              if (e.keyCode === 13) {
                this.elPopupBanExt["MDCDialog"].close("0");
              }
            }))),
          row( input("banext-quiet", mdcrd.checkbox("Quiet (no broadcast)", false))),
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
    setTimeout(() => el.querySelector("input#ban-reason")["focus"](), 200);

    let promise = new Promise(
        resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"]["action"] === "0" && {
          selection: el.querySelector("#connInfo-select .mdc-select__selected-text")["value"],
          banConnUID: el.querySelector("#banext-connUID input")["checked"],
          reason: el.querySelector("#banext-reason input")["value"],
          quiet: el.querySelector("#banext-quiet input")["checked"],
      }), { once: true }));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    dialog.then(
      ban => {
        if (!ban || !(ban.reason = ban.reason.trim()))
          return;
        this.frontend.sync.run("banext", { ID: id, ConnUID: connectionUID, ConnInfo: ban.selection, BanConnUID: ban.banConnUID, Reason: ban.reason, Quiet: ban.quiet });
      }
    );

    return dialog;
  }

}
