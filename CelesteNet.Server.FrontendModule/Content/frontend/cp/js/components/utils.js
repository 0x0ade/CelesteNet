//@ts-check
import { DateTime } from "../../../js/deps/luxon.js";

export class FrontendUtils {
  /**
   * @param {import("../frontend.js").Frontend} frontend
   */
  constructor(frontend) {
    this.frontend = frontend;

    /** @type {WeakMap<any, number>} */
    this._uidMap = new WeakMap();
    this._uidLast = 0;
  }

  /**
   * @param {any} ref
   */
  getUID(ref) {
    if (this._uidMap.has(ref))
      return this._uidMap.get(ref);

    let value = this._uidLast++;
    this._uidMap.set(ref, value);
    return value;
  }

  /**
   * @param {() => void} func
   */
  time(func) {
    let then = performance.now();
    func();
    let now = performance.now();
    return now - then;
  }

  /**
   * @param {number | string} key
   */
  hexcolor(key) {
    if (typeof key === "number")
      return key;
    if (key[0] === "#")
      key = key.slice(1);
    return parseInt(key, 16);
  }

  /**
   * @param {number} millis
   */
  datetime(millis) {
    return DateTime.fromMillis(millis).setLocale("en-GB").toFormat("yyyy-MM-dd HH:mm:ss")
  }

}
