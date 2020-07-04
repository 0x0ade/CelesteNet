//@ts-check
(async function() {
  console.log("[boot]", "Waiting until DOM ready.");
  await lazyman.load("DOM");
  console.log("[boot]", "DOM ready.");

  document.body.addEventListener("mousedown", e => {
    if (e.button === 1)
      e.preventDefault();
  });
  document.addEventListener("contextmenu", e => {
    e.preventDefault();
  }, false);

  let splash = document.getElementById("splash");
  let splashProgressBar = document.getElementById("splash-progress-bar");

  let loaded = new Set();
  let failed = new Set();

  let monacoBase = "https://unpkg.com/monaco-editor@0.20.0/min";

  /** @type {(string | string[])[]} */
  let depsBase = [
    "frontend.css",
  ];

  depsBase = [...depsBase,
    // https://material.io/develop/web/docs/getting-started/
    ["material-icons.css", "https://fonts.googleapis.com/icon?family=Material+Icons"],
    ["Roboto.css", "https://fonts.googleapis.com/css?family=Roboto:300,400,500,600,700"],
    "https://unpkg.com/material-components-web@6.0.0/dist/material-components-web.min.css",
    "https://unpkg.com/material-components-web@6.0.0/dist/material-components-web.min.js",

    // https://github.com/showdownjs/showdown
    "https://cdnjs.cloudflare.com/ajax/libs/showdown/1.9.1/showdown.min.js",
  ];

  depsBase = [...depsBase,
  ];

  /** @type {(string | string[])[]} */
  let core = [
    // Frontend itself.
    ["frontend.mjs", "frontend.js"]
  ];
  let depsLength = depsBase.length + core.length + 1;

  // Update the splash screen's progress bar.
  let updateProgress = (id, success) => {
    console.log("[boot]", id, success ? "loaded." : "failed loading!");
    (success ? loaded : failed).add(id);
    if (!success) {
      splash.classList.add("failed");
      splashProgressBar.style.transform = `scaleX(1)`;
      return;
    }
    if (failed.size !== 0)
      return;
    splashProgressBar.style.transform = `scaleX(${0.25 * (loaded.size / depsLength)})`;
  }

  // Wrapper around lazyman.all which runs updateProgress.
  let load = (deps, ordered) => lazyman.all(
    deps, ordered,
    id => updateProgress(id, true),
    id => updateProgress(id, false),
  );

  console.log("[boot]", "Loading Frontend and all dependencies.");

  await load(depsBase, true);
  console.log("[boot]", "Base dependencies loaded.");

  // Monaco is some fun mess.
  {
    await load([
      // https://microsoft.github.io/monaco-editor/
      ["monaco-loader.js", `${monacoBase}/vs/loader.js`]
    ]);
    console.log("[boot]", "Monaco loader ready.");

    // Adapted from https://github.com/microsoft/monaco-editor/blob/master/docs/integrate-amd-cross.md

    window["require"].config({
      paths: {
        "vs": `${monacoBase}/vs`
      }
    });

    window["MonacoEnvironment"] = {
      getWorkerUrl: (workerId, label) => {
        return `data:text/javascript;charset=utf-8,${encodeURIComponent(`
          self.MonacoEnvironment = {
            baseUrl: '${monacoBase}/'
          };
          importScripts('${monacoBase}/vs/base/worker/workerMain.js');`
        )}`;
      }
    };

    await new Promise((resolve, reject) => {
      try {
        window["require"](["vs/editor/editor.main"], () => resolve(), () => reject());
      } catch (e) {
        reject(e);
      }
    });

    // These are needed by Monaco but cause issues when loading certain deps.
    /*
    window["requireMonaco"] = window["require"];
    window["require"] = null;
    window["defineMonaco"] = window["define"];
    window["define"] = null;
    */
  }

  console.log("[boot]", "Monaco loaded.");

  await load(core, true);
  console.log("[boot]", "Frontend prepared.");

  await window["frontend"].start();
  console.log("[boot]", "Frontend loaded.");

  splash.classList.add("hidden");
  document.getElementsByTagName("app")[0].classList.add("ready");
  setTimeout(() => {
    splash.remove();
  }, 2000);

})().catch(e => {
  document.getElementById("splash").classList.add("failed");
  if (window["nodeRequire"])
    window["nodeRequire"]("electron").remote.getCurrentWindow().webContents.openDevTools();
  throw e;
});
