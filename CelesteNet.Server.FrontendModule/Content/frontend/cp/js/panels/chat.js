//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../../../js/rdom.js";
import mdcrd from "../utils/mdcrd.js";
import { FrontendBasicPanel } from "./basic.js";
import { FrontendPlayersPanel } from "./players.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/**
@typedef {{
  ID: number,
  PlayerID: number,
  Targets: number[] | null,
  Color: string,
  DateTime: number,
  Tag: string,
  Text: string
}} ChatData
 */

export class FrontendChatPanel extends FrontendBasicPanel {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    super(frontend);
    this.header = "Chat";
    this.ep = "/api/chatlog";

    /** @type {[string, string, () => void][]} */
    this.actions = [
      [
        "Pause", "pause",
        () => {
          this.paused = !this.paused;
          this.render();
          if (!this.paused)
            this.refresh();
        }
      ],

      [
        "Auto-scroll", "vertical_align_bottom",
        () => {
          this.autoscroll = !this.autoscroll;
          this.refresh();
        }
      ],

      [
        "Shorten Server messages", this.frontend.settings.minimizeServerMsgs ? "short_text" : "notes",
        () => {
          this.frontend.settings.minimizeServerMsgs = !this.frontend.settings.minimizeServerMsgs;
          this.frontend.settings.save();
          this.actions[2][1] = this.frontend.settings.minimizeServerMsgs ? "short_text" : "notes";
          this.list = [];
          this.refresh();
        }
      ],

      [
        "Fetch Log", "history",
        () => {
          this.refresh();
        }
      ],

      [
        "Clear", "clear",
        () => {
          this.list = [];
          this.render();
        }
      ]
    ];

    /** @type {[string | ((el: HTMLElement) => HTMLElement), () => void][] | [string | ((el: HTMLElement) => HTMLElement)][]} */
    this.list = [];
    /** @type {ChatData[]} */
    this.data = [];
    /** @type boolean */
    this.paused = false;
    /** @type boolean */
    this.autoscroll = true;

    frontend.sync.register("chat", data => this.log(data.Text, data.Color, data));
  }

  async refresh() {
    if (this.paused)
      return;
    await super.refresh();
    if (this.autoscroll)
      this.elBody.scrollTop = this.elBody.scrollHeight;
  }

  async update() {
    if (this.paused)
      return;
    this.data = await fetch(this.ep + "?count=100&detailed=true").then(r => r.json());
    // @ts-ignore
    this.list = this.data.filter(data => !this.dropExtraChannelChats(data)).map(data => this.createEntry(data.Text, data.Color, data));
  }

    /**
     * @param {ChatData} [data]
     * @returns boolean
     */
    dropExtraChannelChats(data) {
      // Conditions to DROP this message:
      // it's sent in a channel, AND it has Targets AND the player sending it is not such a Target.
      // this predicate is meant to drop all the repeat channel messages that are akin to whispering each recipient individually
      return data.Tag.startsWith("channel ") && data.Targets && data.Targets.indexOf(data.PlayerID) == -1
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

  renderHeader(el) {
    el = super.renderHeader(el);
    let actionBtns = this.elHeader.getElementsByTagName("button");
    actionBtns[0].classList.toggle("button-fade", !this.paused);
    actionBtns[1].classList.toggle("button-fade", !this.autoscroll);
    if (this.paused) {
      actionBtns[1].setAttribute("disabled", "true");
      actionBtns[2].setAttribute("disabled", "true");
    } else {
      actionBtns[1].removeAttribute("disabled");
      actionBtns[2].removeAttribute("disabled");
    }
    return el;
  }

  renderInput(el) {
    // Render input only once.
    if (this.elInput)
      return this.elInput;

    return this.elInput = rd$(el || this.elInput)`
    <div class="panel-input">
      ${mdcrd.textField("", "", null, e => {
        if (e.keyCode === 13) {
          this.send();
        }
      })}
    </div>`
  }

  send(text) {
    /** @type {HTMLInputElement} */
    const input = this.elInput.getElementsByTagName("input")[0];
    text = text || input.value.trim();
    if (!text)
      return;

    this.progress += 2;
    this.render();

    this.frontend.sync.run("chat", text).then(
      data => {
        this.progress -= 2;
        this.render();
      },
      () => {
        this.progress -= 2;
        this.render();
      }
    );

    input.value = "";
  }

  /**
   * @param {string} text
   * @param {string} [color]
   * @param {ChatData} [data]
   * @returns {(el: HTMLElement) => HTMLElement}
   */
  createEntry(text, color, data) {
    return el => {
      let opts = [];
      let name = "";
        let targets = [];
        let tag = "";

      if (data) {
        opts = [
          ...opts,
          [ "delete", `Delete #${data.ID}`, () => this.frontend.sync.run("chatedit", {
            ID: data.ID,
            Color: "#ee2233",
            Text: "*deleted*"
          }) ]
          ];

          if (data.Tag)
              tag = "[" + data.Tag + "] ";

          if (data.PlayerID) {
              if (data.PlayerID !== this.frontend.MAX_INT) {
                  const player = FrontendPlayersPanel["instance"].data.find(p => p.ID == data.PlayerID);
                  // TODO: Rerender el on missing player only once! Otherwise render -> refresh -> render -> refresh...
                  if (!player)
                      FrontendPlayersPanel["instance"].refresh();
                  name = player && player.FullName || ("#" + data.PlayerID);

                  opts = [
                      ...opts,
                      ["error_outline", `Kick ${name}`, () => this.frontend.dialog.kick(data.PlayerID)],
                      ["gavel", `Ban ${name}`, () => this.frontend.dialog.ban(player && player.UID, player && player.ConnectionUID)]
                  ];
              } else {
                  name = " ** SERVER ** ";
              }
          }

          if (data.Targets && data.Targets.length > 0 && !data.Tag.startsWith("channel ")) {
              for (var targetID in data.Targets) {
                  const player = FrontendPlayersPanel["instance"].data.find(p => p.ID == targetID);
                  if (player)
                      targets.push(player && player.FullName || ("#" + targetID));
              }
          }

      }

        let chatText = `[${new Date(data.DateTime).toLocaleTimeString("de-DE")}] ${tag}${name}${(targets.length > 0 ? " @" + targets.join(",") : "")}:${(data.Text.indexOf('\n') != -1 ? "\n" : " ")}${data.Text}`;

      el = mdcrd.list.item(chatText)(el);
      if (color && color.toLowerCase() !== "#ffffff")
        el.style.color = color;
      else
        el.style.color = "#000000";


      this.frontend.dom.setContext(el, ...opts);

      if (data.Targets && !this.frontend.settings.sensitive)
        el.classList.add("hidden");
      else
        el.classList.remove("hidden");

      if (this.frontend.settings.minimizeServerMsgs)
        if (data.Targets && color && (color.toLowerCase() == "#9e24f5" || color.toLowerCase() == "#e39dcc"))
          el.classList.add("minimized");

      return el;
    };
  }

  /**
   * @param {string} text
   * @param {string} [color]
   * @param {ChatData} [data]
   */
  log(text, color, data) {
    if (this.dropExtraChannelChats(data))
      return;
    // @ts-ignore
    this.list.push(this.createEntry(text, color, data));
    if (this.list.length > 100) {
      this.list = this.list.slice(-100);
    }
    if (this.paused)
      return;
    this.render(null);
    if (this.autoscroll)
      this.elBody.scrollTop = this.elBody.scrollHeight;
  }

}
