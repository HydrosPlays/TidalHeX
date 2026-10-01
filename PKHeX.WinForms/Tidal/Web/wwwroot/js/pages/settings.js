// Settings: quick toggles here; PKHeX's full settings open in the classic dialog.
window.TidalPages.settings = {
  props: { store: Object },
  mounted() {
    this.store.note = '';
    this.store.hints = [];
    window.TidalFx.enter(this.$el.querySelectorAll('.screen'), { stagger: 60, y: 16 });
  },
  methods: {
    motion(v) {
      this.store.settings.reducedMotion = v;
      window.TidalFx.setReduced(v);
      this.store.call('app.setSetting', { name: 'reducedMotion', value: v }).catch(() => {});
    },
  },
  template: `
    <div class="page">
      <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:20px;align-items:start">
        <section class="screen" style="padding:22px">
          <h4 class="card-title">Appearance</h4>
          <div style="display:flex;flex-direction:column;gap:16px">
            <t-switch :model-value="store.settings.reducedMotion" @update:model-value="motion" label="Reduce motion (no bubbles or animations)"></t-switch>
          </div>
        </section>
        <section class="screen" style="padding:22px">
          <h4 class="card-title">PKHeX settings</h4>
          <p class="muted" style="margin-top:0">Legality, sprites, backups, privacy, hover and every other option.</p>
          <button class="btn primary" @click="store.settingsDialog()"><t-icon name="settings"></t-icon>Open all settings</button>
        </section>
        <section class="screen" style="padding:22px">
          <h4 class="card-title">Classic mode</h4>
          <p class="muted" style="margin-top:0">Restart in the classic PKHeX window, e.g. to use plugins such as Auto-Legality Mod. To come back, click <b>TidalHeX view</b> in its menu bar.</p>
          <button class="btn" @click="store.classic()"><t-icon name="classic"></t-icon>Switch to classic PKHeX</button>
        </section>
        <section class="screen" style="padding:22px">
          <h4 class="card-title">About</h4>
          <div class="row" style="align-items:center;gap:14px"><img src="img/logo.png" style="width:64px;height:64px" alt=""><div><b style="font-size:18px">TidalHeX</b><div class="muted">{{ store.version }}</div></div></div>
          <p class="muted">Built on PKHeX by Kaphotics and contributors (GPLv3). Pokémon data and sprites © Nintendo / Game Freak / The Pokémon Company.</p>
        </section>
      </div>
    </div>`,
};
