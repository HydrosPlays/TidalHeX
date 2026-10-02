// Home: console-style launcher (party avatars, title band, app tiles, dock).
window.TidalPages.home = {
  props: { store: Object },
  // sel / dockSel: the tile or dock button last under the mouse or picked with the arrow keys (-1: none yet). Like the
  // other screens, nothing is highlighted until the mouse is over it; kbd = the arrow keys moved, so draw the highlight.
  data: () => ({ zone: 'tiles', sel: -1, dockSel: -1, kbd: false, canLeft: false, canRight: false, paging: false }),
  computed: {
    save() { return this.store.save; },
    party() { return (this.save?.party ?? []).filter(p => !p.empty); },
    clock() { return this.store.now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false }); },
    tiles() {
      const s = this.save, e = this.store.editor;
      const list = [
        { key: 'editor', title: 'Pokémon', sub: e && !e.empty ? `${e.nickname || e.speciesName} · Lv. ${e.level}` : 'Create or edit a Pokémon', bg: 'linear-gradient(150deg,#ff9a5a,#ff5d7a 55%,#c2388f)', sprite: e && !e.empty ? e.sprite : null, icon: 'pokeball' },
        { key: 'boxes', title: 'Boxes', sub: s?.hasBox ? `${s.pokemonCount.toLocaleString()} Pokémon stored` : 'No boxes in this save', bg: 'linear-gradient(150deg,#4fe3ff,#1a8fe6 55%,#1956c9)', party: this.party.slice(0, 6), icon: 'boxes' },
        { key: 'encounters', title: 'Encounters', sub: 'Where to find any Pokémon', bg: 'linear-gradient(150deg,#7cf29a,#23c37a 55%,#138a6b)', icon: 'sparkle' },
        { key: 'gifts', title: 'Mystery Gifts', sub: 'Every event distribution', bg: 'linear-gradient(150deg,#ffd96b,#ffab3b 55%,#f06b2d)', icon: 'gift' },
        { key: 'tools', title: 'Save Tools', sub: 'Trainer, items, Pokédex & more', bg: 'linear-gradient(150deg,#b99bff,#7d6bf2 55%,#4a47c7)', icon: 'tools' },
        { key: 'open', title: 'Open File', sub: 'Saves, Pokémon, gifts, box dumps', bg: 'linear-gradient(150deg,#e9f6ff,#9fc9ee 55%,#5f93c9)', icon: 'open' },
      ];
      for (const r of (this.store.recent ?? []).filter(r => r.exists && r.path !== s?.filePath).slice(0, 3))
        list.push({ key: 'recent', path: r.path, title: r.name, sub: `Recent · ${r.folder}`, bg: 'linear-gradient(150deg,#2a4f8f,#15306a 60%,#0b1f4c)', icon: 'save' });
      return list;
    },
    current() { return this.zone === 'tiles' ? this.tiles[this.sel] : this.dock[this.dockSel]; },
    dock() {
      const st = this.store;
      return [
        { icon: 'open', c: 'var(--dock-red)', tip: 'Open file', run: () => st.openFile() },
        { icon: 'library', c: 'var(--cyan)', tip: 'Save Manager', run: () => st.go('saves') },
        { icon: 'export', c: 'var(--dock-orange)', tip: 'Export save', run: () => st.exportSave() },
        { icon: 'boxes', c: 'var(--dock-blue)', tip: 'Boxes', run: () => st.go('boxes') },
        { icon: 'shield', c: 'var(--dock-green)', tip: 'Check every Pokémon', run: () => st.call('tools.open', { id: 'VerifySaveEntities' }).catch(() => {}) },
        { icon: 'settings', c: 'var(--dock-grey)', tip: 'Settings', run: () => st.go('settings') },
        { icon: 'power', c: 'var(--dock-grey)', tip: 'Classic PKHeX', run: () => st.classic() },
      ];
    },
    /** Before anything is picked, the band names the loaded save. */
    bandTitle() {
      if (this.current) return this.zone === 'tiles' ? this.current.title : this.current.tip;
      return this.hasSave ? this.save.game : 'TidalHeX';
    },
    bandSub() {
      if (this.current) return this.zone === 'tiles' ? this.current.sub : '';
      return this.hasSave ? `${this.save.ot} · ${this.save.pokemonCount.toLocaleString()} Pokémon` : 'Open a save to get started';
    },
    hasSave() { return !!(this.save?.loaded && !this.save.blank); },
  },
  watch: {
    sel() {
      if (this.paging || this.sel < 0) return; // page() scrolls by itself
      this.$nextTick(() => this.$refs.tiles?.children[this.sel]?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'smooth' }));
    },
    'tiles.length'() { this.$nextTick(this.updateArrows); },
  },
  mounted() {
    this.store.hints = [];
    if (!this.store.editor) this.store.call('editor.get').then(e => { if (!this.store.editor) this.store.editor = e; }).catch(() => {});
    window.TidalFx.enter(this.$el.querySelectorAll('.avatar'), { stagger: 50, y: -16 });
    window.TidalFx.enter(this.$el.querySelectorAll('.tile'), { stagger: 60, y: 30, delay: 120 });
    window.TidalFx.enter(this.$el.querySelectorAll('.dock-btn'), { stagger: 40, y: 20, delay: 260 });
    this.$nextTick(this.updateArrows);
    window.addEventListener('resize', this.updateArrows);
  },
  beforeUnmount() { window.removeEventListener('resize', this.updateArrows); },
  methods: {
    activate(t) {
      if (!t) return;
      if (t.key === 'open') this.store.openFile();
      else if (t.key === 'recent') this.store.openPath(t.path);
      else this.store.go(t.key);
    },
    hover(i) { this.kbd = false; this.zone = 'tiles'; this.sel = i; },
    cleanUrl(src) { return window.Tidal.url.clean(src); },
    /** Edge arrows show only when there are more tiles that way. */
    updateArrows() {
      const el = this.$refs.tiles;
      if (!el) return;
      this.canLeft = el.scrollLeft > 4;
      this.canRight = el.scrollLeft + el.clientWidth < el.scrollWidth - 4;
    },
    /** Scrolls a page of tiles and selects the first one that comes into view. */
    page(dir) {
      const el = this.$refs.tiles;
      const tiles = el ? [...el.children] : [];
      if (!tiles.length) return;
      const view = { left: el.scrollLeft, right: el.scrollLeft + el.clientWidth };
      const step = tiles.length > 1 ? tiles[1].offsetLeft - tiles[0].offsetLeft : tiles[0].offsetWidth;
      const next = dir > 0
        ? tiles.findIndex(t => t.offsetLeft + t.offsetWidth > view.right + 1)
        : tiles.findLastIndex(t => t.offsetLeft < view.left - 1);
      this.paging = true;
      this.zone = 'tiles';
      if (next >= 0) this.sel = next;
      el.scrollBy({ left: dir * Math.max(step, el.clientWidth - step), behavior: 'smooth' });
      this.$nextTick(() => { this.paging = false; });
    },
    /** A mouse wheel scrolls the row sideways. */
    wheel(e) {
      const el = this.$refs.tiles;
      if (!el || Math.abs(e.deltaX) > Math.abs(e.deltaY) || el.scrollWidth <= el.clientWidth) return;
      e.preventDefault();
      el.scrollBy({ left: e.deltaY, behavior: 'auto' });
    },
    hoverDock(i) { this.kbd = false; this.zone = 'dock'; this.dockSel = i; },
    avatar(p) { this.store.call('box.view', { slot: { box: p.box, slot: p.slot } }).catch(() => {}); },
    onKey(e) {
      const n = this.zone === 'tiles' ? this.tiles.length : this.dock.length;
      const key = this.zone === 'tiles' ? 'sel' : 'dockSel';
      const arrows = ['ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'];
      if (arrows.includes(e.key)) {
        e.preventDefault();
        const first = !this.kbd || this[key] < 0; // the first arrow press shows the highlight where it is
        this.kbd = true;
        if (this[key] < 0) this[key] = 0;
        if (first) return;
      }
      if (e.key === 'ArrowRight') this[key] = Math.min(n - 1, this[key] + 1);
      else if (e.key === 'ArrowLeft') this[key] = Math.max(0, this[key] - 1);
      else if (e.key === 'ArrowDown' && this.zone === 'tiles') { this.zone = 'dock'; if (this.dockSel < 0) this.dockSel = 0; }
      else if (e.key === 'ArrowUp' && this.zone === 'dock') { this.zone = 'tiles'; if (this.sel < 0) this.sel = 0; }
      else if (e.key === 'Enter' || e.key === ' ' || e.key.toLowerCase() === 'a') {
        e.preventDefault();
        if (this.zone === 'tiles') this.activate(this.current); else this.current?.run();
      }
      else if (e.key === '+' || e.key === '=') this.store.go('settings');
    },
  },
  template: `
    <div class="launcher">
      <div class="top">
        <div class="avatars">
          <div class="avatar trainer" :title="save?.ot">{{ (save?.ot || '?').slice(0, 1).toUpperCase() }}</div>
          <div v-for="p in party" :key="p.slot" class="avatar" :title="p.nickname + ' · Lv. ' + p.level" @click="avatar(p)">
            <img class="sprite" :src="cleanUrl(p.sprite)" alt="">
            <span v-if="p.shiny" class="flag">★</span>
          </div>
          <div v-for="i in Math.max(0, 6 - party.length)" :key="'e' + i" class="avatar empty"></div>
        </div>
        <div class="status">
          <div class="status-icon" v-if="save?.edited" title="Unsaved changes"><t-icon name="save"></t-icon></div>
          <t-game-badge :save="save"></t-game-badge>
          <div class="clock">{{ clock }}</div>
        </div>
      </div>

      <div class="title-band">
        <div class="band"><h1>{{ bandTitle }}</h1><span class="sub">{{ bandSub }}</span></div>
      </div>

      <div class="tiles-wrap">
      <button class="tile-arrow left" :class="{ show: canLeft }" title="Previous" aria-label="Scroll left" @click="page(-1)"><t-icon name="back"></t-icon></button>
      <div class="tiles" ref="tiles" @scroll.passive="updateArrows" @wheel="wheel">
        <button v-for="(t, i) in tiles" :key="t.key + (t.path || '')" class="tile" :class="{ sel: kbd && zone === 'tiles' && sel === i }"
                :style="{ '--tile-bg': t.bg }" @mouseenter="hover(i)" @click="activate(t)">
          <div v-if="t.sprite" class="art"><img class="sprite" :src="cleanUrl(t.sprite)" alt=""></div>
          <div v-else-if="t.party && t.party.length" class="art sprites"><img v-for="p in t.party" :key="p.slot" class="sprite" :src="p.sprite" alt=""></div>
          <div v-else class="art"><t-icon :name="t.icon"></t-icon></div>
          <h3>{{ t.title }}</h3><p>{{ t.sub }}</p>
        </button>
      </div>
      <button class="tile-arrow right" :class="{ show: canRight }" title="More" aria-label="Scroll right" @click="page(1)"><t-icon name="next"></t-icon></button>
      </div>

      <div class="dock-wrap">
        <div class="dock">
          <button v-for="(d, i) in dock" :key="d.tip" class="dock-btn" :class="{ sel: kbd && zone === 'dock' && dockSel === i }" :style="{ '--c': d.c }"
                  @mouseenter="hoverDock(i)" @click="d.run()"><t-icon :name="d.icon"></t-icon><span class="tip">{{ d.tip }}</span></button>
        </div>
      </div>

      <footer class="footer">
        <div class="left"><span class="joycon"></span><span class="strip"></span></div>
        <span></span>
        <div class="right">
          <button class="hint" @click="store.go('settings')"><span class="glyph round">+</span>Options</button>
          <button class="hint" @click="zone === 'tiles' ? activate(current) : current?.run()"><span class="glyph round">A</span>Start</button>
        </div>
      </footer>
    </div>`,
};
