// Encounter database: find where/how any Pokémon can be obtained.
window.TidalPages.encounters = {
  props: { store: Object },
  data: () => ({
    lists: {}, species: 0, version: 0, moves: [0, 0, 0, 0], shiny: null, egg: null,
    types: { slot: true, static: true, trade: true, egg: true, mystery: true },
    results: [], sel: null, searching: false, searched: false, query: '', sort: 'default',
  }),
  computed: {
    speciesOpts() { return (this.lists.species ?? []).map(o => (o.v === 0 ? { v: 0, t: 'Any Pokémon' } : { ...o, img: window.Tidal.url.species(o.v) })); },
    gameOpts() { return [{ v: 0, t: 'Any game' }, ...(this.lists.games ?? [])]; },
    moveOpts() { return (this.lists.moves ?? []).map(o => (o.v === 0 ? { v: 0, t: 'Any move' } : o)); },
    canSearch() { return Object.values(this.types).some(Boolean) && (this.species || this.moves.some(Boolean)); },
    shown() {
      const q = this.query.trim().toLowerCase();
      let r = this.results;
      if (q) r = r.filter(x => `${x.name} ${x.location} ${x.versionName} ${x.typeLabel} ${(x.games ?? []).map(g => g.name).join(' ')} ${x.source ?? ''}`.toLowerCase().includes(q));
      const by = { species: (a, b) => a.species - b.species, level: (a, b) => a.levelMin - b.levelMin, game: (a, b) => a.version - b.version, type: (a, b) => a.typeLabel.localeCompare(b.typeLabel) }[this.sort];
      return by ? [...r].sort(by) : r;
    },
  },
  async mounted() {
    this.store.note = 'Search by Pokémon, game or moves';
    this.store.hints = [
      { glyph: 'Y', label: 'Search', run: () => this.search() },
      { glyph: 'A', label: 'Load into editor', run: () => this.load() },
    ];
    const names = ['species', 'games', 'moves'];
    const loaded = await Promise.all(names.map(n => this.store.list(n).catch(() => [])));
    names.forEach((n, i) => { this.lists[n] = loaded[i]; });
    const e = this.store.editor;
    if (e && !e.empty && e.species) { this.species = e.species; this.search(); }
    window.TidalFx.enter(this.$el.querySelectorAll('.filters > *'), { stagger: 30, y: 10 });
  },
  methods: {
    sprite(r) { return r.sprite ?? window.Tidal.url.enc(r.token); },
    badgeClass(r) { return { slot: 'good', static: 'warn', trade: '', egg: 'shiny', mystery: 'bad', gift: 'bad', raid: 'bad' }[r.type] ?? ''; },
    async search() {
      if (!this.canSearch || this.searching) return;
      this.searching = true; this.sel = null;
      try {
        this.results = await this.store.call('enc.search', {
          species: this.species, version: this.version, moves: this.moves.filter(Boolean), shiny: this.shiny, egg: this.egg,
          types: Object.entries(this.types).filter(([, v]) => v).map(([k]) => k),
        });
        this.searched = true;
        if (this.results.length) this.sel = this.shown[0];
        this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.rcard'), { stagger: 14, y: 16, scale: 0.94 }));
      } catch { /* toast shown */ } finally { this.searching = false; }
    },
    reset() { this.species = 0; this.version = 0; this.moves = [0, 0, 0, 0]; this.shiny = null; this.egg = null; for (const k in this.types) this.types[k] = true; this.results = []; this.searched = false; this.sel = null; },
    /** PKHeX's "filter unavailable species": off also lists encounters of Pokémon this game can't hold. */
    async setInGameOnly(v) {
      if (await this.store.setOption('encountersInGameOnly', v) && this.searched) this.search();
    },
    toggleType(k, e) {
      if (e.shiftKey) { for (const t in this.types) this.types[t] = t === k; return; }
      this.types[k] = !this.types[k];
    },
    async load(r = this.sel) {
      if (!r) return;
      const state = await this.store.call('enc.load', { token: r.token }).catch(() => null);
      if (state) { this.store.loadEditor(state); this.store.toast(`Loaded ${r.name} from ${r.versionName}`, 'success'); }
    },
    onKey(e) {
      if (e.key === 'Enter' && this.sel) this.load();
      else if (e.key.toLowerCase() === 'y') this.search();
      else if (e.key.startsWith('Arrow') && this.shown.length) {
        const i = Math.max(0, this.shown.indexOf(this.sel));
        const cols = Math.max(1, Math.floor((this.$el.querySelector('.card-grid')?.clientWidth ?? 600) / 162));
        const d = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: cols, ArrowUp: -cols }[e.key];
        this.sel = this.shown[Math.min(this.shown.length - 1, Math.max(0, i + d))];
        e.preventDefault();
        this.$nextTick(() => this.$el.querySelector('.rcard.sel')?.scrollIntoView({ block: 'nearest' }));
      }
    },
  },
  template: `
    <div class="page">
      <div class="db">
        <aside class="filters screen">
          <div class="field"><label>Pokémon</label><t-combo v-model="species" :options="speciesOpts" placeholder="Any Pokémon"></t-combo></div>
          <div class="field"><label>Game</label><t-combo v-model="version" :options="gameOpts"></t-combo></div>
          <div class="field"><label>Knows moves</label>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px"><t-combo v-for="i in 4" :key="i" v-model="moves[i - 1]" :options="moveOpts"></t-combo></div></div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div class="field"><label>Shiny</label><t-tri v-model="shiny"></t-tri></div>
            <div class="field"><label>Egg</label><t-tri v-model="egg"></t-tri></div>
          </div>
          <div class="field"><label>Encounter types</label>
            <div style="display:flex;flex-wrap:wrap;gap:6px">
              <button v-for="(label, k) in { slot: 'Wild', static: 'Static', trade: 'Trades', egg: 'Eggs', mystery: 'Mystery Gifts' }" :key="k" class="chip" :class="{ on: types[k] }" @click="toggleType(k, $event)">{{ label }}</button>
            </div>
            <small class="muted">Shift+click to show only one type</small>
          </div>
          <div class="field"><label>Availability</label>
            <t-switch :model-value="store.settings.encountersInGameOnly" @update:model-value="setInGameOnly" label="Only Pokémon in this game"></t-switch>
          </div>
          <div style="display:grid;grid-template-columns:1fr 1.4fr;gap:8px;margin-top:auto">
            <button class="btn" @click="reset"><t-icon name="refresh"></t-icon>Reset</button>
            <button class="btn primary" :disabled="!canSearch || searching" @click="search"><t-icon name="search"></t-icon>{{ searching ? 'Searching…' : 'Search' }}</button>
          </div>
        </aside>

        <section class="results screen">
          <div class="results-bar">
            <input class="input" v-model="query" placeholder="Filter results…" title="Filter by name, game, location or type">
            <div class="segmented"><button v-for="(l, k) in { default: 'Default', species: 'Species', game: 'Game', level: 'Level', type: 'Type' }" :key="k" :class="{ on: sort === k }" @click="sort = k">{{ l }}</button></div>
            <span class="count">{{ shown.length.toLocaleString() }}<template v-if="shown.length !== results.length"> of {{ results.length.toLocaleString() }}</template> results</span>
          </div>
          <div v-if="searching" class="empty-state"><div><t-icon name="sparkle"></t-icon><h3>Searching every game…</h3></div></div>
          <div v-else-if="!results.length" class="empty-state"><div><t-icon name="search"></t-icon><h3>{{ searched ? 'No encounters match' : 'Find any encounter' }}</h3><p>{{ searched ? 'Try fewer filters or another game.' : 'Pick a Pokémon or moves on the left, then Search.' }}</p>
            <button v-if="searched && store.settings.encountersInGameOnly" class="btn" style="margin-top:10px" @click="setInGameOnly(false)"><t-icon name="search"></t-icon>Include Pokémon not in this game</button></div></div>
          <div v-else class="card-grid">
            <div v-for="r in shown" :key="r.token" class="rcard" :class="{ sel: sel === r }" @click="sel = r" @dblclick="load(r)">
              <span class="tag pill" :class="badgeClass(r)">{{ r.typeLabel }}</span>
              <span class="corner">Lv. {{ r.levelMin === r.levelMax ? r.levelMin : r.levelMin + '–' + r.levelMax }}</span>
              <img :src="sprite(r)" alt="" loading="lazy">
              <div class="name">{{ r.name }}</div>
              <div class="sub">{{ r.location || r.versionName }}</div>
              <t-games :games="r.games ?? []" :source="r.source" :max="3"></t-games>
            </div>
          </div>
        </section>

        <aside class="db-detail screen">
          <template v-if="sel">
            <div style="text-align:center"><img class="sprite" :src="sprite(sel)" style="width:204px;height:168px" alt=""></div>
            <h2 style="margin:0;text-align:center;font-family:var(--font-display)">{{ sel.name }}</h2>
            <div class="row" style="justify-content:center;flex-wrap:wrap;gap:6px;margin-top:8px">
              <span class="pill" :class="badgeClass(sel)">{{ sel.typeLabel }}</span><span class="pill">Gen {{ sel.generation }}</span>
              <span v-if="sel.shiny === 'never'" class="pill">Shiny locked</span><span v-if="sel.shiny === 'always'" class="pill shiny">★ Shiny</span>
              <span v-if="sel.alpha" class="pill bad">Alpha</span><span v-if="sel.gmax" class="pill bad">Gigantamax</span>
            </div>
            <div class="scroll"><div class="kv">
              <div v-if="sel.source"><small>Received in</small><t-games :source="sel.source" full></t-games></div>
              <div><small>{{ sel.source ? 'Can be moved to' : (sel.games ?? []).length === 1 ? 'Game' : 'Games' }}</small><t-games :games="sel.games ?? []" full></t-games></div>
              <div><small>Location</small><span>{{ sel.location || '—' }}</span></div>
              <div><small>Levels</small><span>{{ sel.levelMin === sel.levelMax ? sel.levelMin : sel.levelMin + ' – ' + sel.levelMax }}</span></div>
              <div v-for="f in sel.facts" :key="f.label"><small>{{ f.label }}</small><span>{{ f.value }}</span></div>
            </div></div>
            <button class="btn primary" @click="load()"><t-icon name="pokeball"></t-icon>Load into editor</button>
          </template>
          <div v-else class="empty-state"><div><t-icon name="info"></t-icon><p>Select a result to see where and how it's obtained.</p></div></div>
        </aside>
      </div>
    </div>`,
};
