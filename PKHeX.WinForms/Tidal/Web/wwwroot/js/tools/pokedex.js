// Pokédex: seen/caught per species, with PKHeX's own dex operations. Host: Api.Tool.Pokedex.cs.
// Actions are recorded in order and applied by the host on Save; forms/languages are under "More options".
window.TidalTools.pokedex = {
  props: { store: Object, tool: Object },
  emits: ['close'],
  data: () => ({ d: null, entries: [], ops: [], lastSet: {}, filter: 'all', query: '', shiny: false, limit: 300, busy: false, loading: true }),
  computed: {
    dirty() { return this.ops.length > 0; },
    seen() { return this.entries.filter(e => e.seen).length; },
    caught() { return this.entries.filter(e => e.caught).length; },
    shown() {
      const q = this.query.trim().toLowerCase();
      const num = /^#?\d+$/.test(q) ? Number(q.replace('#', '')) : null;
      return this.entries.filter(e => {
        if (this.filter === 'seen' && !(e.seen && !e.caught)) return false;
        if (this.filter === 'caught' && !e.caught) return false;
        if (this.filter === 'missing' && e.seen) return false;
        if (num != null) return e.species === num;
        return !q || e.name.toLowerCase().includes(q);
      });
    },
    visible() { return this.shown.slice(0, this.limit); },
  },
  watch: { filter() { this.limit = 300; }, query() { this.limit = 300; } },
  async mounted() {
    this.store.note = 'Click a Pokémon to change its entry';
    this.store.hints = [{ glyph: 'S', label: 'Save', run: () => this.save() }];
    await this.load();
  },
  methods: {
    async load() {
      this.loading = true;
      const d = await this.store.call('dex.get').catch(() => null);
      this.loading = false;
      if (!d) { this.$emit('close'); return; }
      this.d = d;
      this.entries = d.entries.map(e => ({ ...e }));
      this.ops = []; this.lastSet = {};
    },
    sprite(e) { return window.Tidal.url.species(e.species); },
    stateOf(e) { return e.caught ? 'caught' : e.seen ? 'seen' : 'none'; },
    /** Records a species' new state; repeated clicks on one species collapse into one action. */
    setState(e, state) {
      e.seen = state !== 'none';
      e.caught = state === 'caught';
      const i = this.lastSet[e.species];
      if (i != null) this.ops[i].state = state;
      else { this.lastSet[e.species] = this.ops.length; this.ops.push({ op: 'set', species: e.species, state, shiny: false }); }
    },
    cycle(e) {
      const next = this.d.seenOnly ? { none: 'seen', seen: 'caught', caught: 'none' } : { none: 'caught', seen: 'caught', caught: 'none' };
      this.setState(e, next[this.stateOf(e)]);
    },
    bulk(op, label) {
      this.ops.push({ op, shiny: this.shiny, species: 0, state: null });
      this.lastSet = {}; // later clicks apply after this bulk action
      for (const e of this.entries) {
        if (op === 'clearAll') { e.seen = e.caught = false; }
        else { e.seen = true; if (op !== 'seenAll') e.caught = true; }
      }
      this.store.toast(`${label} (not saved yet)`, 'info');
    },
    async clearAll() {
      if (await this.store.confirm('Clear every Pokédex entry (seen and caught)?') !== 'yes') return;
      this.bulk('clearAll', 'Cleared every entry');
    },
    more(ev) { const el = ev.target; if (el.scrollTop + el.clientHeight > el.scrollHeight - 400 && this.limit < this.shown.length) this.limit += 300; },
    async save() {
      if (!this.dirty || this.busy) return;
      this.busy = true;
      try {
        await this.store.call('dex.save', { ops: this.ops });
        this.store.toast('Pokédex saved', 'success');
        await this.load();
      } catch { /* toast */ } finally { this.busy = false; }
    },
    async confirmLeave() {
      return !this.dirty || await this.store.confirm('Discard your changes to the Pokédex?', { title: 'Unsaved changes' }) === 'yes';
    },
    async requestClose() { if (await this.confirmLeave()) this.$emit('close'); },
    async classic() {
      if (this.dirty && await this.store.confirm('Open the classic editor? Your unsaved changes here will be discarded.', { title: 'More options' }) !== 'yes') return;
      await this.store.call('tools.open', { id: this.tool.id }).catch(() => {});
      await this.load();
    },
    onKey(e) { if (e.key.toLowerCase() === 's' && !e.ctrlKey) { e.preventDefault(); this.save(); } },
  },
  template: `
    <t-tool title="Pokédex" :subtitle="d?.game" icon="book" :dirty="dirty" :busy="busy" :loading="loading"
            @save="save" @cancel="requestClose" @classic="classic">
      <div v-if="d" class="dex">
        <div class="dex-top">
          <div class="dex-stat"><small>Seen</small><b>{{ seen }}</b><span>/ {{ entries.length }}</span><i :style="{ width: (entries.length ? seen / entries.length * 100 : 0) + '%' }"></i></div>
          <div class="dex-stat caught"><small>Caught</small><b>{{ caught }}</b><span>/ {{ entries.length }}</span><i :style="{ width: (entries.length ? caught / entries.length * 100 : 0) + '%' }"></i></div>
          <div class="dex-bulk">
            <button class="btn" @click="bulk('seenAll', 'Marked every Pokémon as seen')"><t-icon name="search"></t-icon>All seen</button>
            <button class="btn" @click="bulk('caughtAll', 'Marked every Pokémon as caught')"><t-icon name="pokeball"></t-icon>All caught</button>
            <button v-if="d.detailed" class="btn" title="Every form, gender and language" @click="bulk('completeAll', 'Completed every entry')"><t-icon name="star"></t-icon>Complete</button>
            <t-switch v-if="d.detailed" v-model="shiny" label="Include shiny"></t-switch>
            <button class="btn" @click="clearAll"><t-icon name="trash"></t-icon>Clear</button>
          </div>
        </div>
        <div class="dex-bar">
          <input class="input grow" v-model="query" placeholder="Search by name or number…">
          <div class="segmented">
            <button v-for="(l, k) in { all: 'All', caught: 'Caught', seen: 'Seen only', missing: 'Not seen' }" :key="k" :class="{ on: filter === k }" @click="filter = k">{{ l }}</button>
          </div>
        </div>
        <div class="dex-grid" @scroll="more">
          <button v-for="e in visible" :key="e.species" class="dex-cell" :class="stateOf(e)" :title="'#' + e.species + ' ' + e.name + ' · ' + stateOf(e)" @click="cycle(e)">
            <span class="no">#{{ String(e.species).padStart(4, '0') }}</span>
            <img :src="sprite(e)" alt="" loading="lazy">
            <span class="nm">{{ e.name }}</span>
            <span v-if="e.caught" class="mark"><t-icon name="pokeball"></t-icon></span>
          </button>
          <div v-if="!shown.length" class="empty-state"><div><t-icon name="search"></t-icon><p>No Pokémon match.</p></div></div>
        </div>
        <p v-if="d.detailed" class="muted dex-note"><t-icon name="info"></t-icon>Marking a Pokémon caught completes its entry (forms, genders and languages). Per-form and language details are under <b>More options</b>.</p>
      </div>
    </t-tool>`,
};
