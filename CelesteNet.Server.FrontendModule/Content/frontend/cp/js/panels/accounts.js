//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";
import { FrontendStatusPanel } from "./status.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/**
@typedef {{
  UID: string,
  Name: string,
  Discrim: string,
  Tags: string[],
  Key: string,
  Ban: {
    Name: string,
    Reason: string,
    From: number,
    To: number
  },
  Kicks: {
    Reason: string,
    From: number
  }[]
}} UserInfo
 */

export class FrontendAccountsPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Accounts";
    this.ep = "/api/userinfos";
    this.filteredEP = "/api/userinfosfiltered";
    /** @type {UserInfo[]} */
    this.data = [];

    this.lastFetched = "";
    this.currPage = 1;
    this.accountsPerPage = 200;
    this.totalAccounts = 100 * this.accountsPerPage;

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Filter Mode: ...",
        "cloud_off",
        () => {
          this.frontend.settings.accountsFilterLocally = !this.frontend.settings.accountsFilterLocally;
          this.frontend.settings.save();
          this.updateActionButtons();
          if (this.frontend.settings.accountsFilterLocally)
            this.doManualFetch().then(() => this.refresh());
          else
            this.refresh();
        }
      ],

      [
        "Refresh",
        "sync",
        () => {
          if (this.frontend.settings.accountsFilterLocally)
            this.doManualFetch().then(() => this.refresh());
          else
            this.refresh();
        }
      ],

      [
        "Filter: ...",
        "filter_alt_off",
        () => {
          this.frontend.settings.accountsClutter = !this.frontend.settings.accountsClutter;
          this.frontend.settings.save();
          this.updateActionButtons();
          this.refresh();
        }
      ]
    ];

    this.updateActionButtons();
  }

  updateActionButtons() {
    // updates icons & labels (tooltips) of the buttons
    if (this.frontend.settings.accountsFilterLocally) {
          // filter modes
          this.actions[0][0] = "Filter Mode: In Browser";
          this.actions[0][1] = "cloud_off" ;
          // refresh / reload
          this.actions[1][0] = "Reload All";
          this.actions[1][1] = "update" ;

    } else {
          // filter modes
          this.actions[0][0] = "Filter Mode: On Server";
          this.actions[0][1] = "cloud";
          // refresh / reload
          this.actions[1][0] = "Refresh";
          this.actions[1][1] = "sync";
    }

    // filter toggle
    if (!this.frontend.settings.accountsClutter) {
          this.actions[2][0] = "Filter: Kick/Ban/Tag";
          this.actions[2][1] = "filter_alt";
    } else {
          this.actions[2][0] = "Filter: Show All";
          this.actions[2][1] = "filter_alt_off";
    }
  }

  
  render(el) {
    this.updateNumbers();
    return this.el = rd$(el || this.el)`
    <div class="panel" ${rd.toggleClass("panelType", "panel-" + this.id)}=${true}>
      ${el => this.renderHeader(el)}
      ${mdcrd.progress(this.progress)}
      ${el => this.renderInput(el)}
      ${el => this.renderBody(el)}
    </div>`;
  }

  renderInput(el) {
    // Render input only once.
    if (this.elInput)
      return this.elInput;

    this.updateNumbers();

    this.elInput = rd$(el || this.elInput)`
    <div class="panel-input">
      ${mdcrd.textField("", "", null, () => { this.currPage = 1; this.refresh(); })}
      ${mdcrd.iconButton("Prev", "chevron_left", () => { this.prevPage(); this.refresh(); })}
      ${el => {
        el = rd$(el)`<span class="page-counter"></span>`;
        el.innerHTML = this.currPage + " / " + Math.ceil(this.totalAccounts / this.accountsPerPage);
        return el;
      }}
      ${mdcrd.iconButton("Next", "chevron_right", () => { this.nextPage(); this.refresh(); })}
    </div>`;
    
    // tried to do a this.frontend.dom.setContext to a mdcrd.iconButton but failed because fuck all this rd jazz, I understand none of it :) ~rf
    return this.elInput;
  }

  prevPage() {
    if (this.currPage > 1)
      this.currPage--;
  }

  nextPage() {
    this.currPage++;
  }

  updateNumbers() {
    if (this.currPage < 1)
      this.currPage = 1;

    if (this.data && this.totalAccounts == 0)
      this.totalAccounts = this.data.length;

    if (this.elInput) {
      this.counter = this.elInput.getElementsByClassName("page-counter")[0];
      this.counter.innerHTML = this.currPage + " / " + Math.ceil(this.totalAccounts / this.accountsPerPage);
    }

    this.subheader = "(" + this.totalAccounts + "/" + this.data.length + ")";
  }

  async doManualFetch() {
    if (this.progress !== 2) {
      this.progress = 2;
      this.render();
    }

    // this takes the fetch call out of update() when filtering locally
    // so that a "refresh" doesn't automatically fetch all data again

    // either full list for clutter, or filtered, but no other params
    let useEP = this.ep + "?from=0&count=1000000";

    if (!this.frontend.settings.accountsClutter) {
      useEP = this.filteredEP + "?onlyspecial=true&from=0&count=1000000";
    }

    console.log("Starting manual fetch...");

    this.data = (await fetch(useEP).then(r => r.json()));
    this.lastFetched = useEP;

    console.log("Manual fetch done.");
    this.progress = 0;
    this.render();
  }

  async update() {
    if (this.currPage < 1)
      this.currPage = 1;

    this.input = this.elInput.getElementsByTagName("input")[0];
    let filter = this.input.value.trim();

    // first: deal with which data to fetch. For local filtering, handled by doManualFetch() triggered by user

    // fetching when filtering server-side:
    if (!this.frontend.settings.accountsFilterLocally) {
      let fromCountParams = "from=" + this.accountsPerPage * (this.currPage - 1) + "&count=" + this.accountsPerPage;

      // for server-side filtering, always use old EP when no search and clutter, otherwise always filtered
      let useEP = this.ep + "?" + fromCountParams;

      // otherwise, adjust parameters on filteredEP
      if (!this.frontend.settings.accountsClutter || filter !== "") {
        useEP = this.filteredEP + "?onlyspecial=" + !this.frontend.settings.accountsClutter;

        useEP += "&" + fromCountParams;

        if (filter !== "")
          useEP += "&search=" + filter;
      }

      // do the actual fetch
      this.data = (await fetch(useEP).then(r => r.json())).sort((a, b) => {
        if (!a.Name && b.Name)
          return 1;
        if (a.Name && !b.Name)
          return -1;
        return a.Name.localeCompare(b.Name);
      });
      this.lastFetched = useEP;
    }

    this.updateNumbers();

    // deal with slicing (only needed for local filter)
    let sliceFrom = 0;
    let sliceTo = this.data.length;

    if (this.frontend.settings.accountsFilterLocally) {
      sliceFrom = this.accountsPerPage * (this.currPage - 1);
      sliceTo = this.accountsPerPage * this.currPage;
    }

    // mainly for local filtering, to narrow down this.data
    let dataToShow;

    if (!this.frontend.settings.accountsFilterLocally)
      dataToShow = this.data;
    else {
      if (this.frontend.settings.accountsClutter)
        dataToShow = this.data;
      else
        dataToShow = this.data.filter(p => p.Ban || (p.Kicks && p.Kicks.length) || (p.Tags && p.Tags.length));

      if (filter !== "")
        dataToShow = dataToShow.filter(p => p.Name.toLowerCase().indexOf(filter) >= 0);
    }

    this.totalAccounts = dataToShow.length;

    // @ts-ignore
    this.list = dataToShow.slice(sliceFrom, sliceTo).map(p => el => {
      el = mdcrd.list.item(el => {
        el = rd$(el)`<span></span>`;
        const list = new RDOMListHelper(el);
        list.add("name", el => rd$(el)`<span><b>${p.Name || "?"}</b>${(p.Discrim ? "#" + p.Discrim : "") + " "} <i>(${p.Name ? p.UID : this.frontend.censor(p.UID)})</i></span>`);
        if (p.Key)
          list.add("key", el => rd$(el)`<span><br><b>Key: </b>${"#" + this.frontend.censor(p.Key)}</span>`);
        if (p.Tags && p.Tags.length > 0)
          list.add("tags", el => rd$(el)`<span><br><b>Tags: </b>${p.Tags.join(", ")}</span>`);
        if (p.Ban)
          list.add("ban", el => rd$(el)`<span><br><b>Ban: </b>${(p.Ban.Name || "?") + ": " + this.frontend.utils.datetime(p.Ban.From) + ": " + p.Ban.Reason}</span>`);
        if (p.Kicks && p.Kicks.length) {
          list.add("kicks", el => rd$(el)`<span><br><b>Kicks: </b>${p.Kicks.length}</span>`);
          const kick = p.Kicks[p.Kicks.length - 1];
          list.add("lastkick", el => rd$(el)`<span><br><b>Last Kick: </b>${this.frontend.utils.datetime(kick.From) + ": " + kick.Reason}</span>`);
        }
        list.end();
        return el;
      })(el);

      let contextOpts = [
        [ "gavel", p.Ban ? `Unban ${p.Name || p.UID}` : `Ban ${p.Name || p.UID}`, () => {
            if (!p.Ban) {
              this.frontend.dialog.ban(p.Name, 0, p.UID);
            } else {
              this.frontend.sync.run("unban", p.UID);
            }
          } ],
          [ "content_copy", `Copy UID: ${p.UID}`, () =>  navigator.clipboard.writeText(p.UID) ]
      ];

      if (p.Name)
        contextOpts = [
          ...contextOpts,
          [ "content_copy", `Copy Name: ${p.Name}`, () =>  navigator.clipboard.writeText(p.Name) ]
        ];

      if (p.Key)
        contextOpts = [
          ...contextOpts,
          [ "content_copy", "Copy Key", () =>  navigator.clipboard.writeText(p.Key) ]
        ];

      if (p.Ban)
        contextOpts = [
          ...contextOpts,
          [ "content_copy", "Copy Ban reason", () =>  navigator.clipboard.writeText(p.Ban.Reason) ]
        ];

      this.frontend.dom.setContext(el, ...contextOpts);

      return el;
    });
  }

}
