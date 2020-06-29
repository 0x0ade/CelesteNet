//@ts-check
import { rd, rdom, rd$, RDOMListHelper } from "../js/rdom.js";
import { DateTime } from "../js/deps/luxon.js";

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
				<span class="button-icon discord-avatar" style=${`background-image: url(https://cdn.discordapp.com/avatars/${info.UID}/${info.Avatar}.png)`}></span>
				<span>${" " + info.Name}#${info.Discrim}</span>
			</a>
		</p>`);

		list.add("key", el => rd$(el)`
		<p>
			Your key:<br>
			<a id="button-copykey" class="button" onclick="copyFrom(this)">
				<span class="button-icon"></span>
				<span>#${info.Key}</span>
			</a>
		</p>`);

		list.end();
	});
}

function deauth() {
	fetch("/deauth").then(() => window.location.reload());
}

/**
 * @param {HTMLElement} el
 */
function copyFrom(el) {
	navigator.clipboard.writeText(el.textContent.trim());
}

setInterval(fetchStatus, 10000);
fetchStatus();
renderUser();

window["deauth"] = deauth;
window["copyFrom"] = copyFrom;
