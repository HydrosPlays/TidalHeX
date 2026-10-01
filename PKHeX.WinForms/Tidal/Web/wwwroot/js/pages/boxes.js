// Boxes: storage grid, party, slot details, drag & drop.
window.TidalPages.boxes = {
  props: { store: Object },
  data: () => ({ box: null, party: [], sel: null, loading: false, drag: null, dropAt: null }),
  computed: {
    save() { return this.store.save; },
    cols() { const n = this.save?.slotsPerBox ?? 30; return n % 6 === 0 ? 6 : n / 5; },
    boxIndex() { return this.store.box; },
    boxCount() { return this.save?.boxCount ?? 0; },
    selected() {
      if (!this.sel) return null;
      const list = this.sel.box < 0 ? this.party : this.box?.slots ?? [];
      return list[this.sel.slot] ?? null;
    },
  },
  watch: {
    boxIndex() { this.load(); },
    'store.spriteVersion'() { this.load(); },
  },
  async mounted() {
    this.setHints();
    await this.load();
    window.TidalFx.enter(this.$el.querySelectorAll('.slot'), { stagger: 12, y: 12, scale: 0.9 });
  },
  methods: {
    async load() {
      if (!this.save?.hasBox && !this.save?.hasParty) return;
      this.loading = true;
      try {
        const [box, party] = await Promise.all([
          this.save.hasBox ? this.store.call('box.get', { box: this.boxIndex }) : Promise.resolve(null),
          this.save.hasParty ? this.store.call('box.party') : Promise.resolve({ slots: [] }),
        ]);
        this.box = box; this.party = party.slots;
      } finally { this.loading = false; }
    },
    setHints() {
      this.store.note = this.store.placing ? 'Click a slot to place the Pokémon from the editor' : 'Drag to move · Shift+drag to clone · Alt+drag to overwrite';
      this.store.hints = [
        { glyph: 'X', label: 'Export save', run: () => this.store.exportSave() },
        { glyph: 'Y', label: 'Legality', run: () => this.legality() },
        { glyph: 'A', label: 'Edit', run: () => this.edit() },
      ];
    },
    go(delta) {
      if (!this.boxCount) return;
      this.store.box = (this.boxIndex + delta + this.boxCount) % this.boxCount;
      this.sel = null;
      this.$nextTick(() => window.TidalFx.enter(this.$el.querySelectorAll('.box-stage .slot'), { stagger: 8, y: 0, scale: 0.92, duration: 400 }));
    },
    wheel(e) { if (this.drag) return; this.go(e.deltaY > 0 ? 1 : -1); },
    select(ref) { this.sel = ref; },
    async edit(ref = this.sel) {
      const s = ref && (ref.box < 0 ? this.party : this.box?.slots ?? [])[ref.slot];
      if (!s || s.empty) return;
      const state = await this.store.call('box.view', { slot: { box: ref.box, slot: ref.slot } }).catch(() => null);
      if (state) this.store.loadEditor(state);
    },
    async setFromEditor(ref = this.sel) {
      if (!ref) return;
      if (await this.store.call('box.set', { slot: { box: ref.box, slot: ref.slot } }).catch(() => false)) { this.store.toast('Saved the editor Pokémon here', 'success'); await this.load(); }
    },
    async remove(ref = this.sel) {
      if (!ref || !this.selected || this.selected.empty) return;
      if (await this.store.call('box.delete', { slot: { box: ref.box, slot: ref.slot } }).catch(() => false)) await this.load();
    },
    async legality(ref = this.sel) {
      if (!ref || !this.selected || this.selected.empty) return;
      const r = await this.store.call('box.legality', { slot: { box: ref.box, slot: ref.slot } }).catch(() => null);
      if (r) this.store.report(r.valid ? 'Legal' : 'Legality issues', r.report);
    },
    async exportFile(ref = this.sel) {
      if (!ref || !this.selected || this.selected.empty) return;
      await this.store.call('box.exportFile', { slot: { box: ref.box, slot: ref.slot } }).catch(() => {});
    },
    menu(e, ref, slot) {
      this.select(ref);
      const items = [];
      if (!slot.empty) items.push({ icon: 'edit', label: 'Edit', kbd: 'Enter', run: () => this.edit(ref) });
      items.push({ icon: 'save', label: 'Set from editor', run: () => this.setFromEditor(ref) });
      if (!slot.empty) {
        items.push({ icon: 'shield', label: 'Legality report', run: () => this.legality(ref) });
        items.push({ icon: 'file', label: 'Export file…', run: () => this.exportFile(ref) });
        items.push('-');
        items.push({ icon: 'trash', label: 'Delete', kbd: 'Del', run: () => this.remove(ref) });
      }
      this.store.menu(e, items);
    },

    // ---------- drag & drop (pointer based so it works everywhere in WebView2)
    down(e, ref, slot) {
      if (e.button !== 0) return;
      if (this.store.placing) { this.store.placing = false; this.setHints(); this.select(ref); this.setFromEditor(ref); return; }
      if (slot.empty) { this.select(ref); return; }
      this.drag = { ref, slot, x: e.clientX, y: e.clientY, active: false, ghost: null };
      window.addEventListener('pointermove', this.move);
      window.addEventListener('pointerup', this.up, { once: true });
    },
    move(e) {
      const d = this.drag; if (!d) return;
      if (!d.active && Math.hypot(e.clientX - d.x, e.clientY - d.y) > 6) {
        d.active = true;
        const g = document.createElement('img');
        g.src = d.slot.sprite; g.className = 'drag-ghost sprite'; document.body.appendChild(g); d.ghost = g;
        window.anime?.animate(g, { scale: { from: 0.6, to: 1.15 }, rotate: { from: 0, to: -6 }, duration: 260, ease: 'outBack' });
      }
      if (!d.active) return;
      d.ghost.style.left = (e.clientX - 60) + 'px'; d.ghost.style.top = (e.clientY - 55) + 'px';
      const target = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-slot]');
      this.dropAt = target ? JSON.parse(target.dataset.slot) : null;
    },
    async up(e) {
      window.removeEventListener('pointermove', this.move);
      const d = this.drag; this.drag = null;
      const to = this.dropAt; this.dropAt = null;
      if (!d) return;
      d.ghost?.remove();
      if (!d.active) { this.select(d.ref); return; }
      if (!to || (to.box === d.ref.box && to.slot === d.ref.slot)) return;
      const mode = e.shiftKey ? 'clone' : e.altKey ? 'overwrite' : 'move';
      const ok = await this.store.call('box.move', { from: d.ref, to, mode }).catch(() => false);
      if (ok) { this.sel = to; await this.load(); }
    },
    isDrop(ref) { return this.dropAt && this.dropAt.box === ref.box && this.dropAt.slot === ref.slot; },
    isSel(ref) { return this.sel && this.sel.box === ref.box && this.sel.slot === ref.slot; },
    refOf(s) { return { box: s.box, slot: s.slot }; },

    back() {
      if (this.store.placing) { this.store.placing = false; this.setHints(); return true; }
      if (this.sel) { this.sel = null; return true; }
      return false;
    },
    onKey(e) {
      const count = this.box?.slots.length ?? 0;
      if ((e.ctrlKey && e.key.toLowerCase() === 'z')) { e.preventDefault(); this.store.call('box.undo').then(() => this.load()).catch(() => {}); return; }
      if ((e.ctrlKey && e.key.toLowerCase() === 'y')) { e.preventDefault(); this.store.call('box.redo').then(() => this.load()).catch(() => {}); return; }
      if (e.key === 'PageDown') { this.go(1); return; }
      if (e.key === 'PageUp') { this.go(-1); return; }
      if (!this.sel) { if (e.key.startsWith('Arrow') && count) this.sel = { box: this.boxIndex, slot: 0 }; return; }
      if (this.sel.box < 0) {
        if (e.key === 'Enter') this.edit();
        return;
      }
      const c = this.cols, i = this.sel.slot;
      const moveTo = j => { if (j >= 0 && j < count) this.sel = { box: this.boxIndex, slot: j }; e.preventDefault(); };
      if (e.key === 'ArrowRight') moveTo(i + 1);
      else if (e.key === 'ArrowLeft') moveTo(i - 1);
      else if (e.key === 'ArrowDown') moveTo(i + c);
      else if (e.key === 'ArrowUp') moveTo(i - c);
      else if (e.key === 'Enter' || e.key.toLowerCase() === 'a') this.edit();
      else if (e.key === 'Delete') this.remove();
      else if (e.key.toLowerCase() === 'y') this.legality();
    },
    isFileDrop(s) { return this.store.dropSlot != null && this.store.dropSlot === JSON.stringify(this.refOf(s)); },
    genderSym(g) { return g === 0 ? '♂' : g === 1 ? '♀' : ''; },
  },
  template: `
    <div class="page">
      <div v-if="!save?.hasBox && !save?.hasParty" class="empty-state screen"><div><t-icon name="boxes"></t-icon><h2>No storage in this save</h2><p>Open a save file to see its boxes.</p></div></div>
      <div v-else class="boxes">
        <section class="box-panel screen" v-if="save?.hasBox">
          <div class="box-head">
            <button class="btn small" @click="go(-1)"><t-icon name="back"></t-icon></button>
            <h2 :title="'Box ' + (boxIndex + 1) + ' of ' + boxCount">{{ box?.name ?? '…' }}</h2>
            <button class="btn small" @click="go(1)"><t-icon name="next"></t-icon></button>
            <div class="box-dots"><i v-for="n in boxCount" :key="n" :class="{ on: n - 1 === boxIndex }" :title="'Box ' + n" @click="store.box = n - 1"></i></div>
          </div>
          <div class="box-stage" @wheel.prevent="wheel">
            <div class="wall" v-if="box?.wallpaper" :style="{ backgroundImage: 'url(' + box.wallpaper + ')' }"></div>
            <div class="slot-grid" :style="{ gridTemplateColumns: 'repeat(' + cols + ', 1fr)' }">
              <div v-for="s in box?.slots ?? []" :key="s.slot" class="slot" :data-slot="JSON.stringify(refOf(s))"
                   :class="{ sel: isSel(s), drop: isDrop(s) || isFileDrop(s), dragging: drag?.active && drag.ref.box === s.box && drag.ref.slot === s.slot }"
                   @pointerdown="down($event, refOf(s), s)" @dblclick="edit(refOf(s))" @contextmenu.prevent="menu($event, refOf(s), s)">
                <img v-if="!s.empty" class="sprite" :src="s.sprite" alt="" draggable="false">
                <span v-if="s.legal === false" class="mark illegal" title="Legality issues"></span>
                <span v-if="s.locked" class="mark lock" title="Locked slot">🔒</span>
                <span v-if="!s.empty" class="lv">Lv. {{ s.level }}</span>
              </div>
            </div>
          </div>
        </section>

        <aside class="side">
          <section class="party screen" v-if="save?.hasParty">
            <h4 class="card-title">Party</h4>
            <div class="party-grid">
              <div v-for="s in party" :key="s.slot" class="slot" :data-slot="JSON.stringify(refOf(s))"
                   :class="{ sel: isSel(s), drop: isDrop(s) || isFileDrop(s) }"
                   @pointerdown="down($event, refOf(s), s)" @dblclick="edit(refOf(s))" @contextmenu.prevent="menu($event, refOf(s), s)">
                <img v-if="!s.empty" class="sprite" :src="s.sprite" alt="" draggable="false">
                <span v-if="s.legal === false" class="mark illegal"></span>
                <span v-if="!s.empty" class="lv">Lv. {{ s.level }}</span>
              </div>
            </div>
          </section>

          <section class="detail screen">
            <template v-if="selected && !selected.empty">
              <div class="hero-mini">
                <img class="sprite" :src="selected.sprite" alt="">
                <div>
                  <h3>{{ selected.nickname || selected.name }}</h3>
                  <div class="muted" v-if="selected.nickname && selected.nickname !== selected.name">{{ selected.name }}</div>
                  <div class="row" style="margin-top:6px;flex-wrap:wrap;gap:6px">
                    <span class="pill solid">Lv. {{ selected.level }}</span>
                    <span v-if="selected.gender === 0" class="pill male">♂</span>
                    <span v-if="selected.gender === 1" class="pill female">♀</span>
                    <span v-if="selected.shiny" class="pill shiny">★ Shiny</span>
                    <span v-if="selected.egg" class="pill warn">Egg</span>
                  </div>
                </div>
              </div>
              <dl class="rows">
                <dt>Location</dt><dd>{{ selected.box < 0 ? 'Party slot ' + (selected.slot + 1) : (box?.name + ' · slot ' + (selected.slot + 1)) }}</dd>
                <dt>Held item</dt><dd>{{ selected.heldItem || '—' }}</dd>
                <dt>Legality</dt><dd><span v-if="selected.legal === true" class="pill good">Legal</span><span v-else-if="selected.legal === false" class="pill bad">Issues</span><span v-else class="muted">Not checked</span></dd>
              </dl>
              <div class="actions">
                <button class="btn primary" @click="edit()"><t-icon name="edit"></t-icon>Edit</button>
                <button class="btn" @click="legality()"><t-icon name="shield"></t-icon>Legality</button>
                <button class="btn" @click="exportFile()"><t-icon name="file"></t-icon>Export</button>
                <button class="btn" @click="setFromEditor()"><t-icon name="save"></t-icon>Set here</button>
                <button class="btn danger" @click="remove()"><t-icon name="trash"></t-icon>Delete</button>
              </div>
            </template>
            <template v-else-if="sel">
              <div class="empty-state"><div><t-icon name="plus"></t-icon><h3>Empty slot</h3><p>Drag a Pokémon here, or place the one in the editor.</p>
                <button class="btn primary" style="margin-top:10px" @click="setFromEditor()"><t-icon name="save"></t-icon>Set from editor</button></div></div>
            </template>
            <div v-else class="empty-state"><div><t-icon name="pokeball"></t-icon><h3>Select a Pokémon</h3><p>Click a slot to see details.<br>Double-click to open it in the editor.</p></div></div>
          </section>
        </aside>
      </div>
    </div>`,
};
