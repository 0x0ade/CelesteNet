//@ts-check

export class FrontendSettings {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;

    this.load();
    this.save();
  }

  load() {
    try {
      const raw = localStorage["frontend-settings"];
      if (raw)
        this.data = JSON.parse(raw);
    } catch (e) {
      console.error("settings", "failed to load:", e);
    }

    this.data = Object.assign({
      sensitive: true,
      accountsClutter: false,
      accountsFilterLocally: true
    }, this.data || {});
  }

  save() {
    localStorage["frontend-settings"] = JSON.stringify(this.data);
  }

  /** @type {boolean} */
  get sensitive() {
    return this.data.sensitive;
  }
  set sensitive(value) {
    this.data.sensitive = value;
  }


  /** @type {boolean} */
  get minimizeServerMsgs() {
    return this.data.minimizeServerMsgs;
  }
  set minimizeServerMsgs(value) {
    this.data.minimizeServerMsgs = value;
  }

  /** @type {boolean} */
  get accountsClutter() {
    return this.data.accountsClutter;
  }
  set accountsClutter(value) {
    this.data.accountsClutter = value;
  }


  /** @type {boolean} */
  get accountsFilterLocally() {
    return this.data.accountsFilterLocally;
  }
  set accountsFilterLocally(value) {
    this.data.accountsFilterLocally = value;
  }

}
