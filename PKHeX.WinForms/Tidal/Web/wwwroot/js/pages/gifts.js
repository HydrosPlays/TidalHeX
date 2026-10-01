// Mystery Gift database: browse every event distribution, filters apply instantly.
window.TidalPages.gifts = {
  props: { store: Object },
  data: () => ({ all: [], loading: true, lists: {}, species: -1, gens: {}, shiny: null, egg: null, query: '', sort: 'default', sel: null, limit: 240 }),
  computed: {
    speciesOpts() { return [{ v: -1, t: 'Any Pokémon' }, ...(this.lists.species ?? []).filter(o => o.v > 0 && this.present.has(o.v)).map(o => ({ ...o, img: window.Tidal.url.species(o.v) }))]; },
    present() { return new Set(this.all.map(g => g.species)); },
    genList() { return [...new Set(this.all.map(g => g.generation))].sort((a, b) => a - b); },
    filtered() {
      const q = this.query.trim().toLowerCase();
      let r = this.all.filter(g => this.gens[g.generation] !== false);
      if (this.species !== -1) r = r.filter(g => g.species === this.species);
      if (this.shiny !== null) r = r.filter(g => g.shiny === this.shiny);
      if (this.egg !== null) r = r.filter(g => g.egg === this.egg);
      if (q) r = r.filter(g => `${g.name} ${g.title} ${g.ot} ${g.type} ${g.cardId} ${g.fileName} ${(g.games ?? []).map(x => x.name).join(' ')} ${g.source ?? ''}`.toLowerCase().includes(q));
      const by = { newest: (a, b) => b.generation - a.generation, species: (a, b) => a.species - b.species, card: (a, b) => a.cardId - b.cardId, title: (a, b) => a.title.localeCompare(b.title) }[this.sort];
      return by ? [...r].sort(by) : r;
    },
    shown() { return this.filtered.slice(0, this.limit); },
  },
  watch: { filtered() { this.limit = 240; } },
  async mounted() {
    this.store.note = 'Filters apply instantly';
    this.store.hints = [
      { glyph: 'X', label: 'Save gift file', run: () => this.saveFile() },
      { glyph: 'A', label: 'Load into editor', run: () => this.load() },
    ];
    const [all, species] = await Promise.all([this.store.call('gift.all').catch(() => []), this.store.list('species').catch(() => [])]);
    this.lists.species = species;
    this.all = all;
    this.markLoaded();
  },
  methods: {
    markLoaded() {
      const saveGen = this.store.save?.generation ?? 9;
      for (const g of this.genList) if (!(g in this.gens)) this.gens[g] = saveGen < 4 || g <= saveGen;
      this.loading = false;
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.rcard'), { stagger: 8, y: 16, scale: 0.94 }));
    },
    /** PKHeX's "filter unavailable species": off shows every gift, including Pokémon this game can't hold. */
    async setInGameOnly(v) {
      if (!(await this.store.setOption('giftsInGameOnly', v))) return;
      this.loading = true; this.sel = null;
      this.all = await this.store.call('gift.all').catch(() => []);
      if (this.species !== -1 && !this.present.has(this.species)) this.species = -1;
      this.markLoaded();
    },
    sprite(g) { return g.sprite ?? window.Tidal.url.enc(g.token); },
    setGens(fn) { for (const g of this.genList) this.gens[g] = fn(g); },
    toggleGen(g, e) { if (e.shiftKey) this.setGens(x => x === g); else this.gens[g] = !this.gens[g]; },
    more(e) { const el = e.target; if (el.scrollTop + el.clientHeight > el.scrollHeight - 300 && this.limit < this.filtered.length) this.limit += 240; },
    async load(g = this.sel) {
      if (!g) return;
      const state = await this.store.call('gift.load', { token: g.token }).catch(() => null);
      if (state) { this.store.loadEditor(state); this.store.toast(`Loaded ${g.title || g.name}`, 'success'); }
    },
    async saveFile(g = this.sel) { if (g) await this.store.call('gift.saveFile', { token: g.token }).catch(() => {}); },
    reset() { this.species = -1; this.shiny = null; this.egg = null; this.query = ''; const saveGen = this.store.save?.generation ?? 9; this.setGens(g => saveGen < 4 || g <= saveGen); },
    onKey(e) {
      if (e.key === 'Enter' && this.sel) this.load();
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
          <div class="field"><label>Pokémon</label><t-combo v-model="species" :options="speciesOpts"></t-combo></div>
          <div class="field"><label>Search</label><input class="input" v-model="query" placeholder="Title, OT, card #…"></div>
          <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            <div class="field"><label>Shiny</label><t-tri v-model="shiny"></t-tri></div>
            <div class="field"><label>Egg</label><t-tri v-model="egg"></t-tri></div>
          </div>
          <div class="field"><label>Availability</label>
            <t-switch :model-value="store.settings.giftsInGameOnly" @update:model-value="setInGameOnly" label="Only Pokémon in this game"></t-switch>
            <small class="muted">{{ store.settings.giftsInGameOnly ? 'Gifts this save can’t hold are hidden (PKHeX default).' : 'Showing every gift in the database.' }}</small>
          </div>
          <div class="field"><label>Generations</label>
            <div style="display:flex;flex-wrap:wrap;gap:6px"><button v-for="g in genList" :key="g" class="chip" :class="{ on: gens[g] !== false }" @click="toggleGen(g, $event)">Gen {{ g }}</button></div>
            <div class="row" style="margin-top:6px"><button class="btn small" @click="setGens(() => true)">All</button><button class="btn small" @click="reset">This save</button></div>
          </div>
          <div class="field" style="margin-top:auto"><label>Sort</label>
            <div class="segmented" style="flex-wrap:wrap;border-radius:16px"><button v-for="(l, k) in { default: 'Default', newest: 'Newest', species: 'Species', card: 'Card #', title: 'Title' }" :key="k" :class="{ on: sort === k }" @click="sort = k">{{ l }}</button></div>
          </div>
        </aside>

        <section class="results screen">
          <div class="results-bar"><h4 class="card-title" style="margin:0">Event distributions</h4><span class="count">{{ filtered.length.toLocaleString() }} of {{ all.length.toLocaleString() }} gifts</span></div>
          <div v-if="loading" class="empty-state"><div><t-icon name="gift"></t-icon><h3>Unwrapping every gift…</h3></div></div>
          <div v-else-if="!filtered.length" class="empty-state"><div><t-icon name="gift"></t-icon><h3>No gifts match</h3><p>Try another generation or clear the search.</p>
            <button v-if="store.settings.giftsInGameOnly" class="btn" style="margin-top:10px" @click="setInGameOnly(false)"><t-icon name="gift"></t-icon>Include Pokémon not in this game</button></div></div>
          <div v-else class="card-grid" @scroll="more">
            <div v-for="g in shown" :key="g.token" class="rcard" :class="{ sel: sel === g }" @click="sel = g" @dblclick="load(g)">
              <span class="tag pill">{{ g.type.toUpperCase() }}</span>
              <span class="corner">{{ g.cardId > 0 ? '#' + String(g.cardId).padStart(4, '0') : 'Gen ' + g.generation }}</span>
              <img :src="sprite(g)" alt="" loading="lazy">
              <div class="name">{{ g.name }}</div>
              <div class="sub">{{ g.title }}</div>
              <t-games :games="g.games ?? []" :source="g.source" :max="3"></t-games>
            </div>
          </div>
        </section>

        <aside class="db-detail screen">
          <template v-if="sel">
            <div style="text-align:center"><img class="sprite" :src="sprite(sel)" style="width:204px;height:168px" alt=""></div>
            <h2 style="margin:0;text-align:center;font-family:var(--font-display)">{{ sel.name }}</h2>
            <div class="muted" style="text-align:center">{{ sel.title }}</div>
            <div class="row" style="justify-content:center;flex-wrap:wrap;gap:6px;margin-top:8px">
              <span class="pill solid">{{ sel.type.toUpperCase() }}</span><span class="pill">Gen {{ sel.generation }}</span>
              <span v-if="sel.shiny" class="pill shiny">★ Shiny</span><span v-if="sel.egg" class="pill warn">Egg</span><span v-if="sel.isItem" class="pill">Item</span>
            </div>
            <div class="scroll"><div class="kv">
              <div v-if="sel.source"><small>Received in</small><t-games :source="sel.source" full></t-games></div>
              <div><small>{{ sel.source ? 'Can be moved to' : (sel.games ?? []).length === 1 ? 'Game' : 'Games' }}</small><t-games :games="sel.games ?? []" full></t-games></div>
              <div><small>Card</small><span>{{ sel.cardId > 0 ? '#' + sel.cardId : '—' }}</span></div>
              <div><small>Original trainer</small><span>{{ sel.ot || '—' }}</span></div>
              <div><small>Level</small><span>{{ sel.level }}</span></div>
              <div v-for="(d, i) in sel.details" :key="i"><span>{{ d }}</span></div>
              <div><small>File</small><span>{{ sel.fileName }}</span></div>
            </div></div>
            <div style="display:grid;grid-template-columns:1fr 1fr;gap:8px">
              <button class="btn primary" style="grid-column:1/-1" @click="load()"><t-icon name="pokeball"></t-icon>Load into editor</button>
              <button class="btn" style="grid-column:1/-1" @click="saveFile()"><t-icon name="gift"></t-icon>Save gift file</button>
            </div>
          </template>
          <div v-else class="empty-state"><div><t-icon name="gift"></t-icon><p>Select a gift to see its card details.</p></div></div>
        </aside>
      </div>
    </div>`,
};
