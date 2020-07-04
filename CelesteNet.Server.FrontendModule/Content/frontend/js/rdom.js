// @ts-check

/* RDOM (rotonde dom)
 * 0x0ade's collection of DOM manipulation functions because updating innerHTML every time a single thing changes isn't cool.
 * This started out as a mini framework for Rotonde.
 * Mostly oriented towards quickly manipulating many things quickly.
 */

/**
 * @typedef {{ fields: any[], renderers: any[], texts: any[], html: string, init: void | ((el: HTMLElement) => (void | HTMLElement)) }} RDOMParseResult
 */

export var rdom = {
  /** @type {Map<string, HTMLElement>} */
  _cachedTemplates: new Map(),
  /** @type {Map<TemplateStringsArray, number[]>} */
  _cachedIDs: new Map(),
  _lastID: -1,

  /**
   * Find an element with a given key.
   * @param {HTMLElement} el Element (context) to search inside.
   * @param {string | number} key Key to look for.
   * @returns {HTMLElement} Found element.
   */
  find(el, key, value = "", type = "field") {
    let attr = `rdom-${type}${key === 1 ? "" : key === undefined ? "s" : "-"+key}`;
    value = value ? value.toString() : value;

    let check = el => {
      let av = el.getAttribute(attr);
      return av !== null && (!value || value === av) ? el : null;
    }

    let checked = check(el);
    if (checked)
      return checked;
    
    let find = el => {
      // Check children first.
      for (let child = el.firstElementChild; child; child = child.nextElementSibling) {
        let checked = check(child);
        if (checked)
          return checked;
      }
      
      // If no child matches, check children's children.
      for (let child = el.firstElementChild; child; child = child.nextElementSibling) {
        if (child["rdomCtx"]) // Context change - ignore this branch.
          continue;
        let found = find(child);
        if (found)
          return found;
      }

      return null;
    }

    return find(el);
  },

  /**
   * Find all elements with a given key.
   * @param {HTMLElement} el Element (context) to search inside.
   * @param {string | number} key Key to look for.
   * @returns {HTMLElement[]} Found elements.
   */
  findAll(el, key, value = "", type = "field") {
    let attr = `rdom-${type}${key === 1 ? "" : key === undefined ? "s" : "-"+key}`;
    value = value ? value.toString() : value;

    let all = [];

    let check = el => {
      let av = el.getAttribute(attr);
      return av !== null && (!value || value === av) ? el : null;
    }

    let checked = check(el);
    if (checked)
      all.push(checked);
    
    let find = el => {
       for (let child = el.firstElementChild; child; child = child.nextElementSibling) {
        let checked = check(child);
        if (checked)
          all.push(checked);

        if (child["rdomCtx"]) // Context change - ignore this branch.
          continue;
        
        find(child);
      }
    }

    find(el);
    return all;
  },

  /**
   * Get the context ID of the given element.
   * @param {HTMLElement} el Element to get the context ID for.
   * @returns {string} RDOM context ID.
   */
  getCtxID(el, self = true) {
    let ctx;

    if (self && (ctx = el["rdomCtx"]))
      return ctx;

    while (el = el.parentElement) {
      if (ctx = el["rdomCtx"])
        return ctx;
    }

    return null;
  },

  /**
   * Get the context of the given element.
   * @param {HTMLElement} el Element to get the context for.
   * @returns {HTMLElement} RDOM context element.
   */
  getCtx(el, self = true) {
    if (self && el["rdomCtx"])
      return el;

    while (el = el.parentElement) {
      if (el["rdomCtx"])
        return el;
    }

    return null;
  },

  /** Prepare an element to be used for RDOM's state-related functions.
    * @param {HTMLElement} el The element to initialize.
    * @returns {HTMLElement} The passed element.
    */
  init(el) {
    if (el["rdomFields"])
      return el;
    el["rdomFields"] = {};
    el["rdomStates"] = {};
    return el;
  },

  /**
   * Move an element to a given index non-destructively.
   * @param {ChildNode} el The element to move.
   * @param {number} index The target index.
   */
  move(el, index) {
    if (!el)
      return;

    let tmp = el;
    // @ts-ignore previousElementSibling is too new?
    while (tmp = tmp.previousElementSibling)
      index--;

    // offset == 0: We're fine.
    if (!index)
      return;

    let swap;
    tmp = el;
    if (index < 0) {
      // offset < 0: Element needs to be pushed "left" / "up".
      // -offset is the "# of elements we expected there not to be",
      // thus how many places we need to shift to the left.
      // @ts-ignore previousElementSibling is too new?
      while ((swap = tmp) && (tmp = tmp.previousElementSibling) && index < 0)
        index++;
      // @ts-ignore before is too new?
      swap.before(el);
      
    } else {
      // offset > 0: Element needs to be pushed "right" / "down".
      // offset is the "# of elements we expected before us but weren't there",
      // thus how many places we need to shift to the right.
      // @ts-ignore previousElementSibling is too new?
      while ((swap = tmp) && (tmp = tmp.nextElementSibling) && index > 0)
        index--;
      // @ts-ignore after is too new?
      swap.after(el);
    }
  },

  /** Escape a string into a HTML - safe format.
   * @param {string} m String to escape.
   * @returns {string} Escaped string.
   */
  escape(m) {
    let n = "";
    for (let c of ""+m) {
      if (c === "&")
        n += "&amp;";
      else if (c === "<")
        n += "&lt;";
      else if (c === ">")
        n += "&gt;";
      else if (c === "\"")
        n += "&quot;";
      else if (c === "'")
        n += "&#039;";
      else
        n += c;
    }
    return n;
  },

  /**
   * Get the holder of the rdom-get with the given value, or all holders into the given object.
   * @param {HTMLElement} el
   * @param {string | any} valueOrObj
   * @returns {HTMLElement | any}
   */
  get(el, valueOrObj = {}) {
    if (typeof(valueOrObj) === "string")
      return rdom.find(el, 1, valueOrObj, "get");

    for (let field of rdom.findAll(el, 1, "", "get")) {
      let key = field.getAttribute("rdom-get");
      valueOrObj[key] = field;
    }

    return valueOrObj;
  },

  /**
   * Parse a template string into a HTML string + extra data, escaping expressions unprefixed with $, inserting attribute arrays and preserving child nodes.
   * @param {TemplateStringsArray} template
   * @param {...any} values
   * @returns {RDOMParseResult}
   */
  rdparse$(template, ...values) {
    try {
      let fields = [];
      let renderers = [];
      let texts = [];
      let init = null;

      let attrProxies = new Set();
      
      let ignored = 0;
      let ids = rdom._cachedIDs.get(template);
      if (!ids) {
        ids = [];
        rdom._cachedIDs.set(template, ids);
      }
      let idi = -1;
      let getid = () => ids[++idi] || (ids[idi] = ++rdom._lastID);

      let tag = (tag, attr = "", val = "") => `<rdom-${tag} ${attr}>${val}</rdom-${tag}>`

      let html = template.reduce(function rdparse$reduce(prev, next, i) {
        let val = values[i - 1];
        let t = prev[prev.length - 1];

        if (ignored) {
          // Ignore val.
          --ignored;
          return prev + next;
        }

        if (t === "$") {
          // Keep value as-is.
          return prev.slice(0, -1) + val + next;
        }

        if (val && val.key && next.trim() === "=") {
          // Settable / gettable field.
          next = "";
          fields.push({ h: val, key: val.key, state: val.state, value: values[i] });
          ++ignored;
          val = `rdom-field-${rdom.escape(val.key)}="${rdom.escape(val.key)}"`;

        } else if (t === "=") {
          // Proxy attributes using a field.
          if (val && val.join)
            val = val.join(" ");
          else if (!(val instanceof Function))
            val = ""+val;
          
          let split = prev.lastIndexOf(" ") + 1;
          let attr = prev.slice(split, -1);
          let key = attr;
          if (attrProxies.has(key))
            key += "-" + getid();
          attrProxies.add(attr);
          prev = prev.slice(0, split);
          let h = rd.attr(key, attr);
          h.value = val;
          fields.push(h);
          val = `rdom-field-${rdom.escape(key)}="${rdom.escape(key)}"`;

        } else if (val instanceof Function && i === values.length && !next) {
          // Add an init processor, which is present at the end of the template.
          init = val;
          val = "";

        } else if (val && (val instanceof Node || val instanceof Function)) {
          // Add placeholders, which will be replaced later on.
          let id = getid();
          let val_ = val;
          renderers.push({ id: id, value: val instanceof Function ? val : () => val_ });
          val = tag("empty", "rdom-render="+id);
        
        } else {
          // Proxy text using a text node.
          let id = getid();
          texts.push({ id: id, value: val });
          val = tag("text", "rdom-text="+id);
          // @ts-ignore
          prev = prev.trimRight();
          // @ts-ignore
          next = next.trimLeft();
        }

        return prev + val + next;
      });

      let data = {
        fields,
        renderers,
        texts,
        html,
        init
      };
      return data;
    } catch (e) {
      console.warn("[rdom]", "rd$ failed parsing:", String.raw(template, ...(values.map(v => "${"+v+"}"))), "\n", e);
      throw e;
    }
  },

  /**
   * Build the result of rdparse$ into a HTML element.
   * @param {HTMLElement} el
   * @param {RDOMParseResult} data 
   * @returns {HTMLElement}
   */
  rdbuild(el, data) {
    let elEmpty = null;
    if (el && el.tagName === "RDOM-EMPTY") {
      elEmpty = el;
      el = null;
    }

    /** @type {HTMLElement} */
    let nodeBase = null;
    if (data.html) {
      let html = data.html.trim();
      nodeBase = rdom._cachedTemplates.get(html);
      if (!nodeBase) {
        nodeBase = document.createElement("template");
        nodeBase.innerHTML = html;
        // @ts-ignore
        nodeBase = nodeBase.content.firstElementChild;
        rdom._cachedTemplates.set(html, nodeBase);
      }
    }

    if (!nodeBase && data.init)
      return el || data.init(null) || null;

    let init = !el && data.init;
    /** @type {HTMLElement} */
    // @ts-ignore
    let rel = rdom.init(el || document.importNode(nodeBase, true));

    if (!rel["rdomCtx"])
      rel["rdomCtx"] = ""+(++rdom._lastID);

    for (let { id, value } of data.texts) {
      let el = rdom.find(rel, 1, id, "text");
      if (el && value !== undefined) {
        if (el.tagName === "RDOM-TEXT" && el.parentNode.childNodes.length === 1) {
          // Inline rdom-text.
          el = el.parentElement;
          el.removeChild(el.children[0]);
          el.setAttribute("rdom-text", id);
        }
        el.textContent = value;
      }
    }

    // "Collect" fields.
    for (let wrap of data.fields) {
      let { h, key, state, value } = wrap;
      h = h || wrap;
      let el = rdom.init(rdom.find(rel, key));
      // @ts-ignore
      let fields = el["rdomFields"];
      // @ts-ignore
      let states = el["rdomStates"];

      if (!fields[key]) {
        // Initialize the field.
        el.setAttribute(
          "rdom-fields",
          `${el.getAttribute("rdom-fields") || ""} ${key}`.trim()
        );
        fields[key] = h;
        states[key] = state;
        if (h.init)
          h.init(state, el, key);
      }

      // Set the value.
      if (value !== undefined)
        h.set(states[key], el, value);
    }

    for (let { id, value } of data.renderers) {
      let el = rdom.find(rel, 1, id, "render");
      if (el && value !== undefined) {
        let p = el.parentNode;
        if (value && value instanceof Function)
          value = value(el.tagName === "RDOM-EMPTY" ? null : el);
        value = value || rd$(null)`<rdom-empty/>`;
        if (el !== value && !(el.tagName === "RDOM-EMPTY" && value.tagName === "RDOM-EMPTY")) {
          // Replace (fill) the field.
          p.replaceChild(value, el);
          value.setAttribute("rdom-render", id);
        }
      }
    }

    if (elEmpty && elEmpty.parentNode)
      elEmpty.parentNode.replaceChild(rel, elEmpty);

    if (init) {
      let rv = init(rel);
      if (rv instanceof HTMLElement)
        rel = rv || rel;
    }
    return rel;
  },

  /**
   * Parse a template string into an existing HTML element, escaping expressions unprefixed with $, inserting attribute arrays and preserving child nodes.
   * Returns a function parsing a given template string into the given HTML element.
   * @param {HTMLElement} el
   */
  rd$(el) {
    /**
     * @param {TemplateStringsArray} template
     * @param {any[]} values
     */
    function rd$dyn(template, ...values) { return rdbuild(el, rdparse$(template, ...values)); }
    return rd$dyn;
  },

  /**
   * Parse a template string, escaping expressions unprefixed with $.
   * @param {TemplateStringsArray} template
   * @param {...any} values
   * @returns {string}
   */
  escape$(template, ...values) {
    return template.reduce(function escape$reduce(prev, next, i) {
      let val = values[i - 1];
      let t = prev[prev.length - 1];

      if (t === "$") {
        // Keep value as-is.
        return prev.slice(0, -1) + val + next;
      }

      // Escape HTML
      if (val && val.join)
        val = val.join(" ");
      else
        val = rdom.escape(val);

      if (t === "=")
        // Escape attributes.
        val = `"${val}"`;

      return prev + val + next;
    }).trim();
  },

}

export var rdparse$ = rdom.rdparse$;
export var rdbuild = rdom.rdbuild;
export var rd$ = rdom.rd$;
export var escape$ = rdom.escape$;

/**
 * Sample RDOM field handlers.
 */
export var rd = {
  _: (h, key, state) => {
    return {
      key: key,
      state: state,
      init: h.init,
      get: h.get,
      set: h.set,
    };
  },

  _attr: {
    get: (s, el) => s.v,
    set: (s, el, v) => {
      if (s.v === v)
        return;
      let prev = s.v;
      s.v = v;
      if (s.name.startsWith("on") && v instanceof Function) {
        let ev = s.name.slice(2);
        if (prev && prev instanceof Function)
          el.removeEventListener(ev, prev);
        el.addEventListener(ev, s.v = v.bind(el), false);
        return;
      }

      el.setAttribute(s.name, v);
    }
  },
  attr: (key, name) => rd._(rd._attr, key, {
    name: name || key,
    v: undefined,
  }),

  _toggleClass: {
    get: (s) => s.v,
    set: (s, el, v) => {
      if (s.v === v)
        return;
      s.v = v;
      if (v) {
        el.classList.add(s.nameTrue);
        if (s.nameFalse)
          el.classList.remove(s.nameFalse);
      } else {
        el.classList.remove(s.nameTrue);
        if (s.nameFalse)
          el.classList.add(s.nameFalse);
      }
    }
  },
  toggleClass: (key, nameTrue, nameFalse) => rd._(rd._toggleClass, key, {
    nameTrue: nameTrue || key,
    nameFalse: nameFalse,
    v: undefined,
  }),

  _html: {
    get: (s, el) => s.v,
    set: (s, el, v) => {
      if (s.v === v)
        return;
      s.v = v;
      el.innerHTML = v;
    }
  },
  html: (key) => rd._(rd._html, key, {
    v: undefined,
  }),
}

/**
 * A list container context.
 */
export class RDOMListHelper {
  /**
   * @param {HTMLElement} container
   */
  constructor(container, ordered = true) {
    if (container["rdomListHelper"]) {
      let ctx = container["rdomListHelper"];
      ctx.ordered = ordered;
      return ctx;
    }
    
    this.container = container;
    this.container["rdomListHelper"] = this;

    this.ordered = ordered;

    /** 
     * Set of previously added elements.
     * This set will be checked against [added] on cleanup, ensuring that any zombies will be removed properly.
     * @type {Set<HTMLElement>}
     */
    this.prev = new Set();
    /**
     * Set of [rdom.add]ed elements.
     * This set will be used and reset in [rdom.cleanup].
     * @type {Set<HTMLElement>}
     */
    this.added = new Set();

    /**
     * All current element -> object mappings.
     * @type {Map<HTMLElement, any>}
     */
    this.refs = new Map();
    /**
     * All current object -> element mappings.
     * @type {Map<any, HTMLElement>}
     */
    this.elems = new Map();

    this._i = -1;
  }

  /**
   * Adds or updates an element.
   * This function needs a reference object so that it can find and update existing elements for any given object.
   * @param {any} ref The reference object belonging to the element.
   * @param {any} render The element renderer. Either function(HTMLElement) : HTMLElement, or an object with a property "render" with such a function.
   * @returns {HTMLElement} The created / updated wrapper element.
   */
  add(ref, render) {
    // Check if we already added an element for ref.
    // If so, update it. Otherwise create and add a new element.
    let el = this.elems.get(ref);
    let elOld = el;
    // @ts-ignore
    el = render.render ? render.render(el) : render(el);

    if (elOld) {
      if (elOld !== el)
        this.container.replaceChild(el, elOld);
    } else {
      this.container.appendChild(el);
    }

    if (this.ordered) {
      // Move the element to the given index.
      rdom.move(el, ++this._i);
    }

    // Register the element as "added:" - It's not a zombie and won't be removed on cleanup.
    this.added.add(el);
    // Register the element as the element of ref.
    this.refs.set(el, ref);
    this.elems.set(ref, el);
    return el;
  }

  /**
   * Remove an element from this context, both the element in the DOM and all references in RDOM.
   * @param {HTMLElement} el The element to remove.
   */
  remove(el) {
    if (!el)
      return;
    let ref = this.refs.get(el);
    if (!ref)
      return; // The element doesn't belong to this context - no ref object found.
    // Remove the element and all related object references from the context.
    this.refs.delete(el);
    this.elems.delete(ref);
    // Remove the element from the DOM.
    el.remove();
  }

  /**
   * Remove zombie elements and perform any other ending cleanup.
   * Call this after the last [add].
   */
  end() {
    for (let el of this.prev) {
      if (this.added.has(el))
        continue;
      this.remove(el);
    }
    let tmp = this.prev;
    this.prev = this.added;
    this.added = tmp;
    this.added.clear();
    this._i = -1;
  }

}
