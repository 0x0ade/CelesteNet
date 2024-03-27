//@ts-check
import { rd, rdom, rd$, RDOMListHelper } from "../../../js/rdom.js";

/** @type {import("material-components-web")} */
const mdc = window["mdc"]; // mdc
// @ts-ignore
const markdown = new showdown.Converter(); // showdown

/**
 * @typedef {(el: HTMLElement) => HTMLElement} HTMLElementGen
 * @typedef {string | HTMLElementGen} Label
 * @typedef {(string | HTMLElementGen | any[])[]} Items
 */

var mdcrd = {

  _: {
    menus: [],
  },

  /**
   * @param {string} text
   */
  markdown: (text) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`<span ${rd.html("text")}=${markdown.makeHtml(text)}></span>`,

  /**
   * @param {string} name
   */
  icon: (name) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`<i class="material-icons">${name}</i>`,

  /**
   * @param {string} text
   * @param {string} action
   * @param {(e: Event) => void} callback
   */
  snackbar: (text, action, callback) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`
  <div class="mdc-snackbar mdc-snackbar--leading">
    <div class="mdc-snackbar__surface">
      <div class="mdc-snackbar__label" role="status" aria-live="polite">
        ${text}
      </div>
      <div class="mdc-snackbar__actions" rdom-get="list">
        <button type="button" class="mdc-button mdc-snackbar__action" onclick=${callback}>${action}</button>
        <button class="mdc-icon-button mdc-snackbar__dismiss material-icons" title="Dismiss">close</button>
      </div>
    </div>
  </div>
  ${el => el.MDCSnackbar = new mdc.snackbar.MDCSnackbar(el)}`,

  /**
   * @param {string} label
   * @param {string} [placeholder]
   * @param {(value: string) => void} [cbChange]
   * @param {(e: KeyboardEvent) => void} [cbKey]
   */
  textField: (label, placeholder, cbChange, cbKey) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <div class="mdc-text-field mdc-text-field--outlined" ${rd.toggleClass("no-label", "mdc-text-field--no-label")}=${!label}>
      <input name=${label} class="mdc-text-field__input" onchange=${cbChange} onkeydown=${cbKey}>
      <div class="mdc-notched-outline">
        <div class="mdc-notched-outline__leading"></div>
        ${el => label ?
          rd$(el)`<div class="mdc-notched-outline__notch"><label class="mdc-floating-label">${label}</label></div>` :
          null}
        <div class="mdc-notched-outline__trailing"></div>
      </div>
    </div>
    ${el => el.MDCTextField = new mdc.textField.MDCTextField(el)}`;

    el["MDCTextField"].value = placeholder || "";

    return el;
  },

  /**
   * @param {Label} label
   * @param {(e: MouseEvent) => void} [cb]
   */
  button: (label, cb) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`
  <button
    class="mdc-button"
    onclick=${cb}
  >${label}</button>
  ${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`,

  /**
   * @param {string} label
   * @param {string} icon
   * @param {(e: MouseEvent) => void} [cb]
   */
  iconButton: (label, icon, cb) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`
  <button
    class="mdc-icon-button material-icons"
    aria-label=${label}
    onclick=${cb}
  >${icon}</button>`,

  list: {
    /**
     * @param {HTMLElement} el
     */
    divider: el =>
    rd$(el)`<li class="mdc-list-divider" role="separator"></li>`,

    /**
     * @param {Label} label
     * @param {(e: Event) => void} [callback]
     * @param {boolean} [enabled]
     */
    item: (label, callback, enabled = true) =>
    /**
     * @param {HTMLElement} el
     */
    el => {
      el = rd$(el)`
      <li class="mdc-list-item"
        onclick=${callback} tabindex=${enabled ? 0 : -1} aria-disabled=${!enabled}
        ${rd.toggleClass("disabled", "mdc-list-item--disabled")}=${!enabled}
      >
        <span class="mdc-list-item__text">${label}</span>
      </li>
      ${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`;

      el.style.cursor = callback ? undefined : "default";

      return el;
    },

    /**
     * @param {Items} items
     */
    list: (...items) =>
    /**
     * @param {HTMLElement} el
     */
    el => {
      el = rd$(el)`
      <ul class="mdc-list"></ul>
      ${el => {
        // @ts-ignore
        el.MDCList = new mdc.list.MDCList(el);
      }}`;

      let list = new RDOMListHelper(el);
      for (let i in items) {
        let item = items[i];
        if (!(item instanceof Function))
          // @ts-ignore
          list.add(i, mdcrd.list.item(...item));
        else
          list.add(i, item);
      }
      list.end();

      return el;
    },
  },

  menu: {
    /**
     * @param {Label} label
     * @param {Items} items
     */
    auto: (label, ...items) =>
    /**
     * @param {HTMLElement} el
     */
    el => rd$(el)`
    <div class="mdc-menu-surface--anchor">
      ${mdcrd.menu.button(label)}
      ${mdcrd.menu.list(...items)}
    </div>`,

    /**
     * @param {HTMLElement | Event} a
     * @param {Event} [b]
     */
    open: function(a, b) {
      let el =
        a instanceof HTMLElement ? a :
        this instanceof HTMLElement ? this :
        null;
      let e =
        a instanceof Event ? a :
        b instanceof Event ? b :
        null;

      let menuEl = el["MDCMenu"] ? el : el.parentElement.getElementsByClassName("mdc-menu")[
        Array.prototype.slice.call(
          el.parentElement.getElementsByClassName("mdc-menu-btn")
        ).indexOf(el)
      ];
      let menu = menuEl["MDCMenu"];

      // TODO: Fix tabindex
      /*
      if (e instanceof KeyboardEvent) {
        for (let child of menuEl.querySelectorAll("[tabindex-o]")) {
          if (!(child instanceof HTMLElement))
            continue;
          rd$(rdom.getCtx(child))`tabindex=${child.getAttribute("tabindex-o")}`;
          child.removeAttribute("tabindex-o");
        }
      } else {
        for (let child of menuEl.querySelectorAll("[tabindex]")) {
          if (!(child instanceof HTMLElement))
            continue;
          child.setAttribute("tabindex-o", child.getAttribute("tabindex-o") || child.getAttribute("tabindex"));
          rd$(rdom.getCtx(child))`tabindex=${""}`;
        }
      }
      */
      for (let child of menuEl.querySelectorAll("[tabindex]")) {
        if (!(child instanceof HTMLElement))
          continue;
        rd$(rdom.getCtx(child))`tabindex=${""}`;
      }

      for (let other of mdcrd._.menus)
        other.open = other === menu;
    },

    /**
     * @param {Label} label
     * @param {(e: MouseEvent | KeyboardEvent) => void} [open]
     */
    button: (label, open) =>
    /**
     * @param {HTMLElement} el
     */
    el => rd$(el)`
    <button
      class="mdc-button mdc-menu-btn"
      onmousedown=${function(/** @type {MouseEvent} */ e) {
        if (e.button > 2)
          return;
        if (open)
          return open.apply(this, [e]);
        return mdcrd.menu.open(this, e);
      }}
      onkeypress=${function(/** @type {KeyboardEvent} */ e) {
        if (e.key !== "Enter")
          return;
        if (open)
          return open.apply(this, [e]);
        return mdcrd.menu.open(this, e);
      }}
    >${label}</button>
    ${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`,

    /**
     * @param {string} icon
     * @param {Label} label
     * @param {(e: MouseEvent) => void} callback
     */
    item: (icon, label, callback, enabled = true) =>
    /**
     * @param {HTMLElement} el
     */
    el => rd$(el)`
    <li class="mdc-list-item" role="menuitem"
      onclick=${callback} tabindex=${enabled ? 0 : -1} aria-disabled=${!enabled}
      ${rd.toggleClass("disabled", "mdc-list-item--disabled")}=${!enabled}
    >
      <span class="mdc-list-item__text">${mdcrd.icon(icon)}${label}</span>
    </li>
    ${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`,

    /**
     * @param {Items} items
     */
    list: (...items) =>
    /**
     * @param {HTMLElement} el
     */
    el => {
      el = rd$(el)`
      <div class="mdc-menu mdc-menu-surface" tabindex="-1">
        <ul class="mdc-menu__items mdc-list" role="menu" rdom-get="list"></ul>
      </div>
      ${el => mdcrd._.menus.push(el.MDCMenu = new mdc.menu.MDCMenu(el))}`;

      el["MDCMenu"].quickOpen = false;

      let list = new RDOMListHelper(rdom.get(el, "list"));
      for (let i in items) {
        let item = items[i];
        if (!(item instanceof Function))
          // @ts-ignore
          list.add(i, mdcrd.menu.item(...item));
        else
          list.add(i, item);
      }
      list.end();

      return el;
    },
  },

  /**
   * @param {Items} left
   * @param {Items} right
   */
  topAppBar: (left, right) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <header class="mdc-top-app-bar mdc-top-app-bar--fixed">
      <div class="mdc-top-app-bar__row">
        <section class="mdc-top-app-bar__section mdc-top-app-bar__section--align-start" rdom-get="left">
        </section>

        <section class="mdc-top-app-bar__section mdc-top-app-bar__section--align-end" rdom-get="right">
        </section>
      </div>
    </header>
    ${el => el.MDCTopAppBar = new mdc.topAppBar.MDCTopAppBar(el)}`;

    let list;
    list = new RDOMListHelper(rdom.get(el, "left"));
    for (let i in left)
      list.add(i, left[i]);
    list.end();
    list = new RDOMListHelper(rdom.get(el, "right"));
    for (let i in right)
      list.add(i, right[i]);
    list.end();

    return el;
  },

  /**
   * @param {string} text
   */
  topAppBarTitle: (title) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`<span class="mdc-top-app-bar__title">${title}</span>`,

  /**
   * @param {string} label
   * @param {string} icon
   * @param {(e: MouseEvent) => void} [cb]
   */
  topAppBarAction: (label, icon, cb) =>
  /**
   * @param {HTMLElement} el
   */
  el => rd$(el)`
  <button
    class="mdc-icon-button material-icons mdc-top-app-bar__action-item--unbounded"
    aria-label=${label}
    onclick=${cb}
  >${icon}</button>`,

  /** @param {{ title: string; body: HTMLElementGen; defaultButton: string; buttons: string[] }} _ */
  dialog: ({
    title,
    body,
    defaultButton,
    buttons
  }) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <div class="mdc-dialog" role="alertdialog" aria-modal="true" aria-labelledby="my-dialog-title" aria-describedby="my-dialog-content">
      <div class="mdc-dialog__container">
        <div class="mdc-dialog__surface">
        <h2 class="mdc-dialog__title" id="my-dialog-title">${title}</h2>
        <div class="mdc-dialog__content" id="my-dialog-content">${body}</div>
        <footer class="mdc-dialog__actions" rdom-get="buttons"></footer>
        </div>
      </div>
      <div class="mdc-dialog__scrim"></div>
    </div>
    ${el => el.MDCDialog = new mdc.dialog.MDCDialog(el)}`;

    let list;
    list = new RDOMListHelper(rdom.get(el, "buttons"));
    for (let i in buttons)
      list.add(i, el => rd$(el)`<button type="button" class="mdc-button mdc-dialog__button" data-mdc-dialog-action=${i} ${rd.toggleClass(`default-${i}`, "mdc-dialog__button--default")}=${defaultButton === i}>${buttons[i]}</button>${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`);
    list.end();

    return el;
  },

  /**
   * @param {number} value
   */
  progress: (value) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <div role="progressbar" class="mdc-linear-progress" aria-valuemin="0" aria-valuemax="1" aria-valuenow="0">
      <div class="mdc-linear-progress__buffer">
        <div class="mdc-linear-progress__buffer-bar"></div>
        <div class="mdc-linear-progress__buffer-dots"></div>
      </div>
      <div class="mdc-linear-progress__bar mdc-linear-progress__primary-bar">
        <span class="mdc-linear-progress__bar-inner"></span>
      </div>
      <div class="mdc-linear-progress__bar mdc-linear-progress__secondary-bar">
        <span class="mdc-linear-progress__bar-inner"></span>
      </div>
    </div>
    ${el => el.MDCLinearProgress = new mdc.linearProgress.MDCLinearProgress(el)}`;

    /** @type {import("material-components-web").linearProgress.MDCLinearProgress} */
    const p = el["MDCLinearProgress"];
    const determinate = -1 <= value && value <= 1;
    p.determinate = determinate;
    p.reverse = value < 0;
    if (determinate)
      p.progress = Math.abs(value);

    return el;
  },

  /**
   * @param {string} label
   * @param {boolean} [value]
   */
  checkbox: (label, value) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <div class="mdc-form-field">
      <div class="mdc-checkbox">
        <input type="checkbox"
              class="mdc-checkbox__native-control"
              id="checkbox"/>
        <div class="mdc-checkbox__background">
          <svg class="mdc-checkbox__checkmark"
              viewBox="0 0 24 24">
            <path class="mdc-checkbox__checkmark-path"
                  fill="none"
                  d="M1.73,12.91 8.1,19.28 22.79,4.59"/>
          </svg>
          <div class="mdc-checkbox__mixedmark"></div>
        </div>
        <div class="mdc-checkbox__ripple"></div>
      </div>
      <label for="checkbox">${label}</label>
    </div>
    ${el => {
      el.MDCFormField = new mdc.formField.MDCFormField(el);
      el.MDCCheckbox = new mdc.checkbox.MDCCheckbox(el.querySelector(".mdc-checkbox"));
    }}`;

    if (value != null)
      el.querySelector("input").checked = !!value;

    return el;
  },

  /**
   * @param {string} label
   * @param {string[]} choices
   * @param {number} preselect
   */
  dropdown: (label, choices, preselect) =>
  /**
   * @param {HTMLElement} el
   */
  el => {
    el = rd$(el)`
    <div class="mdc-select mdc-select--filled">
      <div class="mdc-select__anchor" style="width: 100%; min-width:400px">
        <span class="mdc-select__ripple"></span>
        <span class="mdc-floating-label mdc-floating-label--float-above">${label}</span>
          <input type="text" disabled readonly class="mdc-select__selected-text" value="" />
        <span class="mdc-select__dropdown-icon">
          <svg
              class="mdc-select__dropdown-icon-graphic"
              viewBox="7 10 10 5" focusable="false">
            <polygon
                stroke="none"
                fill-rule="evenodd"
                points="7 10 12 15 17 10">
            </polygon>
          </svg>
        </span>
        <span class="mdc-line-ripple"></span>
      </div>

      <div class="mdc-select__menu mdc-menu mdc-menu-surface" style="width: 100%; min-width:400px">
        ${el => {
          el = rd$(el)`<ul class="mdc-list"></ul>`;
          let list = new RDOMListHelper(el);
          for (let i in choices) {
            list.add(i, el => rd$(el)`
            <li class="mdc-list-item mdc-list-item--selected" data-value=${choices[i]} >
            <span class="mdc-list-item__ripple"></span>
            <span class="mdc-list-item__text">${choices[i]}</span>
            </li>
            ${el => el.MDCRipple = new mdc.ripple.MDCRipple(el)}`);
          }
          list.end();
          return el;
        }}
      </div>
    </div>
    ${el => {
      el.MDCSelect = new mdc.select.MDCSelect(el);
      el.MDCSelect.selectedIndex = preselect;
    }}`;

    el["MDCSelect"].selectedIndex = preselect;

    return el;
  }

};

export default window["mdcrd"] = mdcrd;
