//@ts-check
import { rd, rdom, rd$, RDOMListHelper } from "../js/rdom.js";
import { DateTime } from "../js/deps/luxon.js";

const clientrc = `http://localhost:38038/`;

const elDim = document.getElementById("dim");
const elDialog = document.getElementById("dialog");
const elDialogText = document.getElementById("dialog-text");

function li(value) {
	return el => rd$(el)`<li>${value}</li>`;
}

function fetchStatus() {
	const el = document.getElementById("status-list");
	fetch("/status").then(r => r.json()).then(status => {
		for (let dummy of el.querySelectorAll(".dummy"))
			dummy.remove();

		const list = new RDOMListHelper(el);

		let mem = status.GCMemory;
		let memSuffix = " bytes";
		const memSuffixes = [ "kB", "MB", "GB", "TB" ];

		for (let i = 0; i < memSuffixes.length && mem >= 1024; i++) {
			mem = Math.round(mem / 1024);
			memSuffix = memSuffixes[i];
		}

		const startupDate = DateTime.fromMillis(status.StartupTime).setLocale("en-GB");
		list.add("uptime", li(`Last server restart: ${startupDate.toFormat("yyyy-MM-dd HH:mm:ss")}`));
		list.add("memory", li(`Memory used: ${mem}${memSuffix}`));
		list.add("modules", li(`Modules loaded: ${status.Modules}`));
		list.add("playersTotal", li(`Players since restart: ${status.PlayerCounter}`));
		list.add("playersReg", li(`Registered: ${status.Registered}`));
		list.add("playersBan", li(`Banned: ${status.Banned}`));
		list.add("players", li(`Online: ${status.PlayerRefs}`));

		list.end();
	});
}

function renderUser() {
	const el = document.getElementById("userpanel");
	fetch("/userinfo").then(r => r.json()).then(info => {
		for (let dummy of el.querySelectorAll(".dummy"))
			dummy.remove();

		const list = new RDOMListHelper(el);

		if (info.Error) {
			list.add("linkerror", el => rd$(el)`
			<p>
				Create a CelesteNet account<br>
				by linking your Discord account.<br>
				<br>
				<a id="button-auth" class="button" href="/discordauth"><span class="button-icon"></span><span>Link your account</span></a>
			</p>`);

			// list.add("error", li(info.Error));
			list.end();
			return;
		}

		list.add("userinfo", el => rd$(el)`
		<p>
			Linked to:<br>
			<a id="button-reauth" class="button" href="/discordauth">
				<span class="button-icon"></span>
				<span class="button-text">
					<span class="button-icon discord-avatar" style=${`background-image: url(/avatar?uid=${info.UID})`}></span>
					${info.Name}#${info.Discrim}
				</span>
			</a>
		</p>`);

		list.add("key", el => rd$(el)`
		<p>
			Your key:<br>
			<a id="button-copykey" class="button" onclick=${() => navigator.clipboard.writeText("#" + info.Key)}>
				<span class="button-icon"></span>
				<span class="button-text">#${info.Key}</span>
			</a><br>
			<a id="button-sendkey" class="button" onclick=${() => sendKey(info.Key)}>
				<span class="button-icon"></span>
				<span class="button-text">Send to Client</span>
			</a><br>
			<a id="button-revokekey" class="button" onclick=${() => revokeKey()}>
				<span class="button-icon"></span>
				<span class="button-text">Revoke Key</span>
			</a>
		</p>`);

		list.end();
	});
}

function deauth() {
	fetch("/deauth").then(() => window.location.reload());
}

function revokeKey() {
	fetch("/revokekey").then(() => window.location.reload());
}

function dialog(content) {
	if (!content) {
		elDim.className = "";
		elDialog.className = "";
		return;
	}

	elDim.className = "active";
	elDialog.className = "active";
	elDialogText.innerHTML = content;
}

function sendKey(key) {
	const controller = new AbortController();
	setTimeout(() => controller.abort(), 500);
	fetch(`${clientrc}setkey?value=${key}`, { signal: controller.signal }).then(
		() => dialog("Sent. Check your mod options."),
		() => dialog("Couldn't find client.<br>Is Everest running?<br>Is CelesteNet enabled?")
	);
}

setInterval(fetchStatus, 30000);
fetchStatus();
renderUser();
dialog();

elDim.addEventListener("click", () => dialog());

window["deauth"] = deauth;
window["dialog"] = dialog;
