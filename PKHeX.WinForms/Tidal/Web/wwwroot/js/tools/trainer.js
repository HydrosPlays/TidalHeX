// Trainer Info: the fields every game shares (+ coins and badges where present). Host: Api.Tool.Trainer.cs.
// Game-specific details (map position, options, records...) are in PKHeX's own editor via "More options".
window.TidalTools.trainer = {
  props: { store: Object, tool: Object },
  emits: ['close'],
  data: () => ({ t: null, snapshot: '', busy: false, loading: true }),
  computed: {
    dirty() { return !!this.t && this.snapshot !== this.serialize(); },
    tidMax() { return this.t?.idFormat === 'six' ? 999999 : 65535; },
    sidMax() { return this.t?.idFormat === 'six' ? 4294 : 65535; },
    tidText() { return String(this.t?.tid ?? 0).padStart(this.t?.idFormat === 'six' ? 6 : 5, '0'); },
    sidText() { return String(this.t?.sid ?? 0).padStart(this.t?.idFormat === 'six' ? 4 : 5, '0'); },
    badgeCount() { return (this.t?.badges ?? []).filter(Boolean).length; },
    offsets() { let o = 0; return (this.t?.badgeGroups ?? []).map(g => { const start = o; o += g.names.length; return start; }); },
    time() { const t = this.t; return t ? `${t.hours}:${String(t.minutes).padStart(2, '0')}:${String(t.seconds).padStart(2, '0')}` : ''; },
  },
  async mounted() {
    this.store.note = 'Changes are written to the save when you press Save';
    this.store.hints = [{ glyph: 'S', label: 'Save', run: () => this.save() }];
    await this.load();
  },
  methods: {
    serialize() { return JSON.stringify(this.t); },
    async load() {
      this.loading = true;
      const t = await this.store.call('trainer.get').catch(() => null);
      this.loading = false;
      if (!t) { this.$emit('close'); return; }
      this.t = { ...t, badges: [...t.badges] };
      this.snapshot = this.serialize();
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.trainer-card, .tr-section'), { stagger: 50, y: 12 }));
    },
    setGroup(start, count, on) { for (let i = 0; i < count; i++) this.t.badges[start + i] = on; },
    async save() {
      if (!this.dirty || this.busy) return;
      this.busy = true;
      try {
        const t = this.t;
        await this.store.call('trainer.save', { ot: t.ot, gender: t.gender, tid: t.tid, sid: t.sid, money: t.money, hours: t.hours, minutes: t.minutes, seconds: t.seconds, coins: t.coins, badges: t.badges });
        this.store.toast('Trainer info saved', 'success');
        await this.load();
      } catch { /* toast */ } finally { this.busy = false; }
    },
    async confirmLeave() {
      return !this.dirty || await this.store.confirm('Discard your changes to the trainer info?', { title: 'Unsaved changes' }) === 'yes';
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
    <t-tool title="Trainer Info" :subtitle="t?.game" icon="user" :dirty="dirty" :busy="busy" :loading="loading"
            @save="save" @cancel="requestClose" @classic="classic">
      <div v-if="t" class="trainer">
        <div class="trainer-card" :class="{ female: t.gender === 1 }">
          <div class="avatar-big"><t-icon name="user"></t-icon></div>
          <div class="who">
            <small>Trainer</small>
            <h2>{{ t.ot || '—' }}</h2>
            <div class="ids"><span>ID No. <b>{{ tidText }}</b></span><span v-if="t.idFormat !== 'single'">SID <b>{{ sidText }}</b></span></div>
          </div>
          <dl>
            <div><dt>Money</dt><dd>₽ {{ t.money.toLocaleString() }}</dd></div>
            <div><dt>Play time</dt><dd>{{ time }}</dd></div>
            <div v-if="t.badgeGroups.length"><dt>Badges</dt><dd>{{ badgeCount }} / {{ t.badges.length }}</dd></div>
            <div v-if="t.coins != null"><dt>Coins</dt><dd>{{ t.coins.toLocaleString() }}</dd></div>
          </dl>
        </div>

        <div class="tr-grid">
          <section class="tr-section">
            <h4 class="card-title">Identity</h4>
            <div class="field"><label>Name</label><input class="input" v-model="t.ot" :maxlength="t.otMaxLength" spellcheck="false"></div>
            <div v-if="t.gender != null" class="field"><label>Gender</label>
              <div class="segmented"><button :class="{ on: t.gender === 0 }" @click="t.gender = 0">♂ Male</button><button :class="{ on: t.gender === 1 }" @click="t.gender = 1">♀ Female</button></div>
            </div>
            <div class="tr-pair">
              <div class="field"><label>Trainer ID{{ t.idFormat === 'six' ? ' (6 digits)' : '' }}</label><t-number v-model="t.tid" :min="0" :max="tidMax"></t-number></div>
              <div v-if="t.idFormat !== 'single'" class="field"><label>Secret ID</label><t-number v-model="t.sid" :min="0" :max="sidMax"></t-number></div>
            </div>
          </section>

          <section class="tr-section">
            <h4 class="card-title">Money &amp; time</h4>
            <div class="field"><label>Money</label>
              <div class="tr-with-btn"><t-number v-model="t.money" :min="0" :max="t.maxMoney"></t-number><button class="btn small" @click="t.money = t.maxMoney">Max</button></div>
            </div>
            <div v-if="t.coins != null" class="field"><label>Coins</label>
              <div class="tr-with-btn"><t-number v-model="t.coins" :min="0" :max="t.maxCoins"></t-number><button class="btn small" @click="t.coins = t.maxCoins">Max</button></div>
            </div>
            <div class="field"><label>Play time (hours · minutes · seconds)</label>
              <div class="tr-time"><t-number v-model="t.hours" :min="0" :max="t.maxHours"></t-number><span>:</span><t-number v-model="t.minutes" :min="0" :max="59"></t-number><span>:</span><t-number v-model="t.seconds" :min="0" :max="59"></t-number></div>
            </div>
          </section>

          <section v-if="t.badgeGroups.length" class="tr-section wide">
            <h4 class="card-title">Badges</h4>
            <div v-for="(g, gi) in t.badgeGroups" :key="g.region" class="badge-row">
              <span class="region">{{ g.region }}</span>
              <div class="badges">
                <button v-for="(n, i) in g.names" :key="n" class="badge" :class="{ on: t.badges[offsets[gi] + i] }" @click="t.badges[offsets[gi] + i] = !t.badges[offsets[gi] + i]">
                  <span class="gem"></span>{{ n }}
                </button>
              </div>
              <button class="btn small" @click="setGroup(offsets[gi], g.names.length, true)">All</button>
              <button class="btn small" @click="setGroup(offsets[gi], g.names.length, false)">None</button>
            </div>
          </section>
        </div>

        <p class="muted tr-note"><t-icon name="info"></t-icon>Map position, game options, records and other game-specific details are under <b>More options</b>.</p>
      </div>
    </t-tool>`,
};
