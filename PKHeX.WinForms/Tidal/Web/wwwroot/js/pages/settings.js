// Settings: quick toggles here; PKHeX's full settings open in the classic dialog.
window.TidalPages.settings = {
  props: { store: Object },
  mounted() {
    this.store.note = '';
    this.store.hints = [];
    window.TidalFx.enter(this.$el.querySelectorAll('.set-row'), { stagger: 50, y: 12 });
  },
  data: () => ({ themes: [{ id: 'light', name: 'Tidal Light (Default)' }, { id: 'dark', name: 'Tidal Dark' }, { id: 'pss', name: 'Tidal PSS' }, { id: 'za', name: 'Tidal ZA' }, { id: 'pixel', name: 'Tidal Pixel' }, { id: 'arceus', name: 'Tidal Arceus' }], checking: false }),
  methods: {
    async check() {
      this.checking = true;
      try { await this.store.checkForUpdate(); }
      finally { this.checking = false; }
    },
    motion(v) {
      this.store.settings.reducedMotion = v;
      window.TidalFx.setReduced(v);
      this.store.setOption('reducedMotion', v);
    },
  },
  template: `
    <div class="page">
      <div class="settings screen">
        <div class="settings-about">
          <img src="img/logo.png" alt="">
          <div>
            <h2>TidalHeX</h2>
            <div class="muted">Version {{ store.tidalVersion }} · Built on PKHeX {{ store.version }}</div>
          </div>
        </div>

        <div class="settings-list">
          <div class="set-row theme-row">
            <span class="ic" style="--c: var(--cyan)"><t-icon name="theme"></t-icon></span>
            <div class="set-text"><b>Theme</b><small>How TidalHeX looks. PKHeX's classic windows keep the ocean theme.</small></div>
            <div class="theme-picker" role="radiogroup" aria-label="Theme">
              <button v-for="t in themes" :key="t.id" class="theme-opt" :class="{ on: store.settings.theme === t.id }" role="radio" :aria-checked="store.settings.theme === t.id" @click="store.setTheme(t.id)">
                <span class="theme-swatch" :class="t.id"><i></i><i></i><i></i></span>
                <span>{{ t.name }}</span>
                <t-icon v-if="store.settings.theme === t.id" name="check"></t-icon>
              </button>
            </div>
          </div>
          <div class="set-row">
            <span class="ic" style="--c: var(--cyan)"><t-icon name="sparkle"></t-icon></span>
            <div class="set-text"><b>Reduce motion</b><small>Turns off the bubbles and animations.</small></div>
            <t-switch :model-value="store.settings.reducedMotion" @update:model-value="motion"></t-switch>
          </div>
          <div class="set-row">
            <span class="ic" style="--c: var(--dock-green)"><t-icon name="refresh"></t-icon></span>
            <div class="set-text"><b>Updates</b><small>Check GitHub for a new TidalHeX release when it starts. You're on {{ store.tidalVersion }}.</small></div>
            <t-switch :model-value="store.settings.checkForUpdates" @update:model-value="v => { store.settings.checkForUpdates = v; store.setOption('checkForUpdates', v); }"></t-switch>
            <button class="btn" :disabled="checking" @click="check"><t-icon name="refresh"></t-icon>{{ checking ? 'Checking…' : 'Check now' }}</button>
          </div>
          <div class="set-row">
            <span class="ic" style="--c: var(--dock-orange)"><t-icon name="settings"></t-icon></span>
            <div class="set-text"><b>PKHeX settings</b><small>Legality, sprites, backups, privacy and every other PKHeX option.</small></div>
            <button class="btn primary" @click="store.settingsDialog()"><t-icon name="settings"></t-icon>Open settings</button>
          </div>
          <div class="set-row">
            <span class="ic" style="--c: var(--dock-blue)"><t-icon name="classic"></t-icon></span>
            <div class="set-text"><b>Classic mode</b><small>Restart in PKHeX's classic window. To come back, click <b>TidalHeX view</b> in its menu bar.</small></div>
            <button class="btn" @click="store.classic()"><t-icon name="classic"></t-icon>Switch to classic</button>
          </div>
        </div>

        <p class="settings-credits muted">Built on PKHeX by Kaphotics and contributors (GPLv3). Pokémon data and sprites © Nintendo / Game Freak / The Pokémon Company.</p>
      </div>
    </div>`,
};
