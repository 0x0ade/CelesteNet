//@ts-check
import mdcrd from "./utils/mdcrd.js";
/** @type {import("material-components-web")} */
const mdc = window["mdc"];
import { FrontendDOM } from "./components/dom.js";
import { FrontendUtils } from "./components/utils.js";
import { FrontendSync } from "./components/sync.js";
import { FrontendDialog } from "./components/dialog.js";

export class Frontend {
  constructor() {
    /** @type {Map<string, HTMLElement>} */
    this.alerts = new Map();
    this.ready = false;
    this.renderable = false;

    this.gid = 0;
  }

  async start() {
    // Init mdc early.
    mdc.autoInit();

    // Who would've known that the dialog component is needed early...
    this.dialog = new FrontendDialog(this);
    await this.dialog.start();

    // Set up all remaining components.
    this.utils = new FrontendUtils(this);

    this.sync = new FrontendSync(this);

    this.dom = new FrontendDOM(this);

    // TODO: AAAAAAAAAAAAA
    for (let id of [
      "status",
      "cmd",
      "assemblies",
      "endpoints"
    ]) {
      const module = await import(`./panels/${id}.js`);
      for (let key of Reflect.ownKeys(module)) {
        let value = module[key];
        try {
          const p = new value(this);
          value.instance = p;
          p.id = p.id || id;
          await this.dom.add(p);
        } catch (e) {}
      }
    }

    await this.dom.start();

    this.renderable = true;
    this.render();

    this.sync.resync();

    document.getElementById("splash-progress-bar").style.transform = "scaleX(1)";

    setTimeout(() => {
      this.ready = true;
    }, 1200);
  }

  render() {
    if (!this.renderable)
      return;

    this.dom.render();
  }

  alert({
    title = "",
    text = "",
    defaultButton = "yes",
    buttons = ["OK"],
    dismissable = true
  }) {
    let key = [title, text, ...buttons].join("####");

    let el = this.alerts.get(key);
    el = mdcrd.dialog({ title, body: mdcrd.markdown(text), defaultButton, buttons })(el);
    this.alerts.set(key, el);
    document.body.appendChild(el);

    
    /** @type {import("@material/dialog").MDCDialog} */
    let dialog = el["MDCDialog"];

    if (!dismissable) {
      // @ts-ignore Outdated .d.ts
      dialog.escapeKeyAction = "";
      // @ts-ignore Outdated .d.ts
      dialog.scrimClickAction = "";
    } else {
      // @ts-ignore Outdated .d.ts
      dialog.escapeKeyAction = "close";
      // @ts-ignore Outdated .d.ts
      dialog.scrimClickAction = "close";
    }

    // @ts-ignore Outdated .d.ts
    dialog.open();

    let promise = new Promise(resolve => el.addEventListener("MDCDialog:closed", e => resolve(e["detail"].action)));
    dialog["then"] = promise.then.bind(promise);
    dialog["catch"] = promise.catch.bind(promise);

    return dialog;
  }

  snackbar({
    text = "",
    action = ""
  }) {
    if (this.snackbarLast) {
      // @ts-ignore Outdated .d.ts
      if (this.snackbarLast.isOpen && this.snackbarLastText === text)
        return;
      // @ts-ignore Outdated .d.ts
      this.snackbarLast.close("replaced");
    }

    let resolve;
    let promise = new Promise(_ => resolve = _);

    this.snackbarLastText = text;
    let el = mdcrd.snackbar(text, action, null)(null);
    document.body.appendChild(el);
    
    /** @type {import("@material/snackbar").MDCSnackbar} */
    let snackbar = el["MDCSnackbar"];

    // @ts-ignore Outdated .d.ts
    snackbar.open();

    el.addEventListener("MDCSnackbar:closed", e => {
      resolve(e["detail"].reason === "action");
      setTimeout(() => {
        el.remove();
      }, 2000);
    });
    snackbar["then"] = promise.then.bind(promise);
    snackbar["catch"] = promise.catch.bind(promise);

    this.snackbarLast = snackbar;
    return snackbar;
  }

}

const frontend = window["frontend"] = Frontend.instance = new Frontend();
