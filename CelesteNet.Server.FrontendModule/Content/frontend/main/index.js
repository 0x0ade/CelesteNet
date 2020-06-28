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

		const startupDate = DateTime.fromMillis(status.StartupTime).setLocale("en-GB");
		list.add("uptime", li(`Last server restart: ${startupDate.toRelativeCalendar()}, ${startupDate.toFormat("yyyy-MM-dd HH:mm:ss")}`));
		list.add("playersTotal", li(`Players since restart: ${status.PlayerCounter}`));
		list.add("playersReg", li(`Registered: ${status.PlayerRegistered}`));
		list.add("playersBan", li(`Banned: ${status.Bans}`));
		list.add("players", li(`Currently online: ${status.PlayerRefs}`));

		list.end();
	});
}

setInterval(fetchStatus, 10000);
fetchStatus();
