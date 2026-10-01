// Shared Vue components for TidalHeX.
window.TidalComponents = (() => {
  const { ref, computed, watch, nextTick, onBeforeUnmount } = Vue;

  const TIcon = {
    props: { name: String },
    template: `<span class="ti" v-html="svg"></span>`,
    computed: { svg() { return window.TidalIcons[this.name] ?? ''; } },
  };

  // Searchable dropdown. options: [{ v, t, img? }]
  const TCombo = {
    props: { modelValue: [Number, String], options: { type: Array, default: () => [] }, placeholder: String, icon: String, disabled: Boolean, imgFor: Function, noIcon: Boolean },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      const open = ref(false), query = ref(''), hi = ref(0), input = ref(null), list = ref(null);
      const pos = ref({ left: 0, top: 0, bottom: null, width: 0, maxHeight: 330 });
      const selected = computed(() => props.options.find(o => o.v === props.modelValue));
      const filtered = computed(() => {
        const q = query.value.trim().toLowerCase();
        const src = props.options;
        if (!q) return src.slice(0, 400);
        const starts = [], contains = [];
        for (const o of src) {
          const t = String(o.t).toLowerCase();
          if (t.startsWith(q)) starts.push(o); else if (t.includes(q)) contains.push(o);
          if (starts.length > 300) break;
        }
        return starts.concat(contains).slice(0, 400);
      });
      const img = o => (o ? (o.img ?? (props.imgFor ? props.imgFor(o) : null)) : null);
      /** Opens below the box, or above it (anchored to its top edge) when there's more room there. */
      function place() {
        const r = input.value.getBoundingClientRect();
        const below = window.innerHeight - r.bottom, above = r.top;
        const up = below < 250 && above > below;
        const room = (up ? above : below) - 16;
        pos.value = { left: r.left, width: Math.max(r.width, 220), top: up ? null : r.bottom + 6, bottom: up ? window.innerHeight - r.top + 6 : null, maxHeight: Math.max(120, Math.min(330, room)) };
      }
      function show() {
        if (props.disabled || open.value) return;
        place(); open.value = true; query.value = '';
        const idx = filtered.value.findIndex(o => o.v === props.modelValue);
        hi.value = Math.max(0, idx);
        nextTick(() => scrollHi());
      }
      function close() { open.value = false; query.value = ''; }
      function choose(o) { if (o) emit('update:modelValue', o.v); close(); input.value?.blur(); }
      function scrollHi() { const el = list.value?.children[hi.value]; el?.scrollIntoView({ block: 'nearest' }); }
      function key(e) {
        if (!open.value && (e.key === 'ArrowDown' || e.key === 'Enter')) { show(); e.preventDefault(); return; }
        if (!open.value) return;
        if (e.key === 'ArrowDown') { hi.value = Math.min(filtered.value.length - 1, hi.value + 1); scrollHi(); e.preventDefault(); }
        else if (e.key === 'ArrowUp') { hi.value = Math.max(0, hi.value - 1); scrollHi(); e.preventDefault(); }
        else if (e.key === 'Enter') { choose(filtered.value[hi.value]); e.preventDefault(); }
        else if (e.key === 'Escape') { close(); e.stopPropagation(); e.preventDefault(); }
        else if (e.key === 'Tab') close();
      }
      watch(query, () => { hi.value = 0; });
      const outside = e => { if (open.value && !input.value?.parentElement.contains(e.target) && !list.value?.contains(e.target)) close(); };
      document.addEventListener('mousedown', outside, true);
      onBeforeUnmount(() => document.removeEventListener('mousedown', outside, true));
      const display = computed(() => (open.value ? query.value : (selected.value?.t ?? '')));
      return { open, query, hi, input, list, pos, filtered, selected, img, show, close, choose, key, display };
    },
    template: `
      <div class="combo" :class="{ open, 'has-icon': !noIcon && !!img(selected) }">
        <img v-if="!noIcon && img(selected) && !open" class="icon" :src="img(selected)" alt="">
        <input ref="input" class="input" :value="display" :placeholder="open ? (selected?.t ?? 'Search…') : placeholder" :disabled="disabled"
               @focus="show" @click="show" @input="query = $event.target.value; if (!open) show()" @keydown="key">
        <t-icon class="chev" name="chevron"></t-icon>
        <teleport to="body">
          <div v-if="open" class="dropdown" ref="list" :style="{ left: pos.left + 'px', width: pos.width + 'px', top: pos.top != null ? pos.top + 'px' : 'auto', bottom: pos.bottom != null ? pos.bottom + 'px' : 'auto', maxHeight: pos.maxHeight + 'px' }">
            <div v-for="(o, i) in filtered" :key="o.v" class="opt" :class="[o.cls, { hi: i === hi, sel: o.v === modelValue }]" @mousedown.prevent="choose(o)" @mouseenter="hi = i">
              <slot name="option" :o="o"><img v-if="img(o)" :src="img(o)" alt="" loading="lazy"><span>{{ o.t }}</span><span v-if="o.meta" class="meta">{{ o.meta }}</span></slot>
            </div>
            <div v-if="!filtered.length" class="empty">No matches</div>
          </div>
        </teleport>
      </div>`,
  };

  // Game chips: short label and signature color per game (PKHeX GameVersion ids).
  const GAME_INFO = {
    1: ['S', '#3b6fd4'], 2: ['R', '#d8363f'], 3: ['E', '#2f9f5c'], 4: ['FR', '#e8572f'], 5: ['LG', '#58b93a'],
    7: ['HG', '#d7a52c'], 8: ['SS', '#a9b3c6'], 10: ['D', '#5d86d8'], 11: ['P', '#e19ab9'], 12: ['Pt', '#8f98a3'],
    15: ['Colo/XD', '#7a5aa6'], 20: ['W', '#e9eef4'], 21: ['B', '#34373c'], 22: ['W2', '#dde6ee'], 23: ['B2', '#2b3440'],
    24: ['X', '#2e7ed2'], 25: ['Y', '#d5334b'], 26: ['AS', '#2757c0'], 27: ['OR', '#c42c2c'],
    30: ['Sun', '#f4a23b'], 31: ['Moon', '#5b6fd6'], 32: ['US', '#ef7a2b'], 33: ['UM', '#7a40cf'], 34: ['GO', '#35b5e0'],
    35: ['Red', '#d8383a'], 36: ['Blue', '#3b6fd6'], 37: ['Blue JP', '#2f86cf'], 38: ['Yellow', '#f2c231'],
    39: ['Gold', '#c9a03a'], 40: ['Silver', '#b7bfcb'], 41: ['Crystal', '#6fcfdf'], 42: ['LGP', '#f2c231'], 43: ['LGE', '#b9894b'],
    44: ['SW', '#35a2e8'], 45: ['SH', '#d8385b'], 47: ['PLA', '#6a8eaf'], 48: ['BD', '#4a79d8'], 49: ['SP', '#e79ab8'],
    50: ['Scarlet', '#e0482b'], 51: ['Violet', '#8940c7'], 52: ['Z-A', '#3dbf8b'],
    home: ['HOME', '#12a86f'], // Pokémon HOME (gifts claimed in HOME)
  };
  const isLight = hex => { const n = parseInt(hex.slice(1), 16); return (0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255)) > 170; };

  /** Colored chips for the games something is from: short labels on cards, full names with `full`. */
  const TGames = {
    props: { games: { type: Array, default: () => [] }, source: String, full: Boolean, max: { type: Number, default: 4 } },
    setup(props) {
      const all = computed(() => (props.source ? [{ id: 'home', name: props.source }, ...props.games] : props.games));
      const shown = computed(() => (props.full ? all.value : all.value.slice(0, props.max)));
      const more = computed(() => (props.full ? 0 : Math.max(0, all.value.length - props.max)));
      const style = g => { const bg = GAME_INFO[g.id]?.[1] ?? '#5a7fb0'; return { background: bg, color: isLight(bg) ? '#062b66' : '#fff' }; };
      const label = g => (props.full ? g.name : GAME_INFO[g.id]?.[0] ?? g.name);
      return { all, shown, more, style, label };
    },
    template: `<span class="games" :class="{ full }"><span v-for="g in shown" :key="g.id" class="game" :style="style(g)" :title="g.name">{{ label(g) }}</span><span v-if="more" class="game more" :title="all.slice(max).map(g => g.name).join(', ')">+{{ more }}</span></span>`,
  };

  const TSwitch = {
    props: { modelValue: Boolean, label: String },
    emits: ['update:modelValue'],
    template: `<label class="switch" :class="{ on: modelValue }" @click.prevent="$emit('update:modelValue', !modelValue)">
                 <span class="track"><span class="knob"></span></span><span v-if="label">{{ label }}</span></label>`,
  };

  const TTri = {
    props: { modelValue: { default: null } },
    emits: ['update:modelValue'],
    template: `<div class="tri">
      <button :class="{ on: modelValue === null }" @click="$emit('update:modelValue', null)">Any</button>
      <button :class="{ on: modelValue === true }" @click="$emit('update:modelValue', true)">Yes</button>
      <button :class="{ on: modelValue === false }" @click="$emit('update:modelValue', false)">No</button></div>`,
  };

  // Numeric field that commits on change/enter (not on every keystroke).
  const TNumber = {
    props: { modelValue: Number, min: { type: Number, default: 0 }, max: { type: Number, default: 65535 }, cls: String },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      const local = ref(props.modelValue);
      watch(() => props.modelValue, v => { local.value = v; });
      function commit() {
        let v = Math.round(Number(local.value));
        if (!Number.isFinite(v)) v = props.min;
        v = Math.max(props.min, Math.min(props.max, v));
        local.value = v;
        if (v !== props.modelValue) emit('update:modelValue', v);
      }
      function wheel(e) { if (document.activeElement !== e.target) return; e.preventDefault(); local.value = (Number(local.value) || 0) + (e.deltaY < 0 ? 1 : -1); commit(); }
      return { local, commit, wheel };
    },
    template: `<input class="input" :class="cls" type="number" :min="min" :max="max" v-model="local" @change="commit" @keydown.enter="commit" @wheel="wheel">`,
  };

  // Text field that commits on change.
  const TText = {
    props: { modelValue: String, maxlength: Number, cls: String, placeholder: String },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      const local = ref(props.modelValue);
      watch(() => props.modelValue, v => { local.value = v; });
      const commit = () => { if (local.value !== props.modelValue) emit('update:modelValue', local.value ?? ''); };
      return { local, commit };
    },
    template: `<input class="input" :class="cls" :maxlength="maxlength" :placeholder="placeholder" v-model="local" @change="commit" @keydown.enter="commit">`,
  };

  const TModal = {
    props: { store: Object },
    template: `
      <div v-if="store.modal" class="modal-back" @mousedown.self="store.modal = null">
        <div class="modal screen dark">
          <h2>{{ store.modal.title }}</h2>
          <pre v-if="store.modal.text">{{ store.modal.text }}</pre>
          <div class="foot">
            <button v-if="store.modal.copy" class="btn" @click="copy"><t-icon name="copy"></t-icon>Copy</button>
            <button class="btn primary" @click="store.modal = null">Close</button>
          </div>
        </div>
      </div>`,
    methods: { copy() { navigator.clipboard?.writeText(this.store.modal.text); this.store.toast('Copied to clipboard', 'success'); } },
  };

  /** A PKHeX message or question (Alert / Prompt / Error), shown in the page; the host waits for the answer. */
  const TDialog = {
    props: { store: Object },
    data: () => ({ showDetails: false }),
    computed: {
      d() { return this.store.dialogs[0]; },
      icon() { return { error: 'alert', alert: 'info', prompt: 'help' }[this.d?.kind] ?? 'info'; },
      title() { return this.d?.title ?? { error: 'Something went wrong', alert: 'Heads up', prompt: 'Please confirm' }[this.d?.kind] ?? 'TidalHeX'; },
    },
    watch: { 'd.id'() { this.showDetails = false; } },
    methods: {
      label(b) { return { ok: 'OK', yes: 'Yes', no: 'No', cancel: 'Cancel', retry: 'Retry', abort: 'Abort', ignore: 'Ignore', try: 'Try again', continue: 'Continue' }[b] ?? b; },
      copy() { navigator.clipboard?.writeText(`${this.d.text}\n\n${this.d.details ?? ''}`.trim()); this.store.toast('Copied to clipboard', 'success'); },
    },
    template: `
      <div v-if="d" class="modal-back dialog-back">
        <div class="dialog screen dark" :class="d.kind" role="alertdialog" :aria-label="title">
          <div class="dialog-icon"><t-icon :name="icon"></t-icon></div>
          <div class="dialog-body">
            <h3>{{ title }}</h3>
            <p>{{ d.text }}</p>
            <template v-if="d.details">
              <button class="link" @click="showDetails = !showDetails">{{ showDetails ? 'Hide details' : 'Show details' }}</button>
              <pre v-if="showDetails">{{ d.details }}</pre>
            </template>
          </div>
          <div class="foot">
            <button v-if="d.details" class="btn" @click="copy"><t-icon name="copy"></t-icon>Copy</button>
            <button v-for="(b, i) in d.buttons" :key="b" class="btn" :class="{ primary: i === 0 }" @click="store.answerDialog(b)">{{ label(b) }}</button>
          </div>
        </div>
      </div>`,
  };

  /**
   * Frame for in-page save tools: title bar, body, and a footer with More options (PKHeX's full classic editor),
   * Discard/Close and Save. The tool component owns loading, dirty tracking and saving.
   */
  const TTool = {
    props: { title: String, subtitle: String, icon: String, dirty: Boolean, busy: Boolean, loading: Boolean, canSave: { type: Boolean, default: true }, classic: { type: Boolean, default: true } },
    emits: ['save', 'cancel', 'classic'],
    template: `
      <div class="tool-view">
        <header class="tool-head">
          <button class="btn small" title="Back to Save Tools (Esc)" @click="$emit('cancel')"><t-icon name="back"></t-icon></button>
          <span class="ic"><t-icon :name="icon || 'tools'"></t-icon></span>
          <div class="titles"><h2>{{ title }}</h2><small v-if="subtitle">{{ subtitle }}</small></div>
          <div class="actions"><slot name="actions"></slot></div>
        </header>
        <div class="tool-body screen">
          <div v-if="loading" class="empty-state"><div><t-icon :name="icon || 'tools'"></t-icon><h3>Loading…</h3></div></div>
          <slot v-else></slot>
        </div>
        <footer class="tool-foot">
          <button v-if="classic" class="btn" title="Open PKHeX's full editor for this" @click="$emit('classic')"><t-icon name="classic"></t-icon>More options</button>
          <span class="grow"></span>
          <span v-if="dirty" class="pill warn">Unsaved changes</span>
          <button class="btn" @click="$emit('cancel')">{{ dirty ? 'Discard' : 'Close' }}</button>
          <button v-if="canSave" class="btn primary" :disabled="!dirty || busy" @click="$emit('save')"><t-icon name="save"></t-icon>{{ busy ? 'Saving…' : 'Save' }}</button>
        </footer>
      </div>`,
  };

  const TToasts = {
    props: { store: Object },
    template: `<div class="toasts"><transition-group name="toast"><div v-for="t in store.toasts" :key="t.id" class="toast" :class="t.kind"><i></i>{{ t.text }}</div></transition-group></div>`,
  };

  const TCtx = {
    props: { store: Object },
    template: `
      <div v-if="store.ctx" class="ctx" :style="{ left: store.ctx.x + 'px', top: store.ctx.y + 'px' }" @mousedown.stop>
        <template v-for="(it, i) in store.ctx.items" :key="i">
          <hr v-if="it === '-'">
          <button v-else @click="run(it)"><t-icon :name="it.icon"></t-icon>{{ it.label }}<kbd v-if="it.kbd">{{ it.kbd }}</kbd></button>
        </template>
      </div>`,
    methods: { run(it) { this.store.ctx = null; it.run(); } },
  };


  // Format-specific editor fields described by the host (EditorFields.cs): grouped, drawn by kind.
  const MARKS6 = ['●', '▲', '■', '♥', '★', '◆'];
  const MARKS4 = ['●', '■', '▲', '♥']; // Gen 3 order
  const TFields = {
    props: { fields: { type: Array, default: () => [] }, moveOptions: Array, imgFor: Function },
    emits: ['set', 'suggest'],
    data: () => ({ byteIndex: 0 }),
    computed: {
      groups() {
        const map = new Map();
        for (const f of this.fields) {
          if (!map.has(f.group)) map.set(f.group, []);
          map.get(f.group).push(f);
        }
        return [...map.entries()].map(([name, items]) => ({ name, items }));
      },
    },
    methods: {
      set(f, v) { this.$emit('set', f.key, v); },
      marks(f) { return f.value.length === 4 ? MARKS4 : MARKS6; },
      cycleMark(f, i) {
        const next = [...f.value];
        next[i] = (next[i] + 1) % (f.max + 1);
        this.set(f, next);
      },
      toggleFlag(f, bit) { this.set(f, f.value ^ bit); },
      byteAt(f) { return f.value[Math.min(this.byteIndex, f.value.length - 1)] ?? [0, 0]; },
      hex(n) { return '0x' + n.toString(16).toUpperCase().padStart(2, '0'); },
    },
    template: `
      <div class="xfields">
        <template v-for="g in groups" :key="g.name">
          <div v-if="g.name" class="section-title">{{ g.name }}</div>
          <div class="form-grid">
            <div v-for="f in g.items" :key="f.key" class="field" :class="['xf-' + f.kind]">
              <label v-if="f.label !== g.name">{{ f.label }}</label>
              <div v-if="f.kind === 'bool'" class="xf-switch"><t-switch :model-value="f.value" @update:model-value="v => set(f, v)" :label="f.value ? 'Yes' : 'No'"></t-switch></div>
              <div v-else-if="f.kind === 'number'" class="row xf-num">
                <t-number :model-value="f.value" :min="f.min" :max="f.max" @update:model-value="v => set(f, v)"></t-number>
                <button v-if="f.suggest" class="btn small icon-only" title="Suggest" @click="$emit('suggest', f.key)"><t-icon name="wand"></t-icon></button>
              </div>
              <t-combo v-else-if="f.kind === 'select'" :model-value="f.value" :options="f.options ?? []" no-icon @update:model-value="v => set(f, v)"></t-combo>
              <t-combo v-else-if="f.kind === 'move'" class="move-combo" :model-value="f.value" :options="moveOptions ?? []" :img-for="imgFor" @update:model-value="v => set(f, v)"></t-combo>
              <t-text v-else-if="f.kind === 'hex'" cls="mono" :model-value="f.value" :maxlength="f.max" @update:model-value="v => set(f, v)"></t-text>
              <input v-else-if="f.kind === 'datetime'" class="input" type="datetime-local" step="1" :value="f.value" @change="set(f, $event.target.value)">
              <div v-else-if="f.kind === 'flags'" class="xf-chips">
                <button v-for="o in f.options" :key="o.v" class="chip" :class="{ on: (f.value & o.v) !== 0 }" @click="toggleFlag(f, o.v)">{{ o.t }}</button>
              </div>
              <div v-else-if="f.kind === 'marks'" class="xf-marks">
                <button v-for="(m, i) in f.value" :key="i" class="mark" :class="['m' + m]" :title="f.max > 1 ? 'Click: blue → pink → off' : 'Click to toggle'" @click="cycleMark(f, i)">{{ marks(f)[i] }}</button>
              </div>
              <div v-else-if="f.kind === 'bytes'" class="row xf-bytes">
                <select class="input" v-model.number="byteIndex"><option v-for="(b, i) in f.value" :key="b[0]" :value="i">{{ hex(b[0]) }}</option></select>
                <t-number :model-value="byteAt(f)[1]" :min="0" :max="255" @update:model-value="v => $emit('set', 'extra.' + byteAt(f)[0], v)"></t-number>
              </div>
              <div v-else class="input readonly">{{ f.value || '—' }}</div>
              <small v-if="f.hint" class="field-hint">{{ f.hint }}</small>
            </div>
          </div>
        </template>
      </div>`,
  };

  return { TIcon, TCombo, TGames, TDialog, TTool, TSwitch, TTri, TNumber, TText, TModal, TToasts, TCtx, TFields };
})();
