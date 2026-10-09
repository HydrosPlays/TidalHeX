// TidalHeX visual effects: backdrop decorations + anime.js helpers.
window.TidalFx = (() => {
  const A = window.anime;
  let reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const running = [];

  const svgNS = 'http://www.w3.org/2000/svg';
  const el = (tag, attrs = {}, parent) => {
    const e = document.createElementNS(svgNS, tag);
    for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
    if (parent) parent.appendChild(e);
    return e;
  };

  // ---------------------------------------------------------------- backdrop
  // CPU budget: ambient motion only runs on the home and boot scenes, and only while the window is visible and
  // focused. On pages the backdrop sits behind frosted (backdrop-filter) panels, where any movement forces every
  // panel's blur to be recomputed each frame for motion nobody can see, so pages get a still backdrop.
  let host, svg, canvas, mode = 'home';
  let bubbles = null; // { start(), stop(), clear() }
  // Tidal ZA swaps the sea's rings and bubbles for Legends: Z-A's title screen: floating squares and green data dots.
  let theme = document.documentElement.dataset.theme || 'light';
  const isZA = () => theme === 'za';
  // Tidal Pixel: a flat scrolling pattern (CSS) and blocky pixel bubbles instead of rings and glows.
  const isPixel = () => theme === 'pixel';
  // Tidal Arceus: a hazy field (CSS) with far ridge lines and a slowly turning emblem; no particles at all.
  const isArceus = () => theme === 'arceus';

  function mount(container) {
    host = container;
    host.innerHTML = '<div class="sea"></div><div class="shine"></div><div class="grid"></div>';
    svg = el('svg', { preserveAspectRatio: 'none' });
    host.appendChild(svg);
    canvas = document.createElement('canvas');
    host.appendChild(canvas);
    let resizeTimer = 0;
    window.addEventListener('resize', () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => draw(mode, true), 120); });
    bubbles = createBubbles();
    document.addEventListener('visibilitychange', updateMotion);
    window.addEventListener('focus', updateMotion);
    window.addEventListener('blur', updateMotion);
  }

  /** True when ambient motion is worth its CPU: animated scene, motion allowed, window visible and focused. */
  function motionWanted() {
    return !reduced && !!A && mode !== 'page' && !document.hidden && document.hasFocus();
  }

  function updateMotion() {
    document.documentElement.classList.toggle('inactive', document.hidden || !document.hasFocus());
    const on = motionWanted();
    for (const a of running) { try { if (on) a.play(); else a.pause(); } catch { /* */ } }
    host?.classList.toggle('moving', on); // CSS drift of the shine
    if (!bubbles) return;
    if (on && !isArceus()) bubbles.start();
    else { bubbles.stop(); if (mode === 'page' || reduced || isArceus()) bubbles.clear(); }
  }

  function clearLoops() {
    while (running.length) { try { running.pop().pause(); } catch { /* */ } }
  }

  /** Draws decorations for a scene: 'home' (rings), 'page' (brackets + circuits), 'boot'. */
  function draw(next, force = false) {
    if (next === mode && !force && svg?.childElementCount) return; // page → page: same backdrop, don't rebuild it
    mode = next;
    if (!svg) return;
    clearLoops();
    host.querySelectorAll(':scope > .arc').forEach(a => a.remove());
    const w = window.innerWidth, h = window.innerHeight;
    svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
    svg.innerHTML = '';
    const defs = el('defs', {}, svg);
    const glow = el('filter', { id: 'glow', x: '-20%', y: '-20%', width: '140%', height: '140%' }, defs);
    el('feGaussianBlur', { stdDeviation: '3', result: 'b' }, glow);
    const merge = el('feMerge', {}, glow);
    el('feMergeNode', { in: 'b' }, merge); el('feMergeNode', { in: 'SourceGraphic' }, merge);

    if (isArceus()) {
      drawRidges(w, h);
      if (mode !== 'page') drawEmblem(w, h);
      updateMotion();
      return;
    }

    if (isZA() || isPixel()) {
      if (isZA()) drawSquares(w, h, mode === 'page' ? 0.6 : 0.45); // home: fainter, the moving squares (canvas) fill in
      if (mode === 'page') bubbles?.clear();
      updateMotion();
      return;
    }

    if (mode === 'home') drawRings(w * 0.5, h * 0.66, Math.max(w, h) * 0.12, 7, 0.16);
    else drawRings(w * 0.5, h * 0.52, Math.min(w, h) * 0.2, 4, 0.07);
    if (mode !== 'home') { drawBrackets(w, h); drawCircuits(w, h, mode === 'boot' ? 1 : 0.55); }
    else drawCircuits(w, h, 0.35);
    if (mode === 'page') bubbles?.clear();
    updateMotion();
  }

  /** Loops are only created for animated scenes; updateMotion() pauses them while the window is inactive. */
  const animated = () => !reduced && !!A && mode !== 'page';

  function drawRings(cx, cy, r0, count, alpha) {
    const g = el('g', { class: 'deco rings', opacity: alpha * 6 }, svg);
    for (let i = 0; i < count; i++) {
      const r = r0 * (1 + i * 0.62);
      el('circle', { cx, cy, r, fill: 'none', stroke: '#ffffff', 'stroke-opacity': Math.max(0.05, alpha - i * 0.015), 'stroke-width': i % 3 === 0 ? 16 : 3 }, g);
    }
    // A few rotating dashed arcs for motion. Each is its own layer spun by a CSS animation: the compositor rotates
    // it without repainting (rotating them inside the backdrop SVG repainted most of the screen every frame).
    for (let i = 0; i < 3; i++) {
      const r = r0 * (1.3 + i * 1.1), size = 2 * r + 4;
      const layer = el('svg', { class: 'arc' + (i % 2 ? ' ccw' : ''), width: size, height: size, viewBox: `0 0 ${size} ${size}` });
      Object.assign(layer.style, { left: `${cx - size / 2}px`, top: `${cy - size / 2}px`, opacity: alpha * 6, animationDuration: `${60 + i * 20}s` });
      el('circle', { cx: size / 2, cy: size / 2, r, fill: 'none', stroke: '#bff8ff', 'stroke-opacity': 0.28, 'stroke-width': 2, 'stroke-dasharray': `${r * 0.6} ${r * 5}`, 'stroke-linecap': 'round' }, layer);
      host.insertBefore(layer, canvas);
    }
  }

  function drawBrackets(w, h) {
    const g = el('g', { class: 'deco brackets' }, svg);
    const side = (flip) => {
      const x0 = flip ? w : 0, s = flip ? -1 : 1;
      const inset = Math.max(90, w * 0.1), bulge = Math.max(46, w * 0.045);
      const top = h * 0.13, bottom = h * 0.87;
      const d = `M ${x0 + s * (inset - 30)} -10 L ${x0 + s * inset} ${top} Q ${x0 + s * (inset - bulge)} ${h / 2} ${x0 + s * inset} ${bottom} L ${x0 + s * (inset - 30)} ${h + 10}`;
      // lighter band outside the frame
      el('path', { d: `${d} L ${x0} ${h + 10} L ${x0} -10 Z`, fill: '#bff8ff', 'fill-opacity': 0.09 }, g);
      el('path', { d, fill: 'none', stroke: '#7ef4ff', 'stroke-width': 3, 'stroke-opacity': 0.85, filter: 'url(#glow)' }, g);
      el('path', { d: `M ${x0 + s * (inset - 18)} ${top + 40} Q ${x0 + s * (inset - bulge - 16)} ${h / 2} ${x0 + s * (inset - 18)} ${bottom - 40}`, fill: 'none', stroke: '#ffffff', 'stroke-width': 1.5, 'stroke-opacity': 0.35 }, g);
    };
    side(false); side(true);
  }

  /** Tidal Arceus's still layer: two soft ridge lines low on the screen, like hills seen through haze. */
  function drawRidges(w, h) {
    const defs = svg.querySelector('defs');
    const fade = (id, top) => {
      const g = el('linearGradient', { id, x1: 0, y1: 0, x2: 0, y2: 1 }, defs);
      el('stop', { offset: 0, 'stop-color': '#1f2226', 'stop-opacity': top }, g);
      el('stop', { offset: 1, 'stop-color': '#1f2226', 'stop-opacity': 0 }, g);
    };
    fade('arc-far', 0.2); fade('arc-near', 0.32);
    const g = el('g', { class: 'deco ridges' }, svg);
    const p = (x, y) => `${(x * w).toFixed(1)} ${(y * h).toFixed(1)}`;
    el('path', { fill: 'url(#arc-far)', d: `M ${p(0, .64)} Q ${p(.12, .55)} ${p(.24, .6)} T ${p(.46, .57)} T ${p(.7, .61)} T ${p(1, .56)} V ${h} H 0 Z` }, g);
    el('path', { fill: 'url(#arc-near)', d: `M ${p(0, .78)} Q ${p(.18, .7)} ${p(.34, .75)} T ${p(.62, .72)} T ${p(1, .76)} V ${h} H 0 Z` }, g);
  }

  /**
   * Tidal Arceus's emblem on the home and boot screens: a ring with four petals (an original mark). It is its own
   * layer, turned by the compositor like the ring arcs; sized inline because `.backdrop svg` fills the screen.
   */
  function drawEmblem(w, h) {
    const size = Math.round(Math.min(w, h) * (mode === 'boot' ? 0.62 : 0.8));
    const cx = w * (mode === 'boot' ? 0.5 : 0.78), cy = h * (mode === 'boot' ? 0.47 : 0.54);
    const layer = el('svg', { class: 'arc emblem', viewBox: '0 0 100 100' });
    Object.assign(layer.style, { left: `${Math.round(cx - size / 2)}px`, top: `${Math.round(cy - size / 2)}px`, width: `${size}px`, height: `${size}px`, opacity: 0.075, animationDuration: '420s' });
    const ink = '#f3ecd1';
    for (let i = 0; i < 4; i++) el('path', { d: 'M50 43C41 33 41 19 50 7c9 12 9 26 0 36z', fill: ink, transform: `rotate(${i * 90} 50 50)` }, layer);
    el('circle', { cx: 50, cy: 50, r: 4.5, fill: 'none', stroke: ink, 'stroke-width': 2.5 }, layer);
    el('circle', { cx: 50, cy: 50, r: 46, fill: 'none', stroke: ink, 'stroke-width': 1.5, 'stroke-dasharray': '60.26 12', 'stroke-dashoffset': 30.13 }, layer);
    for (const [x, y] of [[82.5, 17.5], [82.5, 82.5], [17.5, 82.5], [17.5, 17.5]]) el('circle', { cx: x, cy: y, r: 2, fill: ink }, layer);
    host.insertBefore(layer, canvas);
  }

  /** Tidal ZA's still layer: hollow and filled squares scattered like the Z-A title screen (same layout every time). */
  function drawSquares(w, h, alpha) {
    const g = el('g', { class: 'deco squares', opacity: alpha }, svg);
    let seed = 7;
    const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
    for (let i = 0; i < 34; i++) {
      const s = 8 + rnd() * 20, x = rnd() * w, y = rnd() * h, filled = rnd() < 0.35;
      el('rect', filled
        ? { x, y, width: s, height: s, fill: '#b8c2bf', 'fill-opacity': 0.12 + rnd() * 0.22 }
        : { x, y, width: s, height: s, fill: 'none', stroke: '#e3ece8', 'stroke-opacity': 0.14 + rnd() * 0.3, 'stroke-width': 1.5 }, g);
    }
  }

  function drawCircuits(w, h, alpha) {
    const g = el('g', { class: 'deco circuits', opacity: alpha }, svg);
    const lines = [
      [[0, 0.2], [0.07, 0.2], [0.1, 0.16], [0.3, 0.16]],
      [[0.47, 0.215], [0.75, 0.215], [0.79, 0.28], [0.95, 0.28]],
      [[0.54, 0.35], [0.56, 0.31], [0.87, 0.31]],
      [[0.28, 0.49], [0.74, 0.49]],
      [[0, 0.71], [0.34, 0.71]],
      [[1, 0.82], [0.9, 0.82], [0.86, 0.9], [0.66, 0.9]],
    ];
    lines.forEach((pts, i) => {
      const d = pts.map((p, j) => `${j ? 'L' : 'M'} ${p[0] * w} ${p[1] * h}`).join(' ');
      el('path', { d, fill: 'none', stroke: '#ffffff', 'stroke-opacity': 0.45, 'stroke-width': 1.5 }, g);
      // A travelling light pulse along each line. No blur filter: a filtered path is re-rasterized every frame,
      // a soft wide stroke under a bright thin one looks the same for a fraction of the cost.
      if (!animated()) return;
      for (const [width, opacity] of [[7, 0.25], [2.5, 1]]) {
        const pulse = el('path', { d, fill: 'none', stroke: '#e6fdff', 'stroke-opacity': opacity, 'stroke-width': width, 'stroke-linecap': 'round', 'stroke-dasharray': '40 4000', 'stroke-dashoffset': 40 }, g);
        running.push(A.animate(pulse, { strokeDashoffset: { from: 40, to: -4000 }, duration: 9000 + i * 1300, delay: i * 900, loop: true, ease: 'linear' }));
      }
    });
  }

  // ---------------------------------------------------------------- bubbles
  /** Rising bubbles on a canvas, capped at 30 fps (high-refresh monitors would otherwise redraw 120–144×/s). */
  function createBubbles() {
    const ctx = canvas.getContext('2d');
    let list = [], W = 0, H = 0, dpr = 1, frame = 0, last = 0;
    const FRAME_MS = 1000 / 30;
    const resize = () => {
      dpr = window.devicePixelRatio || 1; W = window.innerWidth; H = window.innerHeight;
      canvas.width = W * dpr; canvas.height = H * dpr; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };
    resize(); window.addEventListener('resize', resize);
    const spawn = (y) => ({ x: Math.random() * W, y: y ?? H + 20, r: 1.5 + Math.random() * 5, v: 0.25 + Math.random() * 0.6, p: Math.random() * 6.28, a: 0.15 + Math.random() * 0.3 });
    for (let i = 0; i < 24; i++) list.push(spawn(Math.random() * H));
    // Tidal ZA: squares drifting up, and columns of green dots falling like the title screen's data.
    const spawnSquare = (y) => ({ x: Math.random() * W, y: y ?? H + 30, s: 6 + Math.random() * 18, v: 0.12 + Math.random() * 0.35, a: 0.12 + Math.random() * 0.33, fill: Math.random() < 0.4 });
    const spawnStream = (y) => ({ x: Math.round(Math.random() * W / 8) * 8, y: y ?? -Math.random() * H * 0.5, v: 0.5 + Math.random() * 1.1, len: 6 + Math.floor(Math.random() * 16), a: 0.25 + Math.random() * 0.4 });
    let squares = Array.from({ length: 22 }, () => spawnSquare(Math.random() * H));
    let streams = Array.from({ length: 20 }, () => spawnStream(Math.random() * H));
    // Tidal Pixel: bubbles drawn as blocky pixel rings (3 screen pixels per pixel), rising straight up.
    const BUBBLE_PX = ['.##.', '#..#', '#..#', '.##.'];
    const drawPixel = (step) => {
      for (const b of list) {
        b.y -= b.v * step;
        const s = b.r > 4 ? 4 : 3, x = Math.round((b.x + Math.sin(b.p += 0.01 * step) * 4) / s) * s, y = Math.round(b.y / s) * s;
        ctx.fillStyle = `rgba(255,255,255,${Math.min(0.85, b.a + 0.35)})`;
        BUBBLE_PX.forEach((row, ry) => { for (let rx = 0; rx < 4; rx++) if (row[rx] === '#') ctx.fillRect(x + rx * s, y + ry * s, s, s); });
        ctx.fillRect(x + s, y + s, s, s); // shine
      }
      list = list.map(b => (b.y < -20 ? spawn() : b));
    };
    const drawZA = (step) => {
      for (const q of streams) {
        q.y += q.v * step;
        for (let k = 0; k < q.len; k++) {
          const y = q.y - k * 8;
          if (y < -4 || y > H + 4) continue;
          ctx.fillStyle = `rgba(120,236,160,${(k === 0 ? 1 : 1 - k / q.len) * q.a})`;
          ctx.fillRect(q.x, y, 2, 2);
        }
      }
      streams = streams.map(q => (q.y - q.len * 8 > H ? spawnStream(-10) : q));
      for (const q of squares) {
        q.y -= q.v * step;
        if (q.fill) { ctx.fillStyle = `rgba(184,194,191,${q.a * 0.8})`; ctx.fillRect(q.x, q.y, q.s, q.s); }
        else { ctx.strokeStyle = `rgba(227,236,232,${q.a})`; ctx.lineWidth = 1.5; ctx.strokeRect(q.x, q.y, q.s, q.s); }
      }
      squares = squares.map(q => (q.y < -40 ? spawnSquare() : q));
    };
    const tick = (now) => {
      frame = requestAnimationFrame(tick);
      const elapsed = now - last;
      if (elapsed < FRAME_MS) return;
      const step = Math.min(3, elapsed / (1000 / 60)); // keep the speed of the original 60 fps motion
      last = now;
      ctx.clearRect(0, 0, W, H);
      if (isZA()) { drawZA(step); return; }
      if (isPixel()) { drawPixel(step); return; }
      for (const b of list) {
        b.y -= b.v * step; b.p += 0.02 * step; const x = b.x + Math.sin(b.p) * 6;
        ctx.beginPath(); ctx.arc(x, b.y, b.r, 0, 6.283);
        ctx.strokeStyle = `rgba(255,255,255,${b.a})`; ctx.lineWidth = 1.2; ctx.stroke();
        ctx.beginPath(); ctx.arc(x - b.r * 0.35, b.y - b.r * 0.35, b.r * 0.25, 0, 6.283);
        ctx.fillStyle = `rgba(255,255,255,${b.a + 0.2})`; ctx.fill();
      }
      list = list.map(b => (b.y < -20 ? spawn() : b));
    };
    return {
      start() { if (!frame) { last = 0; frame = requestAnimationFrame(tick); } },
      stop() { if (frame) { cancelAnimationFrame(frame); frame = 0; } },
      clear() { ctx.clearRect(0, 0, W, H); },
    };
  }

  // ---------------------------------------------------------------- UI motion helpers
  const ok = () => A && !reduced;

  /**
   * Runs a CSS keyframe animation once. CSS instead of anime.js for entrances: `backwards` fill applies the start
   * state during the stagger delay (JS tweens only did so once the delay elapsed, so items flashed in at full
   * opacity first), it runs on the compositor, and afterwards the element's own CSS transform applies again.
   */
  function playCss(el, name, duration, delay, vars) {
    for (const [k, v] of Object.entries(vars)) el.style.setProperty(k, v);
    el.style.animation = `${name} ${duration}ms cubic-bezier(.16, 1, .3, 1) ${delay}ms backwards`;
    const done = e => {
      if (e.target !== el || e.animationName !== name) return; // child animations bubble up too
      el.style.animation = '';
      el.removeEventListener('animationend', done);
    };
    el.addEventListener('animationend', done);
  }

  function enter(targets, opts = {}) {
    if (!ok()) return;
    const list = typeof targets === 'string' ? document.querySelectorAll(targets) : targets;
    if (!list || (list.length === 0)) return;
    const stagger = opts.stagger ?? 45, start = opts.delay ?? 0;
    const vars = { '--enter-y': `${opts.y ?? 18}px`, '--enter-s': `${opts.scale ?? 0.98}` };
    Array.from(list).forEach((e, i) => playCss(e, 'tidal-enter', opts.duration ?? 620, start + i * stagger, vars));
  }

  /**
   * Page entrance: a short slide only. Fading the page root would make it a "backdrop root", so every frosted panel
   * inside would lose its blur during the fade and snap back at the end (the flicker); contents fade via enter().
   */
  function page(elm, dir = 1) {
    if (!ok() || !elm) return;
    playCss(elm, 'tidal-page', 340, 0, { '--enter-x': `${22 * dir}px` });
  }

  function pop(elm) {
    if (!ok() || !elm) return;
    A.animate(elm, { scale: [{ to: 1.12, duration: 140 }, { to: 1, duration: 420 }], ease: 'outElastic(1, .5)' });
  }

  function bounce(elm) {
    if (!ok() || !elm) return;
    A.animate(elm, { y: [{ to: -12, duration: 180, ease: 'outQuad' }, { to: 0, duration: 520, ease: 'outBounce' }] });
  }

  function title(elm) {
    if (!ok() || !elm || !A.splitText) return;
    try {
      const { chars } = A.splitText(elm, { chars: true });
      A.animate(chars, { opacity: { from: 0 }, y: { from: 10 }, duration: 480, delay: A.stagger(18), ease: 'outExpo' });
    } catch { /* ignore */ }
  }

  function slideTo(elm, props) {
    if (!elm) return;
    if (!ok()) { Object.assign(elm.style, Object.fromEntries(Object.entries(props).map(([k, v]) => [k, typeof v === 'number' ? v + 'px' : v]))); return; }
    A.animate(elm, { ...props, duration: 520, ease: A.createSpring ? A.createSpring({ stiffness: 170, damping: 18 }) : 'outExpo' });
  }

  function count(elm, to, from = 0) {
    if (!elm) return;
    if (!ok()) { elm.textContent = to.toLocaleString(); return; }
    const o = { v: from };
    A.animate(o, { v: to, duration: 900, ease: 'outExpo', onUpdate: () => { elm.textContent = Math.round(o.v).toLocaleString(); } });
  }

  function setTheme(t) {
    const next = t || 'light';
    if (next === theme) return;
    theme = next;
    bubbles?.clear();
    draw(mode, true);
  }

  function setReduced(v) { reduced = !!v; document.documentElement.classList.toggle('reduced', reduced); draw(mode, true); if (reduced) bubbles?.clear(); }

  return { mount, draw, enter, page, pop, bounce, title, slideTo, count, setReduced, setTheme, get reduced() { return reduced; } };
})();
