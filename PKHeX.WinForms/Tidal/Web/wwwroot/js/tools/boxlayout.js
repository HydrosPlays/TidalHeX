// Box Layout: names, wallpapers, order, unlocked count and flags. Host: Api.Tool.BoxLayout.cs (port of SAV_BoxLayout).
window.TidalTools.boxlayout = {
  props: { store: Object, tool: Object },
  emits: ['close'],
  data: () => ({ d: null, boxes: [], sel: 0, unlocked: null, flags: [], snapshot: '', busy: false, loading: true, dragFrom: -1, dragOver: -1 }),
  computed: {
    dirty() { return !!this.d && this.snapshot !== this.serialize(); },
    box() { return this.boxes[this.sel] ?? null; },
    host() { return window.Tidal.isHost; },
    canUp() { return this.sel > 0 && !this.box?.locked && !this.boxes[this.sel - 1]?.locked; },
    canDown() { return this.sel < this.boxes.length - 1 && !this.box?.locked && !this.boxes[this.sel + 1]?.locked; },
  },
  async mounted() {
    this.store.note = 'Changes are written to the save when you press Save';
    this.store.hints = [{ glyph: 'S', label: 'Save', run: () => this.save() }];
    await this.load();
  },
  methods: {
    serialize() { return JSON.stringify([this.boxes.map(b => [b.index, b.name, b.wallpaper]), this.unlocked, this.flags]); },
    async load() {
      this.loading = true;
      const d = await this.store.call('boxlayout.get').catch(() => null);
      this.loading = false;
      if (!d) { this.$emit('close'); return; }
      this.d = d;
      this.boxes = d.boxes.map(b => ({ ...b }));
      this.unlocked = d.unlocked;
      this.flags = [...d.flags];
      this.sel = Math.min(this.store.box ?? 0, Math.max(0, this.boxes.length - 1));
      this.snapshot = this.serialize();
      this.$nextTick(() => {
        this.$el.querySelector('.bl-item.on')?.scrollIntoView({ block: 'center' });
        window.TidalFx.enter(this.$el.querySelectorAll('.bl-detail > *'), { stagger: 40, y: 10 });
      });
    },
    // pos: where the box sits; only the save's own picture ("My Wallpaper") differs by it (left or right half).
    wallpaperUrl(w, pos = 0) {
      if (!this.host || w < 0) return null;
      const half = w === this.d?.pictureWallpaper ? `&box=${pos % 2}` : '';
      return `/wallpaper/choice/${w}?g=${this.store.save?.version ?? 0}${half}`;
    },
    move(dir) {
      const i = this.sel, j = i + dir;
      if (dir < 0 ? !this.canUp : !this.canDown) return;
      [this.boxes[i], this.boxes[j]] = [this.boxes[j], this.boxes[i]];
      this.sel = j;
    },
    // Drag to reorder (moves through the same adjacent swaps as the buttons; locked boxes can't pass).
    dragStart(i, e) { if (this.boxes[i].locked) { e.preventDefault(); return; } this.dragFrom = i; e.dataTransfer.effectAllowed = 'move'; },
    dragEnter(i) { if (this.dragFrom >= 0) this.dragOver = i; },
    drop(i) {
      const from = this.dragFrom;
      this.dragFrom = this.dragOver = -1;
      if (from < 0 || from === i) return;
      const lo = Math.min(from, i), hi = Math.max(from, i);
      if (this.boxes.slice(lo, hi + 1).some(b => b.locked)) { this.store.toast('Boxes with locked or team slots can’t be moved.', 'warn'); return; }
      const [b] = this.boxes.splice(from, 1);
      this.boxes.splice(i, 0, b);
      this.sel = i;
    },
    async save() {
      if (!this.dirty || this.busy) return;
      this.busy = true;
      try {
        await this.store.call('boxlayout.save', {
          order: this.boxes.map(b => b.index), names: this.boxes.map(b => b.name), wallpapers: this.boxes.map(b => b.wallpaper),
          unlocked: this.unlocked, flags: this.flags.length ? this.flags : null,
        });
        this.store.toast('Box layout saved', 'success');
        this.store.bumpSprites();
        await this.load();
      } catch { /* toast */ } finally { this.busy = false; }
    },
    async confirmLeave() {
      return !this.dirty || await this.store.confirm('Discard your changes to the box layout?', { title: 'Unsaved changes' }) === 'yes';
    },
    async requestClose() { if (await this.confirmLeave()) this.$emit('close'); },
    async classic() {
      if (this.dirty && await this.store.confirm('Open the classic editor? Your unsaved changes here will be discarded.', { title: 'More options' }) !== 'yes') return;
      await this.store.call('tools.open', { id: this.tool.id }).catch(() => {});
      this.store.bumpSprites();
      await this.load();
    },
    onKey(e) {
      if (e.key.toLowerCase() === 's' && !e.ctrlKey) { e.preventDefault(); this.save(); }
      else if (e.key === 'ArrowUp' && e.altKey) { e.preventDefault(); this.move(-1); }
      else if (e.key === 'ArrowDown' && e.altKey) { e.preventDefault(); this.move(1); }
      else if (e.key === 'ArrowUp' && this.sel > 0) { e.preventDefault(); this.sel--; }
      else if (e.key === 'ArrowDown' && this.sel < this.boxes.length - 1) { e.preventDefault(); this.sel++; }
    },
  },
  template: `
    <t-tool title="Box Layout" :subtitle="d ? boxes.length + ' boxes · ' + (store.save?.game ?? '') : ''" icon="boxes" :dirty="dirty" :busy="busy" :loading="loading"
            @save="save" @cancel="requestClose" @classic="classic">
      <div v-if="d" class="boxlayout">
        <nav class="bl-list">
          <div v-for="(b, i) in boxes" :key="b.index" class="bl-item" :class="{ on: i === sel, over: i === dragOver && dragFrom !== i, locked: b.locked }"
               :draggable="!b.locked" @click="sel = i" @dragstart="dragStart(i, $event)" @dragenter.prevent="dragEnter(i)" @dragover.prevent @drop.prevent="drop(i)" @dragend="dragFrom = dragOver = -1">
            <span class="num">{{ i + 1 }}</span>
            <span class="thumb"><img v-if="wallpaperUrl(b.wallpaper, i)" :src="wallpaperUrl(b.wallpaper, i)" alt=""></span>
            <span class="nm">{{ b.name || '(no name)' }}</span>
            <span class="cnt" :title="b.count + ' Pokémon'">{{ b.count }}/{{ d.slotsPerBox }}</span>
            <t-icon v-if="b.locked" name="shield" title="Has locked or team slots: can't be moved"></t-icon>
          </div>
        </nav>

        <section v-if="box" class="bl-detail">
          <div class="bl-preview">
            <img v-if="wallpaperUrl(box.wallpaper, sel)" :src="wallpaperUrl(box.wallpaper, sel)" alt="">
            <div class="bl-title"><button class="btn small" :disabled="sel === 0" @click="sel--"><t-icon name="back"></t-icon></button><span>{{ box.name || '(no name)' }}</span><button class="btn small" :disabled="sel === boxes.length - 1" @click="sel++"><t-icon name="next"></t-icon></button></div>
          </div>

          <div class="bl-row">
            <div class="field grow"><label>Box name</label><input class="input" v-model="box.name" :maxlength="d.nameMaxLength" :disabled="!d.canRename" spellcheck="false"></div>
            <div class="field"><label>Position</label>
              <div class="row"><button class="btn" :disabled="!canUp" title="Move up (Alt+↑)" @click="move(-1)"><t-icon name="up"></t-icon>Up</button><button class="btn" :disabled="!canDown" title="Move down (Alt+↓)" @click="move(1)"><t-icon name="down"></t-icon>Down</button></div>
            </div>
          </div>
          <p v-if="box.locked" class="muted bl-note"><t-icon name="shield"></t-icon>This box has locked or team slots, so it can't be moved.</p>

          <div v-if="d.wallpapers.length" class="field"><label>Wallpaper{{ d.fixedWallpaper ? ' (this game shows one scene for every box)' : '' }}</label>
            <div class="wp-grid">
              <button v-for="(w, i) in d.wallpapers" :key="i" class="wp" :class="{ on: box.wallpaper === i }" :title="w" @click="box.wallpaper = i">
                <img v-if="wallpaperUrl(i, sel) && !d.fixedWallpaper" :src="wallpaperUrl(i, sel)" alt="" loading="lazy"><span v-else class="wp-ph">{{ i + 1 }}</span>
                <small>{{ w }}</small>
              </button>
            </div>
          </div>

          <div class="bl-row">
            <div v-if="unlocked != null" class="field"><label>Unlocked boxes</label><t-number v-model="unlocked" :min="0" :max="boxes.length"></t-number></div>
            <details v-if="flags.length" class="bl-flags"><summary>Box flags (advanced)</summary>
              <div class="flags-grid"><label v-for="(f, i) in flags" :key="i"><small>#{{ i }}</small><input class="input mono" :value="f.toString(16).toUpperCase().padStart(2, '0')" maxlength="2" @change="flags[i] = Math.min(255, parseInt($event.target.value, 16) || 0); $event.target.value = flags[i].toString(16).toUpperCase().padStart(2, '0')"></label></div>
            </details>
          </div>
        </section>
      </div>
    </t-tool>`,
};
