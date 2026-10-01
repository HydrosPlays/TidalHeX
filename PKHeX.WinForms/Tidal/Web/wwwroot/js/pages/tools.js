// Save tools: every sub-editor available for the loaded save, grouped by category.
(() => {
  const ICONS = { Trainer: ['user', 'var(--dock-red)'], 'Pokédex & Items': ['bag', 'var(--dock-orange)'], Storage: ['boxes', 'var(--dock-blue)'], Events: ['flag', 'var(--dock-green)'], World: ['globe', 'var(--dock-blue)'], Battle: ['sword', 'var(--dock-red)'], Data: ['data', 'var(--dock-grey)'] };
  window.TidalPages.tools = {
    props: { store: Object },
    data: () => ({ tools: [], loading: true, query: '', active: null }),
    watch: {
      // A different save was loaded: open editors belong to the old one, and the tool list changes.
      async 'store.saveEpoch'() {
        if (this.active) this.closeTool();
        this.tools = await this.store.call('tools.list').catch(() => []);
      },
    },
    computed: {
      groups() {
        const q = this.query.trim().toLowerCase();
        const map = new Map();
        for (const t of this.tools) {
          if (q && !`${t.name} ${t.description} ${t.category}`.toLowerCase().includes(q)) continue;
          if (!map.has(t.category)) map.set(t.category, []);
          map.get(t.category).push(t);
        }
        return [...map.entries()].map(([name, items]) => ({ name, items, icon: (ICONS[name] ?? ['tools'])[0], color: (ICONS[name] ?? [0, 'var(--dock-grey)'])[1] }));
      },
    },
    async mounted() {
      this.setHints();
      this.tools = await this.store.call('tools.list').catch(() => []);
      this.loading = false;
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.tool'), { stagger: 20, y: 14 }));
    },
    methods: {
      setHints() {
        this.store.note = this.store.save?.game ? `Editors for ${this.store.save.game}` : '';
        this.store.hints = [{ glyph: '+', label: 'Settings', run: () => this.store.go('settings') }];
      },
      /** The in-page editor for a tool (the host decides, per game); null opens PKHeX's classic window. */
      webTool(t) { return t.web && window.TidalTools?.[t.web] ? t.web : null; },
      async open(t) {
        if (this.webTool(t)) {
          this.active = t;
          this.store.leaveGuard = () => this.$refs.tool?.confirmLeave?.() ?? Promise.resolve(true);
          return;
        }
        const ok = await this.store.call('tools.open', { id: t.id }).catch(() => false);
        if (ok) this.store.bumpSprites();
      },
      closeTool() {
        this.active = null;
        this.store.leaveGuard = null;
        this.setHints();
        this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.tool'), { stagger: 10, y: 10 }));
      },
      /** B / Backspace / Esc: an open tool closes first (it asks about unsaved changes). */
      back() {
        if (!this.active) return false;
        this.$refs.tool?.requestClose ? this.$refs.tool.requestClose() : this.closeTool();
        return true;
      },
      onKey(e) { this.$refs.tool?.onKey?.(e); },
    },
    beforeUnmount() { this.store.leaveGuard = null; },
    template: `
      <div class="page">
        <component v-if="active" ref="tool" :is="'tool-' + webTool(active)" :store="store" :tool="active" @close="closeTool"></component>
        <div v-else class="tools">
          <div class="row"><input class="input" style="max-width:360px" v-model="query" placeholder="Find a tool…"><span class="muted grow" style="text-align:right">{{ tools.length }} tools for this save</span></div>
          <div v-if="loading" class="empty-state"><div><t-icon name="tools"></t-icon><h3>Loading tools…</h3></div></div>
          <div v-else-if="!groups.length" class="empty-state screen" style="height:auto;padding:40px"><div><t-icon name="tools"></t-icon><h3>No tools found</h3></div></div>
          <section v-for="g in groups" :key="g.name">
            <h4 class="card-title">{{ g.name }}</h4>
            <div class="tool-grid">
              <button v-for="t in g.items" :key="t.id" class="tool" :class="{ web: webTool(t) }" @click="open(t)">
                <span class="ic" :style="{ '--c': g.color }"><t-icon :name="g.icon"></t-icon></span>
                <span><b>{{ t.name }}</b><small>{{ t.description }}</small></span>
                <span v-if="!webTool(t)" class="classic-mark" title="Opens PKHeX's classic editor window"><t-icon name="classic"></t-icon></span>
              </button>
            </div>
          </section>
        </div>
      </div>`,
  };
})();
