// Pokémon editor.
(() => {
  const TYPE_COLORS = {
    normal: '#a8a878', fighting: '#d0463c', flying: '#8e9cf0', poison: '#a646b6', ground: '#d8b45a', rock: '#b8a038',
    bug: '#98b820', ghost: '#6f58a8', steel: '#8ea8c0', fire: '#f08030', water: '#4f8ff0', grass: '#58b848',
    electric: '#f5c518', psychic: '#f55887', ice: '#78d8d8', dragon: '#6f38f8', dark: '#6f5848', fairy: '#ee99ac', stellar: '#5fd0ff',
  };
  const typeColor = t => TYPE_COLORS[String(t ?? '').toLowerCase()] ?? '#5f7fa0';
  const CATEGORIES = { P: 'physical', S: 'special', T: 'status' };
  const CATEGORY_NAMES = { physical: 'Physical', special: 'Special', status: 'Status' };
  // PKMEditor's move flag buttons; Shift+click applies the flags the Pokémon can legally have.
  const MOVE_FLAGS = {
    records: { name: 'Relearn Flags', desc: 'TM / TR moves it can relearn', icon: 'book', legal: 'legalRecords' },
    moveshop: { name: 'Move Shop', desc: 'Purchased and mastered moves', icon: 'bag', legal: 'legalMoveShop' },
    plus: { name: 'Plus Flags', desc: 'Plus moves it has unlocked', icon: 'sparkle', legal: 'legalPlus' },
  };
  const SECTIONS = [
    { key: 'overview', label: 'Overview', icon: 'pokeball' },
    { key: 'met', label: 'Met', icon: 'globe' },
    { key: 'stats', label: 'Stats', icon: 'sparkle' },
    { key: 'moves', label: 'Moves', icon: 'sword' },
    { key: 'trainer', label: 'Trainer', icon: 'user' },
    { key: 'extras', label: 'Extras', icon: 'star' },
  ];

  window.TidalPages.editor = {
    props: { store: Object },
    data: () => ({ section: 'overview', lists: {}, busy: false, metLocations: [], eggLocations: [], moveData: null }),
    computed: {
      e() { return this.store.editor; },
      /** The Egg toggle's picture (Manaphy's egg differs; the sprite style follows the loaded save). */
      eggSprite() { return `/sprite/egg/${this.e?.species === 490 ? 490 : 0}?v=${this.store.saveEpoch}`; },
      /** Move options with type and category; learnable moves first and marked, as in PKHeX's move lists. */
      moveOptions() { return this.decorateMoves(new Set(this.e?.learnable ?? [])); },
      relearnOptions() { return this.decorateMoves(new Set()); },
      isAlpha() { return this.e?.fields?.some(f => f.key === 'alpha' && f.value); },
      hasGv() { return this.e?.stats?.some(s => s.gv != null); },
      hasAv() { return this.e?.stats?.some(s => s.av != null); },
      sections() { return SECTIONS.filter(s => s.key !== 'met' || this.e?.met); },
      species() {
        // The chosen species shows its current form and colors; the others show their base form.
        const e = this.e, url = window.Tidal.url;
        return (this.lists.species ?? []).map(o => ({ ...o, img: !o.v ? null : o.v === e?.species ? this.formImg({ v: e.form }) : url.species(o.v) }));
      },
      items() { return (this.lists.items ?? []).map(o => ({ ...o, img: o.v ? window.Tidal.url.item(o.v) : null })); },
      balls() { return (this.lists.balls ?? []).map(o => ({ ...o, img: window.Tidal.url.ball(o.v) })); },
      ballName() { return this.balls.find(b => b.v === this.e?.ball)?.t ?? 'Ball'; },
      shinyTip() {
        const e = this.e;
        if (!e) return '';
        if (e.format < 3) return e.shiny === 'none' ? 'Make shiny' : 'Shiny · click to remove';
        const keys = 'Shift: square · Ctrl: star · Alt: keep the PID (changes the SID)';
        return e.shiny === 'none' ? `Make shiny
${keys}` : `Shiny (${e.shiny}) · click to remove
${keys}`;
      },
      title() { return this.e?.isNicknamed && this.e?.nickname ? this.e.nickname : this.e?.speciesName; },
      statMax() { return Math.max(1, ...(this.e?.stats ?? []).map(s => s.value)); },
      totals() {
        const st = this.e?.stats ?? [];
        return { base: st.reduce((a, s) => a + s.base, 0), iv: st.reduce((a, s) => a + s.iv, 0), ev: st.reduce((a, s) => a + s.ev, 0), value: st.reduce((a, s) => a + s.value, 0) };
      },
      radar() {
        const st = this.e?.stats ?? [];
        if (!st.length) return null;
        const order = ['hp', 'atk', 'def', 'spe', 'spd', 'spa'];
        const pts = order.map(k => st.find(s => s.key === k)).filter(Boolean);
        const max = Math.max(...pts.map(p => p.value), 1);
        const c = 120, r = 88;
        const at = (i, f) => { const a = -Math.PI / 2 + (i * Math.PI * 2) / pts.length; return [c + Math.cos(a) * r * f, c + Math.sin(a) * r * f]; };
        const poly = f => pts.map((_, i) => at(i, f).join(',')).join(' ');
        return {
          grid: [0.25, 0.5, 0.75, 1].map(poly),
          shape: pts.map((p, i) => at(i, Math.max(0.06, p.value / max)).join(',')).join(' '),
          labels: pts.map((p, i) => { const [x, y] = at(i, 1.2); return { x, y, t: p.name.replace('Sp. ', 'Sp') }; }),
        };
      },
    },
    watch: {
      'e.met.version'(v) { if (v != null) this.loadLocations(); },
      'store.saveEpoch'() { this.loadMoveData(); }, // move types depend on the game
    },
    async mounted() {
      this.store.note = '';
      this.store.hints = [
        { glyph: 'X', label: 'Export file', run: () => this.exportFile() },
        { glyph: 'Y', label: 'Legality', run: () => this.legality() },
        { glyph: 'A', label: 'Place in box', run: () => this.place() },
      ];
      if (!this.store.editor) this.store.editor = await this.store.call('editor.get').catch(() => null);
      const names = ['species', 'natures', 'items', 'balls', 'languages', 'games', 'moves', 'types'];
      const loaded = await Promise.all(names.map(n => this.store.list(n).catch(() => [])));
      names.forEach((n, i) => { this.lists[n] = loaded[i]; });
      this.loadLocations();
      this.loadMoveData();
      this.animateIn();
    },
    methods: {
      typeColor,
      catName(c) { return CATEGORY_NAMES[c] ?? ''; },
      async loadMoveData() { this.moveData = await this.store.call('editor.moveData').catch(() => null); },
      typeIcon(type) { return type >= 0 ? `/sprite/type/${type}?v=${this.store.save?.generation ?? 0}` : null; },
      moveTypeImg(o) { return o.v ? this.typeIcon(this.moveData?.types[o.v] ?? -1) : null; },
      decorateMoves(learn) {
        const md = this.moveData;
        const all = (this.lists.moves ?? []).map(o => {
          const can = learn.has(o.v);
          return { ...o, type: md?.types[o.v] ?? -1, cat: CATEGORIES[md?.categories[o.v]] ?? null, learn: can, cls: can ? 'learn' : '' };
        });
        return learn.size ? [...all.filter(o => o.learn), ...all.filter(o => !o.learn)] : all;
      },
      ballImg(id) { return window.Tidal.url.ball(id); },
      cleanUrl(src) { return window.Tidal.url.clean(src); },
      animateIn() {
        const fx = window.TidalFx;
        fx.enter(this.$el.querySelectorAll('.hero > *'), { stagger: 40, y: 14 });
        this.$nextTick(() => fx.enter(this.$el.querySelectorAll('.editor-body .field, .editor-body .move-card, .editor-body tr.srow, .editor-body .section-title'), { stagger: 18, y: 10, delay: 80 }));
        // The hero sprite's gentle float is a CSS animation (.pedestal img), paused while the window is inactive.
      },
      async loadLocations() {
        if (!this.e?.met) return;
        const v = this.e.met.version;
        [this.metLocations, this.eggLocations] = await Promise.all([
          this.store.call('list.get', { name: 'metLocations', version: v }).catch(() => []),
          this.e.met.hasEggMet ? this.store.call('list.get', { name: 'eggLocations', version: v }).catch(() => []) : Promise.resolve([]),
        ]);
      },
      async set(field, value) {
        if (!this.e) return;
        this.busy = true;
        try {
          const next = await this.store.call('editor.set', { field, value });
          const speciesChanged = next.species !== this.e.species || next.form !== this.e.form;
          this.store.editor = next;
          if (speciesChanged) this.$nextTick(() => window.TidalFx.pop(this.$el.querySelector('.pedestal img')));
        } catch { /* toast shown by store */ } finally { this.busy = false; }
      },
      /** Sprite of a form of the current species, in its current colors and gender. */
      formImg(o) { const e = this.e; return e ? window.Tidal.url.species(e.species, o.v, e.shiny !== 'none', e.gender) : null; },
      /** PKHeX's shiny button: Shift = square, Ctrl = star, Alt = keep the PID and change the SID instead. */
      shinyClick(ev) {
        const type = ev.shiftKey ? 'square' : ev.ctrlKey ? 'star' : 'random';
        const field = ev.altKey && this.e.format >= 3 ? 'shinySID' : 'shiny';
        this.set(field, this.e.shiny === 'none' || type !== 'random' ? type : 'none');
      },
      async legality() {
        const r = await this.store.call('editor.legality').catch(() => null);
        if (r) this.store.report(r.valid ? 'Legal' : 'Legality issues', r.report);
      },
      async exportFile() { await this.store.call('editor.exportFile').catch(() => {}); },
      async suggest(what) {
        const next = await this.store.call('editor.suggest', { what }).catch(() => null);
        if (next) { this.store.editor = next; this.store.toast('Applied suggestion', 'success'); }
      },
      place() {
        this.store.placing = true;
        this.store.go('boxes');
        this.store.toast('Pick a slot to place this Pokémon', 'info');
      },
      classic(what) { this.store.call('editor.classic', { what }).then(s => { if (s) this.store.editor = s; }).catch(() => {}); },
      fieldsFor(section) { return (this.e?.fields ?? []).filter(f => f.section === section); },
      setField(key, value) { this.set('x.' + key, value); },
      suggestField(key) { this.suggest('field:' + key); },
      flagInfo(f) { return MOVE_FLAGS[f] ?? { name: f, desc: '', icon: 'tools' }; },
      /** Opens PKHeX's flag editor; Shift+click sets the legal flags instead (as in PKHeX). */
      async moveFlags(f, ev) {
        if (!ev.shiftKey) { this.classic(f); return; }
        const next = await this.store.call('editor.suggest', { what: this.flagInfo(f).legal }).catch(() => null);
        if (next) { this.store.editor = next; this.store.toast(`Set the legal ${this.flagInfo(f).name.toLowerCase()}`, 'success'); }
      },
      onKey(e) {
        const i = this.sections.findIndex(s => s.key === this.section);
        if (e.key === 'ArrowRight' && e.altKey) this.section = this.sections[(i + 1) % this.sections.length].key;
        else if (e.key === 'ArrowLeft' && e.altKey) this.section = this.sections[(i - 1 + this.sections.length) % this.sections.length].key;
        else if (e.key.toLowerCase() === 'y') this.legality();
        else if (e.key.toLowerCase() === 'x') this.exportFile();
      },
      switchSection(key) {
        this.section = key;
        this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.editor-body .field, .editor-body .move-card, .editor-body tr.srow, .editor-body .section-title, .editor-body .radar'), { stagger: 14, y: 10 }));
      },
    },
    template: `
      <div class="page">
        <div v-if="!e || e.empty" class="empty-state screen">
          <div><t-icon name="pokeball"></t-icon><h2>No Pokémon loaded</h2><p>Open a file, pick one from your boxes, or load an encounter or gift.</p>
            <div class="row" style="justify-content:center;margin-top:12px"><button class="btn primary" @click="store.go('boxes')"><t-icon name="boxes"></t-icon>Go to boxes</button><button class="btn" @click="store.openFile()"><t-icon name="open"></t-icon>Open file</button></div></div>
        </div>
        <div v-else class="editor">
          <!-- Hero -->
          <aside class="hero screen">
            <div class="pedestal">
              <svg class="rings" viewBox="0 0 260 206"><circle cx="130" cy="112" r="96" stroke-width="10"/><circle cx="130" cy="112" r="74" stroke-width="2"/><circle cx="130" cy="112" r="52" stroke-width="6"/></svg>
              <img class="sprite" :src="cleanUrl(e.sprite)" alt="">
            </div>
            <h2>{{ title }}</h2>
            <div class="nick" v-if="e.isNicknamed">{{ e.speciesName }}</div>
            <div class="nick">Lv. {{ e.level }}<template v-if="e.isEgg"> · Egg</template></div>
            <div class="types"><span v-for="t in e.types" :key="t.id" class="type" :style="{ background: typeColor(t.id) }">{{ t.name }}</span><span v-if="isAlpha" class="type alpha-badge" title="Alpha Pokémon">Alpha</span></div>
            <div class="quick">
              <button class="round-toggle shiny" :class="{ on: e.shiny !== 'none' }" :title="shinyTip" @click="shinyClick">★</button>
              <button v-if="!e.genderLocked" class="round-toggle" :class="{ on: true, male: e.gender === 0, female: e.gender === 1 }" title="Toggle gender" @click="set('gender', e.gender === 0 ? 1 : 0)">{{ e.gender === 0 ? '♂' : '♀' }}</button>
              <button v-else class="round-toggle" title="Genderless / fixed gender" disabled>{{ e.gender === 0 ? '♂' : e.gender === 1 ? '♀' : '–' }}</button>
              <button v-if="e.met" class="round-toggle" :title="ballName" @click="switchSection('met')"><img :src="ballImg(e.ball)" alt=""></button>
              <button v-if="e.format >= 2" class="round-toggle egg" :class="{ on: e.isEgg }" :title="e.isEgg ? 'Egg (click to hatch)' : 'Not an egg (click to make it one)'" @click="set('isEgg', !e.isEgg)"><img :src="eggSprite" alt="Egg"></button>
            </div>
            <div v-if="e.legality" class="legal-card" :class="e.legality.valid ? 'ok' : 'bad'" @click="legality">
              <t-icon :name="e.legality.valid ? 'shield' : 'warn'"></t-icon>
              <div><b>{{ e.legality.valid ? 'Legal' : 'Legality issues' }}</b><small>{{ e.legality.valid ? 'Passes every check · view report' : (e.legality.issues[0] ?? 'View the full report') }}</small></div>
            </div>
            <div class="actions">
              <button class="btn primary" @click="place"><t-icon name="boxes"></t-icon>Place in box</button>
              <button class="btn" @click="exportFile"><t-icon name="file"></t-icon>Export</button>
              <button class="btn" @click="legality"><t-icon name="shield"></t-icon>Report</button>
            </div>
          </aside>

          <!-- Sections -->
          <section class="editor-main">
            <div class="segmented" style="align-self:flex-start">
              <button v-for="s in sections" :key="s.key" :class="{ on: section === s.key }" @click="switchSection(s.key)">{{ s.label }}</button>
            </div>
            <div class="editor-body screen">
              <!-- Overview -->
              <div v-if="section === 'overview'" class="form-grid">
                <div class="field"><label>Species</label><t-combo :model-value="e.species" :options="species" @update:model-value="v => set('species', v)"></t-combo></div>
                <div class="field" v-if="e.forms && e.forms.length > 1"><label>Form</label><t-combo :model-value="e.form" :options="e.forms" :img-for="formImg" @update:model-value="v => set('form', v)"></t-combo></div>
                <div class="field" v-else-if="e.formNote"><label>Form</label><div class="input readonly" :title="e.formNote.hint">{{ e.formNote.name }}</div><small class="field-hint"><t-icon name="info"></t-icon>{{ e.formNote.hint }}</small></div>
                <div class="field"><label>Nickname</label><t-text :model-value="e.nickname" :maxlength="e.nicknameMax || 12" @update:model-value="v => set('nickname', v)"></t-text></div>
                <div class="field"><label>Nicknamed</label><div style="height:40px;display:flex;align-items:center"><t-switch :model-value="e.isNicknamed" @update:model-value="v => set('isNicknamed', v)" label="Custom nickname"></t-switch></div></div>
                <div class="field"><label>Level</label><t-number :model-value="e.level" :min="1" :max="100" @update:model-value="v => set('level', v)"></t-number></div>
                <div class="field"><label>Experience</label><t-number :model-value="e.exp" :min="0" :max="2000000" @update:model-value="v => set('exp', v)"></t-number></div>
                <div class="field" v-if="e.nature != null"><label>Nature</label><t-combo :model-value="e.nature" :options="lists.natures ?? []" @update:model-value="v => set('nature', v)"></t-combo></div>
                <div class="field" v-if="e.hasStatNature"><label>Stat nature (mint)</label><t-combo :model-value="e.statNature" :options="lists.natures ?? []" @update:model-value="v => set('statNature', v)"></t-combo></div>
                <div class="field" v-if="e.abilities && e.abilities.length"><label>Ability</label><t-combo :model-value="e.ability" :options="e.abilities" @update:model-value="v => set('ability', v)"></t-combo><small v-if="e.abilityNote" class="field-hint"><t-icon name="info"></t-icon>{{ e.abilityNote }}</small></div>
                <div class="field" v-if="e.hasHeldItem"><label>Held item</label><t-combo :model-value="e.heldItem" :options="items" @update:model-value="v => set('heldItem', v)"></t-combo></div>
                <div class="field" v-if="e.language != null"><label>Language</label><t-combo :model-value="e.language" :options="lists.languages ?? []" @update:model-value="v => set('language', v)"></t-combo></div>
                <div class="field" v-if="e.friendship != null"><label>{{ e.isEgg ? 'Egg cycles' : 'Friendship' }}</label><t-number :model-value="e.friendship" :min="0" :max="255" @update:model-value="v => set('friendship', v)"></t-number></div>
                <t-fields :fields="fieldsFor('overview')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
              </div>

              <!-- Met -->
              <div v-else-if="section === 'met' && e.met" class="form-grid">
                <div class="field"><label>Origin game</label><t-combo :model-value="e.met.version" :options="lists.games ?? []" @update:model-value="v => set('met.version', v)"></t-combo></div>
                <div class="field wide"><label>Met location</label><t-combo :model-value="e.met.location" :options="metLocations" @update:model-value="v => set('met.location', v)"></t-combo></div>
                <div class="field"><label>Met level</label><t-number :model-value="e.met.level" :min="0" :max="100" @update:model-value="v => set('met.level', v)"></t-number></div>
                <div class="field" v-if="e.met.date !== null"><label>Met date</label><input class="input" type="date" :value="e.met.date" @change="set('met.date', $event.target.value)"></div>
                <div class="field"><label>Fateful encounter</label><div style="height:40px;display:flex;align-items:center"><t-switch :model-value="e.met.fateful" @update:model-value="v => set('met.fateful', v)" label="Event Pokémon"></t-switch></div></div>
                <div class="section-title">Poké Ball</div>
                <div class="field" style="grid-column:1/-1">
                  <div style="display:flex;flex-wrap:wrap;gap:8px">
                    <button v-for="b in balls" :key="b.v" class="round-toggle" :class="{ on: b.v === e.ball }" :style="b.v === e.ball ? 'box-shadow: var(--ring)' : ''" :title="b.t" @click="set('ball', b.v)"><img :src="b.img" alt=""></button>
                  </div>
                </div>
                <template v-if="e.met.hasEggMet">
                  <div class="section-title">Egg</div>
                  <div class="field wide"><label>Egg location</label><t-combo :model-value="e.met.eggLocation" :options="eggLocations" @update:model-value="v => set('met.eggLocation', v)"></t-combo></div>
                  <div class="field" v-if="e.met.eggDate !== null"><label>Egg date</label><input class="input" type="date" :value="e.met.eggDate" @change="set('met.eggDate', $event.target.value)"></div>
                </template>
                <t-fields :fields="fieldsFor('met')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
              </div>

              <!-- Stats -->
              <div v-else-if="section === 'stats'" class="stats-wrap">
                <div>
                  <table class="stat-table">
                    <thead><tr><th>Stat</th><th>Base</th><th>IV</th><th>EV</th><th v-if="hasGv" title="Legends: Arceus effort levels">GV</th><th v-if="hasAv" title="Let's Go awakening values">AV</th><th v-if="e.hasHyperTraining">HT</th><th style="width:30%"></th><th style="text-align:right">Total</th></tr></thead>
                    <tbody>
                      <tr v-for="s in e.stats" :key="s.key" class="srow">
                        <td :class="{ plus: s.natureMod > 0, minus: s.natureMod < 0 }">{{ s.name }}<span v-if="s.natureMod > 0"> ▲</span><span v-if="s.natureMod < 0"> ▼</span></td>
                        <td class="muted">{{ s.base }}</td>
                        <td><t-number cls="num" :model-value="s.iv" :min="0" :max="e.ivMax" @update:model-value="v => set('stats.' + s.key + '.iv', v)"></t-number></td>
                        <td><t-number cls="num" :model-value="s.ev" :min="0" :max="e.evMax" @update:model-value="v => set('stats.' + s.key + '.ev', v)"></t-number></td>
                        <td v-if="hasGv" :class="{ over: s.gv > s.gvMax }" :title="'Legal maximum for this IV: ' + s.gvMax"><t-number cls="num" :model-value="s.gv" :min="0" :max="10" @update:model-value="v => set('stats.' + s.key + '.gv', v)"></t-number></td>
                        <td v-if="hasAv"><t-number cls="num" :model-value="s.av" :min="0" :max="200" @update:model-value="v => set('stats.' + s.key + '.av', v)"></t-number></td>
                        <td v-if="e.hasHyperTraining"><t-switch :model-value="s.ht" @update:model-value="v => set('stats.' + s.key + '.ht', v)"></t-switch></td>
                        <td><div class="bar"><i :style="{ width: (s.value / statMax * 100) + '%' }"></i></div></td>
                        <td>{{ s.value }}</td>
                      </tr>
                      <tr class="srow total"><td>Total</td><td class="muted">{{ totals.base }}</td><td class="muted" style="text-align:center">{{ totals.iv }}</td><td :style="{ color: totals.ev > e.evTotalMax ? 'var(--bad)' : '' }" style="text-align:center">{{ totals.ev }} / {{ e.evTotalMax }}</td><td v-if="hasGv"></td><td v-if="hasAv"></td><td v-if="e.hasHyperTraining"></td><td></td><td>{{ totals.value }}</td></tr>
                    </tbody>
                  </table>
                  <div class="row" style="margin-top:12px;flex-wrap:wrap">
                    <button class="btn small" @click="suggest('maxIVs')"><t-icon name="wand"></t-icon>Max IVs</button>
                    <button class="btn small" @click="suggest('randomIVs')"><t-icon name="refresh"></t-icon>Random IVs</button>
                    <button class="btn small" @click="suggest('clearEVs')"><t-icon name="close"></t-icon>Clear EVs</button>
                    <button class="btn small" @click="suggest('suggestEVs')"><t-icon name="sparkle"></t-icon>Suggest EVs</button>
                  </div>
                  <t-fields :fields="fieldsFor('stats')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
                </div>
                <svg v-if="radar" class="radar" viewBox="0 0 240 240">
                  <polygon v-for="(g, i) in radar.grid" :key="i" class="grid" :points="g"/>
                  <polygon class="shape" :points="radar.shape"/>
                  <text v-for="l in radar.labels" :key="l.t" :x="l.x" :y="l.y" text-anchor="middle" dominant-baseline="middle">{{ l.t }}</text>
                </svg>
              </div>

              <!-- Moves -->
              <div v-else-if="section === 'moves'">
                <div class="moves">
                  <div v-for="(m, i) in e.moves" :key="i" class="move-card" :style="{ '--type-color': typeColor(m.type) }">
                    <div class="top">
                      <span class="type" :style="{ background: typeColor(m.type) }">{{ m.type || '—' }}</span>
                      <span v-if="m.category" class="mcat" :class="m.category" :title="catName(m.category) + ' move'"><t-icon :name="m.category"></t-icon>{{ catName(m.category) }}</span>
                      <span class="flag" v-if="m.legal === false"><span class="pill bad">Not legal</span></span>
                      <span class="flag" v-else-if="m.id"><span class="pill good">OK</span></span>
                    </div>
                    <t-combo class="move-combo" :model-value="m.id" :options="moveOptions" :img-for="moveTypeImg" @update:model-value="v => set('moves.' + i + '.id', v)">
                      <template #option="{ o }"><img v-if="o.type >= 0 && o.v" class="type-ic" :src="typeIcon(o.type)" alt=""><span v-if="o.cat" class="mcat mini" :class="o.cat" :title="catName(o.cat)"><t-icon :name="o.cat"></t-icon></span><span>{{ o.t }}</span><span v-if="o.learn" class="meta learn">Can learn</span></template>
                    </t-combo>
                    <div class="mrow">
                      <span class="muted" style="align-self:center">PP {{ m.pp }} / {{ m.maxPp }}</span>
                      <div class="field"><label>PP Ups</label><t-number :model-value="m.ppUps" :min="0" :max="3" @update:model-value="v => set('moves.' + i + '.ppUps', v)"></t-number></div>
                      <div class="field"><label>PP</label><t-number :model-value="m.pp" :min="0" :max="m.maxPp" @update:model-value="v => set('moves.' + i + '.pp', v)"></t-number></div>
                    </div>
                  </div>
                </div>
                <div class="row" style="margin-top:14px"><button class="btn small" @click="suggest('moves')"><t-icon name="wand"></t-icon>Suggest legal moves</button></div>
                <div v-if="e.moveFlags?.length" class="move-flags">
                  <div class="section-title">Move flags</div>
                  <div class="tool-grid">
                    <button v-for="f in e.moveFlags" :key="f" class="tool" title="Shift+click: set every flag it can legally have" @click="moveFlags(f, $event)">
                      <span class="ic" style="--c: var(--dock-green)"><t-icon :name="flagInfo(f).icon"></t-icon></span><span><b>{{ flagInfo(f).name }}</b><small>{{ flagInfo(f).desc }}</small></span>
                    </button>
                  </div>
                  <p class="muted flags-note"><t-icon name="info"></t-icon>Opens PKHeX's editor. Shift+click sets every flag it can legally have.</p>
                </div>
                <t-fields :fields="fieldsFor('moves')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
                <div v-if="e.hasRelearn" class="form-grid" style="margin-top:18px">
                  <div class="section-title">Relearn moves</div>
                  <div class="field" v-for="(r, i) in e.relearn" :key="'r' + i"><label>Relearn {{ i + 1 }}</label><t-combo class="move-combo" :model-value="r" :options="relearnOptions" :img-for="moveTypeImg" @update:model-value="v => set('relearn.' + i, v)">
                    <template #option="{ o }"><img v-if="o.type >= 0 && o.v" class="type-ic" :src="typeIcon(o.type)" alt=""><span v-if="o.cat" class="mcat mini" :class="o.cat" :title="catName(o.cat)"><t-icon :name="o.cat"></t-icon></span><span>{{ o.t }}</span></template>
                  </t-combo></div>
                  <div style="grid-column:1/-1"><button class="btn small" @click="suggest('relearn')"><t-icon name="wand"></t-icon>Suggest relearn moves</button></div>
                </div>
              </div>

              <!-- Trainer -->
              <div v-else-if="section === 'trainer'" class="form-grid">
                <div class="section-title">Original trainer</div>
                <div class="field"><label>OT name</label><t-text :model-value="e.ot.name" :maxlength="e.otMax || 12" @update:model-value="v => set('ot.name', v)"></t-text></div>
                <div class="field"><label>OT gender</label><div class="segmented"><button :class="{ on: e.ot.gender === 0 }" @click="set('ot.gender', 0)">♂ Male</button><button :class="{ on: e.ot.gender === 1 }" @click="set('ot.gender', 1)">♀ Female</button></div></div>
                <div class="field"><label>Trainer ID</label><t-text cls="mono" :model-value="e.ot.tid" @update:model-value="v => set('ot.tid', v)"></t-text></div>
                <div class="field" v-if="e.ot.sid !== null"><label>Secret ID</label><t-text cls="mono" :model-value="e.ot.sid" @update:model-value="v => set('ot.sid', v)"></t-text></div>
                <template v-if="e.ht && e.ht.has">
                  <div class="section-title">Current handler</div>
                  <div class="field"><label>Handler name</label><t-text :model-value="e.ht.name" @update:model-value="v => set('ht.name', v)"></t-text></div>
                  <div class="field"><label>Handler gender</label><div class="segmented"><button :class="{ on: e.ht.gender === 0 }" @click="set('ht.gender', 0)">♂</button><button :class="{ on: e.ht.gender === 1 }" @click="set('ht.gender', 1)">♀</button></div></div>
                  <div class="field"><label>Handler friendship</label><t-number :model-value="e.ht.friendship" :min="0" :max="255" @update:model-value="v => set('ht.friendship', v)"></t-number></div>
                </template>
                <div class="section-title">Identifiers</div>
                <div class="field"><label>PID</label><div class="row"><t-text cls="mono" :model-value="e.pid" :maxlength="8" @update:model-value="v => set('pid', v)"></t-text><button class="btn small" title="Reroll PID" @click="suggest('rerollPID')"><t-icon name="refresh"></t-icon></button></div></div>
                <div class="field" v-if="e.hasEC"><label>Encryption constant</label><div class="row"><t-text cls="mono" :model-value="e.ec" :maxlength="8" @update:model-value="v => set('ec', v)"></t-text><button class="btn small" title="Reroll EC" @click="suggest('rerollEC')"><t-icon name="refresh"></t-icon></button></div></div>
                <t-fields :fields="fieldsFor('trainer')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
              </div>

              <!-- Extras -->
              <div v-else-if="section === 'extras'" class="tools">
                <t-fields :fields="fieldsFor('extras')" :move-options="moveOptions" :img-for="moveTypeImg" @set="setField" @suggest="suggestField"></t-fields>
                <p v-if="e.editors?.length" class="muted" style="margin:0">These open PKHeX's detailed editors, styled to match.</p>
                <div v-if="e.editors?.length" class="tool-grid">
                  <button v-if="e.editors.includes('ribbons')" class="tool" @click="classic('ribbons')"><span class="ic" style="--c: var(--dock-orange)"><t-icon name="flag"></t-icon></span><span><b>Ribbons</b><small>{{ e.ribbonCount ?? 0 }} ribbons</small></span></button>
                  <button v-if="e.editors.includes('memories')" class="tool" @click="classic('memories')"><span class="ic" style="--c: var(--dock-red)"><t-icon name="book"></t-icon></span><span><b>Memories</b><small>Trainer and handler memories</small></span></button>
                  <button v-if="e.editors.includes('medals')" class="tool" @click="classic('medals')"><span class="ic" style="--c: var(--dock-blue)"><t-icon name="star"></t-icon></span><span><b>Medals & training</b><small>Super Training records</small></span></button>
                </div>
              </div>
            </div>
          </section>
        </div>
      </div>`,
  };
})();
