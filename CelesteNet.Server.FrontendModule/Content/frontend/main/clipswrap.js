//@ts-check
import { rd, rdom, rd$, RDOMListHelper } from "../js/rdom.js";

{
	const elClipsWrap = document.getElementById("front-clips-wrap");
	/** @type {HTMLVideoElement} */
	// @ts-ignore
	const elClipsVideo = document.getElementById("front-clips-video");
	/** @type {HTMLCanvasElement} */
	// @ts-ignore
	const elClipsCanvas = rd$(null)`<canvas id="front-clips-canvas"></canvas>`;
	elClipsWrap.appendChild(elClipsCanvas);
	const ctx2d = elClipsCanvas.getContext("2d");

	const vidW = elClipsVideo.width;
	const vidH = elClipsVideo.height;

	let start = 0;
	let then = window.performance.now();
	let skip = 0;
	let manualTime = 0;
	let manualTimeMax = 1;

	let animCanvasFrame;
	function animCanvas() {
		const rect = elClipsCanvas.getBoundingClientRect();
		if (rect.bottom <= elClipsCanvas.clientHeight / 2) {
			elClipsVideo.pause();
			setTimeout(animCanvas, 100);
			return;
		}

		if (skip > 0) {
			skip--;
			animCanvasFrame = requestAnimationFrame(animCanvas);
			return;
		}

		let now = window.performance.now();
		if (!start)
			start = now;
	  	now -= start;
		const delta = now - then;
		then = now;

		let paused = elClipsVideo.paused;
		if (paused) {
			try {
				elClipsVideo.play().then(() => paused = false).catch(() => {});
			} catch (e) {
			}
		}

		if (paused) {
			manualTime -= delta / 1000 * elClipsVideo.playbackRate;
			if (manualTime <= 0) {
				manualTimeMax = manualTime = delta / 1000 + Math.random() * 0.2 + 0.1;
				elClipsVideo.currentTime = (elClipsVideo.currentTime + manualTime) % elClipsVideo.duration;
			}
			skip = 2;
		} else {
			skip = 0;
		}


		// TODO: Adjust skip and scale based on delta.

		const ctxW = elClipsCanvas.clientWidth / 8;
		const ctxH = elClipsCanvas.clientHeight / 8;
		if (elClipsCanvas.width != ctxW || elClipsCanvas.height != ctxH) {
			elClipsCanvas.width = ctxW;
			elClipsCanvas.height = ctxH;
		}

		const scaleW = ctxW / vidW;
		const scaleH = ctxH / vidH;
		let scale;
		if (scaleW > scaleH) {
			scale = scaleW;
		} else {
			scale = scaleH;
		}

		const w = vidW * scale;
		const h = vidH * scale;
		ctx2d.filter = "blur(5px)";
		ctx2d.globalAlpha = paused ? 0.4 : 0.6;
		ctx2d.drawImage(
			elClipsVideo,
			ctxW / 2 - w / 2,
			ctxH / 2 - h / 2,
			w,
			h
		);

		animCanvasFrame = requestAnimationFrame(animCanvas);
	}

	function animCanvasStart() {
		elClipsVideo.removeEventListener("canplay", animCanvasStart);
		animCanvasFrame = requestAnimationFrame(() => {
			if (!Number.isNaN(elClipsVideo.duration))
				elClipsVideo.currentTime = Math.floor((Math.random() * 0.8 + 0.1) * elClipsVideo.duration * 10) / 10;
			elClipsVideo.playbackRate = 0.5;
			elClipsVideo.classList.add("disabled");
			animCanvas();
		});
	}

	if (Number.isNaN(elClipsVideo.duration)) {
		elClipsVideo.addEventListener("canplay", animCanvasStart);
	} else {
		animCanvasStart();
	}
}
