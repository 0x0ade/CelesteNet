//@ts-check
import { rd, rdom, rd$, escape$, RDOMListHelper } from "../utils/rdom.js";
import mdcrd from "../utils/mdcrd.js";

/**
 * @typedef {import("material-components-web")} mdc
 */
/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc

/**
 * @typedef {{
    id: string,
    ep: string?,
    progress: number?,
    render: (el: HTMLElement) => HTMLElement,
    el: HTMLElement,
    start: () => Promise,
    refresh: (() => Promise)?
  }} Panel
 */

export class FrontendDOM {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;
    this.container = document.getElementsByTagName("app-container")[0];
    /** @type {Panel[]} */
    this.panels = [];
    /** @type {Map<string, Panel>} */
    this.panelmap = new Map();
    this.started = false;
    /** @type {import("material-components-web").menu[]} */
    this.menus = [];
  }

  async start() {
    this.started = true;
    for (let panel of this.panels)
      if (panel.start)
        await panel.start();
    document.addEventListener("scroll", e => {
      for (let other of this.menus)
        other.open = false;
    }, true);
  }

  /**
   * @param {string | Panel} panelOrID
   * @param {(el: HTMLElement) => HTMLElement} [render]
   */
  async add(panelOrID, render) {
    /** @type {Panel} */
    // @ts-ignore
    const panel = !render ? panelOrID : {
      id: panelOrID,
      render: render
    }

    if (this.panels.findIndex(p => p.id === panel.id) !== -1)
      return;

    this.panels.push(panel);
    this.panelmap.set(panel.id, panel);
    if (this.started && panel.start)
      await panel.start();
    this.frontend.render();

    if (panel.ep) {
      let refreshTimeout;
      this.frontend.sync.register("update", data => {
        if (data !== panel.ep)
          return;
        console.log("update", data);
        if (panel.progress !== 2) {
          panel.progress = 2;
          panel.render(null);
        }
        if (refreshTimeout)
          clearTimeout(refreshTimeout);
        refreshTimeout = setTimeout(() => panel.refresh(), 300);
      });
    }
  }

  /**
   * @param {string | Panel} panelOrID
   */
  remove(panelOrID) {
    // @ts-ignore
    const id = panelOrID.id || panelOrID;
    const index = this.panels.findIndex(p => p.id == id);

    if (index == -1)
      return;

    this.panels.splice(index, 1);
    this.frontend.render();
  }

  /**
   * @param {HTMLElement} el
   * @param {string} text
   * @param {"up" | "down"} dir
   */
  setTooltip(el, text, dir = "up") {
    let ctx = {
      /** @type {HTMLElement} */
      el: null,
      text: text,
      dir: dir,
      /** @type {() => void} */
      show: null,
      /** @type {() => void} */
      hide: null
    };

    if ("cogTooltip" in el) {
      // @ts-ignore
      el["cogTooltip"].text = text;
      // @ts-ignore
      el["cogTooltip"].dir = dir;
      // @ts-ignore
      return el["cogTooltip"].tooltipEl;
    }

    let visible = false;

    /**
     * @param {boolean} _visible
     */
    const renderTooltip = (_visible) => {
      visible = _visible;
      let tooltipEl = ctx.tooltipEl = rd$(ctx.tooltipEl)`<div class="tooltip" data-tooltip-dir=${ctx.dir} ${rd.toggleClass("visible")}=${visible}>${ctx.text}</div>`;
      refreshTooltip();
      return tooltipEl;
    };

    const refreshTooltip = () => {
      let tooltipEl = ctx.tooltipEl;
      let rect = el.getBoundingClientRect();

      tooltipEl.style.left = undefined;
      tooltipEl.style.bottom = undefined;

      switch (dir) {
        case "up":
        default:
          tooltipEl.style.left = (rect.left + rect.width / 2) + "px";
          tooltipEl.style.bottom = ((window.innerHeight - rect.top) + 2) + "px";
          break;

        case "down":
          tooltipEl.style.left = (rect.left + rect.width / 2) + "px";
          tooltipEl.style.top = (rect.bottom + 2) + "px";
          break;
      }

      if (visible)
        requestAnimationFrame(refreshTooltip);
    };

    el.addEventListener("mouseover", ctx.show = () => {
      renderTooltip(true);
    }, false);
    el.addEventListener("mouseout", ctx.hide = () => {
      renderTooltip(false);
    }, false);

    document.body.appendChild(renderTooltip(false));
    el["cogTooltip"] = ctx;
    return ctx.tooltipEl;
  }

  /**
   * @param {HTMLElement} el
   * @param {(string | any[] | ((el: HTMLElement) => HTMLElement))[]} items
   */
  setContext(el, ...items) {
    const prevEl = el.frontendctx;
    let ctx = el.frontendctx = mdcrd.menu.list(...items)(prevEl);
    let menu = ctx["MDCMenu"];

    if (!prevEl) {
      document.body.appendChild(ctx);
      this.menus.push(menu);
    }

    el.addEventListener("contextmenu", e => {
      e.preventDefault();

      for (let other of this.menus)
        other.open = other === menu && items.length !== 0;

      menu.setAbsolutePosition(e.clientX, e.clientY);
      menu.setFixedPosition(true);

      return false;
    });
  }

  render() {
    let timeStart = performance.now();

    this.el = rd$(this.el)`
    <app>
      ${mdcrd.topAppBar([
        mdcrd.topAppBarTitle("CelesteNet")
      ], [
        mdcrd.topAppBarAction("Settings", "settings")
      ])}

      ${el => {
        el = rd$(el)`<main></main>`;

        let panels = new RDOMListHelper(el);

        for (let panel of this.panels) {
          panels.add(panel.id, el => panel.render(el));
        }

        return el;
      }}

    </app>
    ${el => this.container.appendChild(el)}`;

    let timeEnd = performance.now();
    console.log("[perf]", "FrontendDOM.render", timeEnd - timeStart);
  }

}
