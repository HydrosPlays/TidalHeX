// TidalHeX app shell: store, routing, header/footer, global input.
(() => {
  const { createApp, reactive, ref, computed, nextTick, watch } = Vue;
  const C = window.TidalComponents;
  const P = window.TidalPages;
  const fx = window.TidalFx;

  const TABS = [
    { key: 'boxes', label: 'Boxes', icon: 'boxes' },
    { key: 'editor', label: 'Pokémon', icon: 'pokeball' },
    { key: 'encounters', label: 'Encounters', icon: 'sparkle' },
    { key: 'gifts', label: 'Mystery Gifts', icon: 'gift' },
    { key: 'tools', label: 'Save Tools', icon: 'tools' },
  ];
  const TITLES = { boxes: 'Boxes', editor: 'Pokémon', encounters: 'Encounters', gifts: 'Mystery Gifts', tools: 'Save Tools', settings: 'Settings' };

  // ------------------------------------------------------------------ store
  let toastId = 1;
  const store = reactive({
    booted: false,
    view: 'home',
    save: null,
    recent: [],
    settings: { reducedMotion: false, hideSecrets: false, encountersInGameOnly: true, giftsInGameOnly: true },
    version: '',
    editor: null,
    lists: {},
    toasts: [],
    modal: null,
    saveEpoch: 0, // bumps when a different save is loaded
    dialogs: [], // PKHeX messages/questions waiting for an answer (host is blocked until answered)
    ctx: null,
    dragOver: false,
    dropSlot: null, // data-slot of the box/party slot under a dragged file (Boxes page)
    hints: [],
    note: '',
    box: 0,
    placing: false,
    now: new Date(),
    spriteVersion: 1,

    /** Switches section; an open editor with unsaved changes (leaveGuard) is asked first. */
    async go(view) {
      if (this.view === view) return;
      if (this.leaveGuard && !(await this.leaveGuard())) return;
      this.leaveGuard = null;
      this.ctx = null;
      this.view = view;
    },
    leaveGuard: null, // () => Promise<boolean>: set by an open in-page editor
    toast(text, kind = 'info') {
      const id = toastId++;
      this.toasts.push({ id, text, kind });
      setTimeout(() => { const i = this.toasts.findIndex(t => t.id === id); if (i >= 0) this.toasts.splice(i, 1); }, 3600);
    },
    async call(method, args) {
      try { return await window.Tidal.call(method, args); }
      catch (e) { this.toast(e.message, 'error'); throw e; }
    },
    async list(name) {
      if (!this.lists[name]) this.lists[name] = await this.call('list.get', { name });
      return this.lists[name];
    },
    bumpSprites() { this.spriteVersion++; },
    async openFile() { await this.call('file.open').catch(() => null); },
    async openPath(path) { await this.call('file.openPath', { path }).catch(() => null); },
    async exportSave() { await this.call('file.exportSave').catch(() => null); },
    async settingsDialog() {
      const s = await this.call('app.settings').catch(() => null);
      if (!s) return;
      this.settings = s;
      fx.setReduced(s.reducedMotion);
    },
    /** Changes one of PKHeX's settings exposed on the pages; returns false if it failed. */
    async setOption(name, value) {
      const s = await this.call('app.setOption', { name, value }).catch(() => null);
      if (s) this.settings = s;
      return !!s;
    },
    async classic() { await this.call('app.classic').catch(() => null); },
    async loadEditor(state) { this.editor = state; this.go('editor'); },
    /** Answers the front dialog; PKHeX continues with that result. */
    answerDialog(result) {
      const d = this.dialogs.shift();
      if (!d) return;
      if (d.local) d.resolve(result);
      else window.Tidal.notify('dialog.answer', { id: d.id, result });
    },
    /** A question asked by the page itself (same look as PKHeX's); resolves with the button key, e.g. 'yes'. */
    confirm(text, { buttons = ['yes', 'no'], kind = 'prompt', title } = {}) {
      return new Promise(resolve => this.dialogs.push({ id: `local-${++localDialogs}`, local: true, kind, title, text, buttons, resolve }));
    },
    report(title, text) { this.modal = { title, text, copy: true }; },
    menu(e, items) { this.ctx = { x: Math.min(e.clientX, window.innerWidth - 230), y: Math.min(e.clientY, window.innerHeight - items.length * 40 - 20), items }; },
  });
  let localDialogs = 0;
  window.TidalStore = store;
  setInterval(() => { store.now = new Date(); }, 10_000);

  // Host events
  const T = window.Tidal;
  T.on('saveLoaded', s => { store.saveEpoch++; store.leaveGuard = null; store.save = s; store.lists = {}; store.box = 0; store.bumpSprites(); store.toast(`Loaded ${s.game}`, 'success'); });
  T.on('saveChanged', s => { store.save = s; store.bumpSprites(); });
  T.on('boxChanged', () => store.bumpSprites());
  T.on('editorLoaded', e => { store.editor = e; store.go('editor'); });
  T.on('toast', t => store.toast(t.text, t.kind));
  T.on('dialog', d => store.dialogs.push(d));

  // ------------------------------------------------------------------ root
  const Root = {
    setup() {
      const pageRef = ref(null);
      const pageEl = ref(null);
      const isHome = computed(() => store.view === 'home');
      const tabIndex = computed(() => TABS.findIndex(t => t.key === store.view));
      const title = computed(() => TITLES[store.view] ?? '');
      const clock = computed(() => store.now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false }));

      function cycle(dir) {
        const i = tabIndex.value < 0 ? 0 : tabIndex.value;
        store.go(TABS[(i + dir + TABS.length) % TABS.length].key);
      }
      function back() {
        if (store.modal) { store.modal = null; return; }
        if (store.ctx) { store.ctx = null; return; }
        if (pageRef.value?.back?.()) return; // page handled it (e.g. closing a drawer)
        store.go('home');
      }

      // scene + transitions
      watch(() => store.view, (v, old) => {
        fx.draw(v === 'home' ? 'home' : 'page');
        nextTick(() => {
          const root = document.querySelector(v === 'home' ? '.launcher' : '.page');
          if (root) fx.page(root, TABS.findIndex(t => t.key === v) >= TABS.findIndex(t => t.key === old) ? 1 : -1);
          if (v !== 'home') fx.title(document.querySelector('.header .band h1'));
        });
      });

      // keyboard
      function onKey(e) {
        if (store.dialogs.length) { // a PKHeX question is open: only its buttons respond
          const d = store.dialogs[0];
          if (e.key === 'Enter') { e.preventDefault(); store.answerDialog(d.buttons[0]); }
          else if (e.key === 'Escape') { e.preventDefault(); store.answerDialog(d.buttons.includes('cancel') ? 'cancel' : d.buttons.includes('no') ? 'no' : d.buttons.at(-1)); }
          return;
        }
        const typing = /INPUT|TEXTAREA|SELECT/.test(document.activeElement?.tagName ?? '');
        if (e.ctrlKey && e.key.toLowerCase() === 'o') { e.preventDefault(); store.openFile(); return; }
        if (e.ctrlKey && e.key.toLowerCase() === 'e') { e.preventDefault(); store.exportSave(); return; }
        if (e.ctrlKey && e.key === 'PageDown') { e.preventDefault(); cycle(1); return; }
        if (e.ctrlKey && e.key === 'PageUp') { e.preventDefault(); cycle(-1); return; }
        if (e.ctrlKey && /^[1-5]$/.test(e.key)) { e.preventDefault(); store.go(TABS[+e.key - 1].key); return; }
        if (e.ctrlKey && e.key === 'h') { e.preventDefault(); store.go('home'); return; }
        if (e.key === 'Escape') { if (!typing || e.defaultPrevented) { e.preventDefault(); back(); } else document.activeElement.blur(); return; }
        if (typing) return;
        if (e.key === 'Backspace') { e.preventDefault(); back(); return; }
        if (e.key === '[' || e.key === 'q') { cycle(-1); return; }
        if (e.key === ']' || e.key === 'e') { cycle(1); return; }
        pageRef.value?.onKey?.(e);
      }
      window.addEventListener('keydown', onKey);
      window.addEventListener('mousedown', () => { store.ctx = null; });
      // A clicked button shouldn't keep focus: keyboard shortcuts (L/R, arrows) would then paint the focus ring on it.
      // (detail > 0 = mouse click; keyboard-activated buttons keep focus.)
      window.addEventListener('click', e => { if (e.detail > 0) e.target.closest?.('button')?.blur(); });
      window.addEventListener('contextmenu', e => e.preventDefault());

      // drag & drop files anywhere; on the Boxes page, a Pokémon file dropped onto a slot is placed in that slot
      let dragDepth = 0;
      const slotUnder = e => (store.view === 'boxes' ? e.target?.closest?.('[data-slot]')?.dataset.slot ?? null : null);
      window.addEventListener('dragenter', e => { if (e.dataTransfer?.types?.includes('Files')) { dragDepth++; store.dragOver = true; } });
      window.addEventListener('dragleave', () => { dragDepth = Math.max(0, dragDepth - 1); if (!dragDepth) { store.dragOver = false; store.dropSlot = null; } });
      window.addEventListener('dragover', e => {
        if (!e.dataTransfer?.types?.includes('Files')) return;
        e.preventDefault();
        store.dropSlot = slotUnder(e);
      });
      window.addEventListener('drop', e => {
        if (!e.dataTransfer?.files?.length) return;
        e.preventDefault();
        const slot = slotUnder(e);
        dragDepth = 0; store.dragOver = false; store.dropSlot = null;
        const sent = slot
          ? T.dropFiles(e.dataTransfer.files, 'box.dropFile', { slot: JSON.parse(slot) })
          : T.dropFiles(e.dataTransfer.files);
        sent.catch(err => store.toast(err.message, 'error'));
      });

      return { store, TABS, isHome, title, clock, cycle, back, pageRef, pageEl, tabIndex };
    },
    template: `
      <component v-if="store.booted && isHome" :is="'page-home'" ref="pageRef" :store="store"></component>

      <div v-else-if="store.booted" class="app">
        <header class="header">
          <div class="band">
            <h1 :key="title">{{ title }}</h1>
            <button class="shoulder" title="Previous section ( [ )" @click="cycle(-1)">L</button>
            <nav class="tabs">
              <button v-for="t in TABS" :key="t.key" class="tab" :class="{ on: store.view === t.key }" @click="store.go(t.key)">
                <t-icon :name="t.icon"></t-icon>{{ t.label }}
              </button>
            </nav>
            <button class="shoulder" title="Next section ( ] )" @click="cycle(1)">R</button>
          </div>
          <div class="status">
            <div class="status-icon" v-if="store.save?.edited" title="Unsaved changes"><t-icon name="save"></t-icon></div>
            <div class="clock">{{ clock }}</div>
          </div>
        </header>

        <main class="page-host">
          <component :is="'page-' + store.view" ref="pageRef" :store="store" :key="store.view"></component>
        </main>

        <footer class="footer">
          <div class="left"><button class="back-btn" @click="back"><span class="glyph round">B</span>Back</button><span class="note">{{ store.note }}</span></div>
          <button class="home-btn" title="Home (Ctrl+H)" @click="store.go('home')"><t-icon name="home"></t-icon></button>
          <div class="right">
            <button v-for="h in store.hints" :key="h.label" class="hint" @click="h.run()"><span class="glyph" :class="{ round: h.glyph.length === 1 }">{{ h.glyph }}</span>{{ h.label }}</button>
          </div>
        </footer>
      </div>

      <div v-if="store.dragOver && store.view === 'boxes'" class="dropzone compact"><div class="box"><t-icon name="drop"></t-icon><div><h2>{{ store.dropSlot ? 'Drop to place it in this slot' : 'Drop onto a slot to place it' }}</h2><div class="muted">or anywhere else to open the file</div></div></div></div>
      <div v-else-if="store.dragOver" class="dropzone"><div class="box"><t-icon name="drop"></t-icon><h2>Drop to open</h2><div class="muted">Saves, Pokémon files, gifts and box dumps</div></div></div>
      <t-modal :store="store"></t-modal>
      <t-dialog :store="store"></t-dialog>
      <t-ctx :store="store"></t-ctx>
      <t-toasts :store="store"></t-toasts>
    `,
  };

  // ------------------------------------------------------------------ boot
  async function boot() {
    fx.mount(document.getElementById('backdrop'));
    fx.draw('boot');
    const app = createApp(Root);
    app.component('t-icon', C.TIcon);
    app.component('t-combo', C.TCombo);
    app.component('t-switch', C.TSwitch);
    app.component('t-games', C.TGames);
    app.component('t-tri', C.TTri);
    app.component('t-number', C.TNumber);
    app.component('t-text', C.TText);
    app.component('t-fields', C.TFields);
    app.component('t-modal', C.TModal);
    app.component('t-dialog', C.TDialog);
    app.component('t-tool', C.TTool);
    app.component('t-toasts', C.TToasts);
    app.component('t-ctx', C.TCtx);
    for (const [name, def] of Object.entries(P)) app.component('page-' + name, def);
    for (const [name, def] of Object.entries(window.TidalTools ?? {})) app.component('tool-' + name, def);
    app.mount('#app');

    const bootEl = document.getElementById('boot');
    const init = window.Tidal.call('app.init').catch(e => ({ error: e.message }));
    const minimum = new Promise(r => setTimeout(r, fx.reduced ? 0 : 1300));
    const [data] = await Promise.all([init, minimum]);
    if (data.error) store.toast(data.error, 'error');
    else {
      store.save = data.save; store.recent = data.recent ?? []; store.settings = data.settings ?? store.settings; store.version = data.version;
      fx.setReduced(store.settings.reducedMotion);
    }
    // boot screen out
    if (window.anime && !fx.reduced) {
      await window.anime.animate(bootEl, { opacity: { to: 0 }, scale: { to: 1.04 }, duration: 520, ease: 'inQuad' });
    }
    bootDots?.pause(); // endless loop; would keep ticking on the removed element
    bootEl.remove();
    store.booted = true;
    fx.draw('home');
    nextTick(() => fx.page(document.querySelector('.launcher'), 1));
    window.chrome?.webview?.postMessage({ id: 0, method: 'app.ready', args: null });
  }

  let bootDots = null;
  document.addEventListener('DOMContentLoaded', () => {
    // Boot animation: logo rises and glows, title letters stagger in, dots pulse.
    const A = window.anime;
    if (A && !fx.reduced) {
      A.animate('#boot img', { scale: { from: 0.6 }, opacity: { from: 0 }, rotate: { from: -30 }, duration: 1100, ease: 'outElastic(1, .6)' });
      fx.title(document.querySelector('#boot h1'));
      bootDots = A.animate('#boot .dots i', { backgroundColor: { from: 'rgba(255,255,255,0)', to: '#ffffff' }, duration: 500, delay: A.stagger(220), loop: true, alternate: true, ease: 'inOutSine' });
    }
    boot();
  });
})();
