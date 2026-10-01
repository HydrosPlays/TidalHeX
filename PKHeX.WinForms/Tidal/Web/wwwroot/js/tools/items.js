// Items / bag editor: a port of PKHeX's SAV_Inventory (host: Api.Tool.Items.cs).
window.TidalTools.items = {
  props: { store: Object, tool: Object },
  emits: ['close'],
  data: () => ({ state: null, pouches: [], active: 0, query: '', owned: true, count: 1, busy: false, loading: true, snapshot: '', limit: 240 }),
  computed: {
    pouch() { return this.pouches[this.active] ?? null; },
    names() { return this.state?.itemNames ?? []; },
    fixed() { return !!this.state?.itemsFixed; },
    max() { return this.state?.hax ? this.state.maxQuantityHaX : (this.pouch?.maxCount ?? 999); },
    dirty() { return this.snapshot !== this.serialize(); },
    flags() {
      const c = this.state?.columns ?? {};
      return [
        c.favorite && { key: 'favorite', label: '★', title: 'Favorite' },
        c.new && { key: 'new', label: 'New', title: 'Marked as new' },
        c.freeSpace && { key: 'free', label: 'Free', title: 'In the free space pouch' },
        c.newShop && { key: 'shop', label: 'Shop', title: 'New in shops' },
        c.held && { key: 'held', label: 'Held', title: 'Held by a Pokémon' },
      ].filter(Boolean);
    },
    /** Rows shown for the pouch (keeps each row's index in the pouch for editing). */
    rows() {
      const p = this.pouch;
      if (!p) return [];
      const q = this.query.trim().toLowerCase();
      const list = [];
      p.items.forEach((r, i) => {
        if (this.fixed && this.owned && !q && r.count <= 0) return;
        if (q && !(this.names[r.id] ?? '').toLowerCase().includes(q)) return;
        list.push({ r, i });
      });
      if (this.fixed) list.sort((a, b) => (this.names[a.r.id] ?? '').localeCompare(this.names[b.r.id] ?? ''));
      return list;
    },
    shown() { return this.rows.slice(0, this.limit); },
    used() { return this.pouch ? this.pouch.items.filter(r => r.id && r.count > 0).length : 0; },
    canAdd() { return !this.fixed && this.pouch && this.pouch.items.length < this.pouch.capacity; },
  },
  watch: {
    active() { this.query = ''; this.limit = 240; this.count = this.pouch?.defaultCount ?? 1; },
    query() { this.limit = 240; },
  },
  async mounted() {
    this.store.note = 'Changes are written to the save when you press Save';
    this.store.hints = [{ glyph: 'S', label: 'Save', run: () => this.save() }];
    await this.load();
  },
  methods: {
    serialize() { return JSON.stringify(this.pouches.map(p => p.items)); },
    async load() {
      this.loading = true;
      const state = await this.store.call('items.get').catch(() => null);
      this.loading = false;
      if (!state) { this.$emit('close'); return; }
      this.state = state;
      this.pouches = state.pouches.map(p => ({ ...p, items: p.items.map(r => ({ ...r })) }));
      this.active = Math.min(this.active, Math.max(0, this.pouches.length - 1));
      this.count = this.pouch?.defaultCount ?? 1;
      this.snapshot = this.serialize();
      this.optionCache = new Map();
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.bag-pouch'), { stagger: 25, y: 8 }));
    },
    pouchIcon(p) { return window.Tidal.isHost ? `/sprite/bag/${p.type}` : null; },
    itemIcon(id) { return id ? window.Tidal.url.item(id) : null; },
    /** Items the pouch can hold (any item with HaX), sorted by name, for the item picker. */
    options(p) {
      const cached = this.optionCache?.get(p.type);
      if (cached) return cached;
      const ids = p.allowed.length ? p.allowed : this.names.map((_, i) => i).filter(i => i > 0);
      const opts = ids.filter(id => id > 0).map(id => ({ v: id, t: this.names[id] ?? `#${id}`, img: this.itemIcon(id) }))
        .sort((a, b) => a.t.localeCompare(b.t));
      this.optionCache?.set(p.type, opts);
      return opts;
    },
    addRow() {
      if (!this.canAdd) return;
      this.query = '';
      this.pouch.items.push({ id: 0, count: Math.min(1, this.max), favorite: false, new: false, free: false, freeIndex: 0, shop: false, held: false });
      this.$nextTick(() => {
        const rows = this.$el.querySelectorAll('.bag-row');
        const last = rows[rows.length - 1];
        last?.scrollIntoView({ block: 'nearest' });
        last?.querySelector('.combo input')?.focus();
      });
    },
    removeRow(i) {
      if (this.fixed) this.pouch.items[i].count = 0; // fixed rows stay; an item "removed" has no count
      else this.pouch.items.splice(i, 1);
    },
    toggle(r, key) { r[key] = !r[key]; },
    more(e) { const el = e.target; if (el.scrollTop + el.clientHeight > el.scrollHeight - 300 && this.limit < this.rows.length) this.limit += 240; },
    /** Pouch actions run by the host on a copy of this pouch (same rules as PKHeX); the result replaces the rows. */
    async action(name, label) {
      const p = this.pouch;
      if (!p || this.busy) return;
      this.busy = true;
      try {
        const res = await this.store.call('items.action', { pouch: p.type, action: name, count: this.count, items: p.items });
        p.items = res.items.map(r => ({ ...r }));
        if (label) this.store.toast(label, 'success');
      } catch { /* shown as a toast */ } finally { this.busy = false; }
    },
    sortMenu(e) {
      const r = e.currentTarget.getBoundingClientRect();
      this.store.menu({ clientX: r.left, clientY: r.bottom + 6 }, [
        { label: 'Name (A–Z)', icon: 'book', run: () => this.action('sortName') },
        { label: 'Name (Z–A)', icon: 'book', run: () => this.action('sortNameDesc') },
        '-',
        { label: 'Count (low → high)', icon: 'data', run: () => this.action('sortCount') },
        { label: 'Count (high → low)', icon: 'data', run: () => this.action('sortCountDesc') },
        '-',
        { label: 'Item number', icon: 'grid', run: () => this.action('sortIndex') },
        { label: 'Item number (reversed)', icon: 'grid', run: () => this.action('sortIndexDesc') },
      ]);
    },
    async removeAll() {
      if (await this.store.confirm(`Remove every item from ${this.pouch.name}?`) !== 'yes') return;
      this.action('removeAll', `${this.pouch.name} emptied`);
    },
    async save() {
      if (!this.dirty || this.busy) return;
      this.busy = true;
      try {
        await this.store.call('items.save', { pouches: this.pouches.map(p => ({ type: p.type, items: p.items })) });
        this.store.toast('Bag saved', 'success');
        await this.load(); // show exactly what was stored (counts clamped to the game's rules)
      } catch { /* toast */ } finally { this.busy = false; }
    },
    /** True if it's fine to leave: nothing unsaved, or the user agreed to discard it. */
    async confirmLeave() {
      return !this.dirty || await this.store.confirm('Discard your changes to the bag?', { title: 'Unsaved changes' }) === 'yes';
    },
    async requestClose() {
      if (await this.confirmLeave()) this.$emit('close');
    },
    async classic() {
      if (this.dirty && await this.store.confirm('Open the classic editor? Your unsaved changes here will be discarded.', { title: 'More options' }) !== 'yes') return;
      await this.store.call('tools.open', { id: this.tool.id }).catch(() => {});
      await this.load();
    },
    onKey(e) {
      if (e.key.toLowerCase() === 's' && !e.ctrlKey) { e.preventDefault(); this.save(); }
      else if (e.key === 'ArrowDown' && this.active < this.pouches.length - 1) { e.preventDefault(); this.active++; }
      else if (e.key === 'ArrowUp' && this.active > 0) { e.preventDefault(); this.active--; }
    },
  },
  template: `
    <t-tool title="Items" :subtitle="store.save?.game ? 'Bag · ' + store.save.game : 'Bag'" icon="bag" :dirty="dirty" :busy="busy" :loading="loading"
            @save="save" @cancel="requestClose" @classic="classic">
      <div v-if="!pouches.length" class="empty-state"><div><t-icon name="bag"></t-icon><h3>This save has no bag</h3></div></div>
      <div v-else class="bag">
        <nav class="bag-pouches">
          <button v-for="(p, i) in pouches" :key="p.type" class="bag-pouch" :class="{ on: i === active }" @click="active = i">
            <img v-if="pouchIcon(p)" :src="pouchIcon(p)" alt=""><t-icon v-else name="bag"></t-icon>
            <span>{{ p.name }}</span>
            <span class="n">{{ p.items.filter(r => r.id && r.count > 0).length }}</span>
          </button>
        </nav>

        <section class="bag-main">
          <div class="bag-bar">
            <input class="input grow" v-model="query" :placeholder="'Search ' + pouch.name + '…'">
            <div v-if="fixed" class="segmented"><button :class="{ on: owned }" @click="owned = true">Owned</button><button :class="{ on: !owned }" @click="owned = false">All</button></div>
            <button class="btn" :disabled="busy" @click="sortMenu"><t-icon name="swap"></t-icon>Sort</button>
            <button class="btn" :disabled="busy" @click="removeAll"><t-icon name="trash"></t-icon>Remove all</button>
          </div>
          <div class="bag-bar bulk" v-if="pouch.canBulkGive">
            <span class="muted">Quantity</span>
            <t-number v-model="count" :min="1" :max="max" cls="qty"></t-number>
            <button class="btn" :disabled="busy" @click="action('giveAll', 'Gave every item in ' + pouch.name)" :title="pouch.cramped ? 'Not every item fits; you can pick a random selection' : 'Add every item this pouch can hold'"><t-icon name="gift"></t-icon>Give all</button>
            <button class="btn" :disabled="busy" @click="action('setCounts', 'Updated every count')"><t-icon name="refresh"></t-icon>Set every count</button>
            <span class="muted grow" style="text-align:right">{{ used }} / {{ pouch.capacity }} slots used · max {{ max }} each</span>
          </div>

          <div class="bag-rows" @scroll="more">
            <div v-for="{ r, i } in shown" :key="pouch.type + ':' + i" class="bag-row" :class="{ zero: r.count <= 0 }">
              <img v-if="r.id" class="item-ic" :src="itemIcon(r.id)" alt=""><span v-else class="item-ic empty"></span>
              <span v-if="fixed" class="name" :title="names[r.id]">{{ names[r.id] }}</span>
              <t-combo v-else v-model="r.id" :options="options(pouch)" placeholder="Choose an item" no-icon></t-combo>
              <t-number v-model="r.count" :min="0" :max="max" cls="qty"></t-number>
              <span class="flags" v-if="flags.length">
                <button v-for="f in flags" :key="f.key" class="flag-btn" :class="{ on: r[f.key] }" :title="f.title" @click="toggle(r, f.key)">{{ f.label }}</button>
              </span>
              <button class="x" :title="fixed ? 'Set the count to 0' : 'Remove from the bag'" @click="removeRow(i)"><t-icon name="close"></t-icon></button>
            </div>
            <button v-if="canAdd && !query" class="bag-add" @click="addRow"><t-icon name="plus"></t-icon>Add an item</button>
            <div v-if="!rows.length && (query || fixed)" class="empty-state"><div><t-icon name="search"></t-icon><p>{{ query ? 'No items match.' : 'Nothing in this pouch yet. Switch to All to add items.' }}</p></div></div>
            <div v-if="!rows.length && !query && !fixed && !canAdd" class="empty-state"><div><p>This pouch is full.</p></div></div>
          </div>
        </section>
      </div>
    </t-tool>`,
};
