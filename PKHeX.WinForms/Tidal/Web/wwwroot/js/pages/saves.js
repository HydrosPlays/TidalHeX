// Save Manager: the saves (raw files or .zip backups) in the "saves" folder next to the exe, one group per sub-folder,
// oldest games first. Double-click (or A) opens a save and shows its boxes.
window.TidalPages.saves = {
  props: { store: Object },
  // sel: the save last under the mouse (or picked with the arrow keys); kbd: the arrow keys moved it, so it's drawn highlighted.
  data: () => ({ lib: null, loading: true, query: '', gen: 0, sel: null, kbd: false, busy: null, refreshing: false }),
  computed: {
    all() { return (this.lib?.groups ?? []).flatMap(g => g.saves); },
    gens() { return [...new Set(this.all.map(s => s.generation))].sort((a, b) => a - b); },
    groups() {
      const q = this.query.trim().toLowerCase();
      return (this.lib?.groups ?? [])
        .map(g => ({ ...g, saves: g.saves.filter(s => (!this.gen || s.generation === this.gen) && (!q || this.text(s, g).includes(q))) }))
        .filter(g => g.saves.length);
    },
    shown() { return this.groups.flatMap(g => g.saves); },
    selected() { return this.shown.find(s => s.id === this.sel) ?? null; },
  },
  watch: {
    // Filtering can hide the selected save.
    shown(list) { if (!list.some(s => s.id === this.sel)) this.sel = null; },
  },
  async mounted() {
    this.store.note = 'Double-click a save to open it';
    this.store.hints = [
      { glyph: 'A', label: 'Open', run: () => this.open(this.selected) },
      { glyph: 'X', label: 'Open folder', run: () => this.openFolder() },
      { glyph: 'Y', label: 'Refresh', run: () => this.refresh() },
    ];
    // The startup scan's list shows at once; a fresh scan then picks up files added since.
    await this.load(false);
    this.refresh();
    window.addEventListener('focus', this.refresh);
  },
  beforeUnmount() { window.removeEventListener('focus', this.refresh); },
  methods: {
    async load(refresh) {
      const lib = await this.store.call('saves.list', { refresh }).catch(() => null);
      const first = !this.lib;
      if (lib) this.lib = lib;
      this.loading = false;
      if (!first) return;
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.save-card'), { stagger: 18, y: 14 }));
    },
    async refresh() {
      if (this.refreshing) return;
      this.refreshing = true;
      try { await this.load(true); }
      finally { this.refreshing = false; }
    },
    async openFolder() { await this.store.call('saves.openFolder').catch(() => null); },
    /** Loads the save (PKHeX asks first if the current one has unsaved changes), then shows its boxes. */
    async open(s) {
      if (!s || this.busy) return;
      if (s.loaded && this.store.save?.loaded) { this.store.go('boxes'); return; } // already open: keep any edits
      this.busy = s.id;
      try {
        const summary = await this.store.call('saves.open', { id: s.id }).catch(() => null);
        if (summary) this.store.go('boxes');
        else this.refresh(); // cancelled, or the file changed: show the folder as it is now
      } finally {
        this.busy = null;
      }
    },
    text(s, g) { return `${s.game} ${s.ot} ${s.tid} ${s.language} ${s.languageName} ${s.folder} ${s.fileName} ${s.entry ?? ''} ${g.name} ${g.key} gen ${s.generation}`.toLowerCase(); },
    file(s) { return [s.folder, s.fileName, s.entry].filter(Boolean).join(' › '); },
    date(s) { return s.modified ? new Date(s.modified).toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' }) : ''; },
    /** "2024-03-02" as a local date (new Date() would read it as UTC midnight and can show the day before). */
    day(iso) { const [y, m, d] = iso.split('-').map(Number); return new Date(y, m - 1, d).toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' }); },
    mon(p) { return `${window.Tidal.url.species(p.species, p.form, p.shiny, p.gender)}&crop=1${p.egg ? '&egg=1' : ''}`; },
    // Like the Save Tools list, a card only lights up under the mouse. A / Enter (or the footer's Open) opens the last
    // save the mouse was over.
    hover(s) { this.kbd = false; this.sel = s.id; },
    /** Arrows move a highlight through the cards (up/down by a row of the grid), Enter / A opens, F5 / Y refreshes. */
    onKey(e) {
      const list = this.shown;
      if (!list.length) return;
      const at = list.findIndex(s => s.id === this.sel);
      const cols = this.columns();
      const moves = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -cols, ArrowDown: cols };
      if (e.key in moves) {
        e.preventDefault();
        const i = at < 0 ? 0 : Math.min(list.length - 1, Math.max(0, at + moves[e.key])); // first press: the first card
        this.kbd = true;
        this.sel = list[i].id;
        this.$nextTick(() => this.$el.querySelector('.save-card.sel')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }));
      } else if (e.key === 'Enter' || e.key === 'a') { e.preventDefault(); this.open(list[at]); }
      else if (e.key === 'F5' || e.key === 'y') { e.preventDefault(); this.refresh(); }
      else if (e.key === 'x') { e.preventDefault(); this.openFolder(); }
    },
    columns() {
      const grid = this.$el.querySelector('.save-grid');
      return grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : 1;
    },
  },
  template: `
    <div class="page">
      <div class="saves screen">
        <div class="saves-bar">
          <input class="input" v-model="query" placeholder="Find a save…">
          <div class="gen-chips" v-if="gens.length > 1">
            <button class="chip" :class="{ on: !gen }" @click="gen = 0">All</button>
            <button v-for="g in gens" :key="g" class="chip" :class="{ on: gen === g }" @click="gen = gen === g ? 0 : g">Gen {{ g }}</button>
          </div>
          <span class="grow"></span>
          <span class="muted">{{ all.length }} {{ all.length === 1 ? 'save' : 'saves' }}</span>
          <button class="btn small" :class="{ spinning: refreshing }" title="Look for new saves (F5)" @click="refresh"><t-icon name="refresh"></t-icon>Refresh</button>
          <button class="btn small" title="Open the saves folder in Explorer" @click="openFolder"><t-icon name="open"></t-icon>Open folder</button>
        </div>

        <div v-if="loading" class="empty-state"><div><t-icon name="library"></t-icon><h3>Looking for saves…</h3></div></div>

        <div v-else-if="!all.length" class="empty-state saves-empty">
          <div>
            <t-icon name="library"></t-icon>
            <h3>No saves yet</h3>
            <p class="muted">Put save files or <b>.zip</b> backups in the <b>saves</b> folder next to TidalHeX.<br>Folders inside it (like <b>switch</b> or <b>gba</b>) become groups here.</p>
            <p class="saves-path">{{ lib?.folder }}</p>
            <button class="btn primary" @click="openFolder"><t-icon name="open"></t-icon>Open the saves folder</button>
          </div>
        </div>

        <div v-else-if="!groups.length" class="empty-state"><div><t-icon name="search"></t-icon><h3>No saves match</h3></div></div>

        <section v-for="g in groups" :key="g.key" class="save-group">
          <h4 class="card-title">{{ g.name }} <span class="muted">· {{ g.saves.length }}</span><span class="saves-folder">saves/{{ g.key }}</span></h4>
          <div class="save-grid">
            <button v-for="s in g.saves" :key="s.id" class="save-card" :class="{ sel: kbd && s.id === sel, loaded: s.loaded, busy: busy === s.id }"
                    :title="file(s)" @mouseenter="hover(s)" @dblclick="open(s)">
              <span class="save-art" :class="{ pair: s.icons.length > 1 }">
                <img v-for="(src, n) in s.icons" :key="src" :src="src" :class="'n' + n" alt="" draggable="false">
                <span v-if="!s.icons.length" class="save-art-blank"><t-icon name="save"></t-icon><small>{{ s.game }}</small></span>
              </span>
              <span class="save-info">
                <span class="save-title">
                  <b>{{ s.game }}</b>
                  <span class="save-tags">
                    <span v-if="s.loaded" class="save-tag open">Open now</span>
                    <span v-if="s.language" class="save-tag lang" :title="s.languageName">{{ s.language }}</span>
                    <span v-if="s.entry" class="save-tag" title="Inside a .zip backup">ZIP</span>
                    <span class="save-tag">Gen {{ s.generation }}</span>
                  </span>
                </span>
                <span v-if="s.note" class="sub">{{ s.note }}</span>
                <template v-else>
                  <span class="save-trainer">
                    <span v-if="s.gender >= 0" class="g" :class="s.gender ? 'f' : 'm'" :title="s.gender ? 'Female trainer' : 'Male trainer'">{{ s.gender ? '♀' : '♂' }}</span>
                    <b>{{ s.ot || 'No name' }}</b>
                    <span v-if="s.tid">TID {{ s.tid }}</span>
                    <span v-if="s.sid">SID {{ s.sid }}</span>
                  </span>
                  <span class="save-stats">
                    <span v-if="s.playTime" title="Play time"><t-icon name="clock"></t-icon>{{ s.playTime }}</span>
                    <span v-if="s.started" title="Adventure started"><t-icon name="calendar"></t-icon>{{ day(s.started) }}</span>
                    <span v-if="s.money" title="Money"><i class="pd">₽</i>{{ s.money.toLocaleString() }}</span>
                    <span v-if="s.dexCaught >= 0" title="Pokédex: caught"><t-icon name="pokeball"></t-icon>{{ s.dexCaught.toLocaleString() }} caught</span>
                  </span>
                </template>
                <span class="save-party" v-if="s.party.length"><img v-for="(p, i) in s.party" :key="i" :src="mon(p)" alt="" loading="lazy"></span>
                <small class="save-file">{{ file(s) }}<template v-if="date(s)"> · saved {{ date(s) }}</template></small>
              </span>
              <span v-if="busy === s.id" class="save-busy">Opening…</span>
            </button>
          </div>
        </section>

        <p v-if="!loading && lib?.skipped?.length" class="muted saves-skipped" :title="lib.skipped.join('\\n')">
          {{ lib.skipped.length }} {{ lib.skipped.length === 1 ? "file in the folder isn't a save" : "files in the folder aren't saves" }} TidalHeX recognizes.
        </p>
      </div>
    </div>`,
};
