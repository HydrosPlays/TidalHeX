// Development-only mock of the C# host, so the UI can be designed in a normal browser.
// Not embedded in the app (wwwroot/mock is excluded from the build).
window.TidalMock = (() => {
  const S = (id, shiny) => `mock/sprites/${id}${shiny ? 's' : ''}.png`;
  const species = {
    1: ['Bulbasaur', ['grass', 'poison']], 4: ['Charmander', ['fire']], 6: ['Charizard', ['fire', 'flying']], 7: ['Squirtle', ['water']],
    9: ['Blastoise', ['water']], 25: ['Pikachu', ['electric']], 26: ['Raichu', ['electric']], 94: ['Gengar', ['ghost', 'poison']],
    130: ['Gyarados', ['water', 'flying']], 131: ['Lapras', ['water', 'ice']], 133: ['Eevee', ['normal']], 143: ['Snorlax', ['normal']],
    149: ['Dragonite', ['dragon', 'flying']], 150: ['Mewtwo', ['psychic']], 151: ['Mew', ['psychic']], 196: ['Espeon', ['psychic']],
    197: ['Umbreon', ['dark']], 248: ['Tyranitar', ['rock', 'dark']], 249: ['Lugia', ['psychic', 'flying']], 250: ['Ho-Oh', ['fire', 'flying']],
    282: ['Gardevoir', ['psychic', 'fairy']], 373: ['Salamence', ['dragon', 'flying']], 384: ['Rayquaza', ['dragon', 'flying']],
    445: ['Garchomp', ['dragon', 'ground']], 448: ['Lucario', ['fighting', 'steel']], 471: ['Glaceon', ['ice']], 658: ['Greninja', ['water', 'dark']],
    700: ['Sylveon', ['fairy']], 887: ['Dragapult', ['dragon', 'ghost']], 906: ['Sprigatito', ['grass']], 909: ['Fuecoco', ['fire']],
    912: ['Quaxly', ['water']], 925: ['Maushold', ['normal']], 937: ['Ceruledge', ['fire', 'ghost']], 999: ['Gimmighoul', ['ghost']],
    1000: ['Gholdengo', ['steel', 'ghost']], 1007: ['Koraidon', ['fighting', 'dragon']], 1008: ['Miraidon', ['electric', 'dragon']],
    1017: ['Ogerpon', ['grass']], 1024: ['Terapagos', ['normal']],
  };
  const ids = Object.keys(species).map(Number);
  const rnd = (seed => () => (seed = (seed * 16807) % 2147483647) / 2147483647)(42);
  const pick = arr => arr[Math.floor(rnd() * arr.length)];

  const mkSlot = (box, slot, id, extra = {}) => ({
    box, slot, empty: !id, species: id || 0, form: 0, name: id ? species[id][0] : '', nickname: id ? species[id][0] : '',
    level: id ? 5 + Math.floor(rnd() * 95) : 0, gender: Math.floor(rnd() * 2), shiny: false, egg: false, legal: true,
    heldItem: '', sprite: id ? S(id, extra.shiny) : '', locked: false, ...extra,
  });

  const boxCount = 32;
  const boxes = Array.from({ length: boxCount }, (_, b) => Array.from({ length: 30 }, (_, s) => {
    const fill = b < 3 ? 0.8 : b < 8 ? 0.4 : 0.05;
    if (rnd() > fill) return mkSlot(b, s, 0);
    const id = pick(ids);
    return mkSlot(b, s, id, { shiny: rnd() < 0.08, legal: rnd() > 0.06 });
  }));
  const party = [
    mkSlot(-1, 0, 1000, { level: 68, nickname: 'Goldie' }), mkSlot(-1, 1, 6, { level: 72 }), mkSlot(-1, 2, 94, { level: 65, shiny: true, sprite: S(94, true) }),
    mkSlot(-1, 3, 658, { level: 70 }), mkSlot(-1, 4, 448, { level: 64 }), mkSlot(-1, 5, 131, { level: 61 }),
  ];

  const save = {
    loaded: true, blank: false, game: 'Pokémon Scarlet', version: 50, generation: 9, context: 'Gen9', ot: 'Hydro', tid: '482913', sid: '1234',
    playTime: '128:22:05', fileName: 'main', filePath: 'C:\\Users\\hydro\\Saves\\Scarlet\\main', boxCount, slotsPerBox: 30,
    pokemonCount: 0, slotCount: boxCount * 30, dexCaught: 312, dexTotal: 400, hasParty: true, hasBox: true, exportable: true, edited: false, party,
  };
  const count = () => { save.pokemonCount = boxes.flat().filter(s => !s.empty).length; };
  count();

  const typeColors = {};
  const natures = ['Hardy', 'Lonely', 'Brave', 'Adamant', 'Naughty', 'Bold', 'Docile', 'Relaxed', 'Impish', 'Lax', 'Timid', 'Hasty', 'Serious', 'Jolly', 'Naive', 'Modest', 'Mild', 'Quiet', 'Bashful', 'Rash', 'Calm', 'Gentle', 'Sassy', 'Careful', 'Quirky'];
  const moveNames = { 0: '(None)', 85: 'Thunderbolt', 94: 'Psychic', 247: 'Shadow Ball', 399: 'Dark Pulse', 430: 'Flash Cannon', 53: 'Flamethrower', 57: 'Surf', 58: 'Ice Beam', 89: 'Earthquake', 200: 'Outrage', 406: 'Dragon Pulse', 14: 'Swords Dance', 182: 'Protect', 874: 'Make It Rain', 417: 'Nasty Plot', 105: 'Recover', 347: 'Calm Mind' };
  const moveTypes = { 85: 'electric', 94: 'psychic', 247: 'ghost', 399: 'dark', 430: 'steel', 53: 'fire', 57: 'water', 58: 'ice', 89: 'ground', 200: 'dragon', 406: 'dragon', 14: 'normal', 182: 'normal', 874: 'steel', 417: 'dark', 105: 'normal', 347: 'psychic' };
  const lists = {
    species: [{ v: 0, t: '(None)' }, ...ids.map(v => ({ v, t: species[v][0] }))],
    moves: Object.entries(moveNames).map(([v, t]) => ({ v: +v, t })),
    natures: natures.map((t, v) => ({ v, t })),
    items: [{ v: 0, t: '(None)' }, { v: 234, t: 'Leftovers' }, { v: 235, t: 'Light Clay' }, { v: 247, t: 'Life Orb' }, { v: 275, t: 'Focus Sash' }, { v: 1606, t: 'Ability Patch' }],
    abilities: [{ v: 0, t: '—' }],
    balls: [{ v: 1, t: 'Master Ball' }, { v: 2, t: 'Ultra Ball' }, { v: 3, t: 'Great Ball' }, { v: 4, t: 'Poké Ball' }, { v: 5, t: 'Safari Ball' }, { v: 16, t: 'Cherish Ball' }, { v: 26, t: 'Moon Ball' }],
    languages: [{ v: 2, t: 'English' }, { v: 1, t: 'Japanese' }, { v: 3, t: 'French' }, { v: 5, t: 'German' }, { v: 7, t: 'Spanish' }],
    games: [{ v: 50, t: 'Scarlet' }, { v: 51, t: 'Violet' }, { v: 44, t: 'Sword' }, { v: 45, t: 'Shield' }],
    types: ['normal', 'fighting', 'flying', 'poison', 'ground', 'rock', 'bug', 'ghost', 'steel', 'fire', 'water', 'grass', 'electric', 'psychic', 'ice', 'dragon', 'dark', 'fairy', 'stellar'].map((t, v) => ({ v, t: t[0].toUpperCase() + t.slice(1) })),
    locations: [{ v: 6, t: 'South Province (Area One)' }, { v: 12, t: 'Mesagoza' }, { v: 30024, t: 'a Pokémon event' }, { v: 22, t: 'Glaseado Mountain' }],
  };

  const baseStats = { 1000: [87, 60, 95, 133, 91, 84] };
  let editorVersion = 1;
  let editor = null;

  function makeEditor(id = 1000) {
    const [name, types] = species[id] ?? ['???', ['normal']];
    const base = baseStats[id] ?? [80, 90, 80, 100, 85, 95];
    const keys = [['hp', 'HP'], ['atk', 'Attack'], ['def', 'Defense'], ['spa', 'Sp. Atk'], ['spd', 'Sp. Def'], ['spe', 'Speed']];
    editor = {
      empty: false, format: 9, generation: 9, sprite: S(id), species: id, speciesName: name, form: 0, forms: [{ v: 0, t: 'Chest' }], nickname: id === 1000 ? 'Goldie' : name, isNicknamed: id === 1000,
      level: 68, exp: 314432, nature: 15, statNature: 15, hasStatNature: true, ability: 281, abilityNumber: 1, abilities: [{ v: 281, t: 'Good as Gold' }],
      heldItem: 234, hasHeldItem: true, gender: 2, genderLocked: true, shiny: 'none', isEgg: false, pid: '8F2A91C4', ec: '1B7730E2', hasEC: true,
      language: 2, friendship: 160, ball: 4, types: types.map(t => ({ id: t, name: t })),
      met: { version: 50, location: 6, level: 5, date: '2023-11-18', eggLocation: 0, eggDate: '', fateful: false, hasEggMet: true },
      ot: { name: 'Hydro', gender: 0, tid: '482913', sid: '1234', language: 2 },
      ht: { has: true, name: '', gender: 0, friendship: 0 },
      stats: keys.map(([key, label], i) => ({ key, name: label, base: base[i], iv: 31, ev: i === 3 ? 252 : i === 5 ? 252 : i === 0 ? 4 : 0, value: 0, ht: false, natureMod: key === 'spa' ? 1 : key === 'atk' ? -1 : 0 })),
      ivMax: 31, evMax: 252, evTotalMax: 510, hasHyperTraining: true, teraType: 8, hasTera: true, hiddenPower: 'Dark',
      moves: [874, 247, 417, 105].map(m => ({ id: m, name: moveNames[m], pp: 8, ppUps: 3, maxPp: 8, type: moveTypes[m], category: m === 417 || m === 105 ? 'Status' : 'Special', legal: true })),
      relearn: [0, 0, 0, 0], hasRelearn: true,
      legality: { valid: true, summary: 'Legal!', issues: [] },
      v: editorVersion++,
    };
    recalc();
    return editor;
  }

  function recalc() {
    const lvl = editor.level;
    editor.stats.forEach((s, i) => {
      const core = Math.floor(((2 * s.base + s.iv + Math.floor(s.ev / 4)) * lvl) / 100);
      s.value = i === 0 ? core + lvl + 10 : Math.floor((core + 5) * (s.natureMod > 0 ? 1.1 : s.natureMod < 0 ? 0.9 : 1));
    });
  }

  const encounterPool = () => [1000, 999, 999, 999, 25, 133, 131, 445, 925].map((id, i) => ({
    token: `e${i}`, species: id, form: 0, name: species[id][0], version: 50, versionName: pick(['Scarlet', 'Violet', 'Legends: Z-A']),
    location: pick(['South Province (Area One)', 'Glaseado Mountain', 'Asado Desert', 'West Paldean Sea', 'Hyperspace Lumiose']),
    levelMin: 5 + i * 3, levelMax: 10 + i * 4, type: pick(['slot', 'static', 'gift', 'raid']), typeLabel: '', generation: 9,
    shiny: pick(['random', 'never']), ball: 0, alpha: i === 2, gmax: false, games: pick([[{ id: 50, name: 'Scarlet' }, { id: 51, name: 'Violet' }], [{ id: 32, name: 'Ultra Sun' }, { id: 33, name: 'Ultra Moon' }], [{ id: 35, name: 'Red' }, { id: 36, name: 'Green' }, { id: 37, name: 'Blue' }, { id: 38, name: 'Yellow' }], [{ id: 52, name: 'Legends: Z-A' }]]), facts: i % 3 ? [] : [{ label: 'Moves', value: 'Tackle / Growl' }, { label: 'Nature', value: 'Adamant' }, { label: 'IVs', value: 'At least 3 perfect' }],
    sprite: S(id),
  })).map(e => ({ ...e, typeLabel: { slot: 'Wild', static: 'Static', gift: 'Gift', raid: 'Raid' }[e.type] }));

  const gifts = Array.from({ length: 60 }, (_, i) => {
    const id = pick(ids);
    return {
      token: `g${i}`, species: id, form: 0, name: species[id][0], title: pick(['Pokémon HOME Gift', 'Tera Raid Event', 'Anniversary Celebration', 'Ash\'s Pikachu', 'Shiny Rayquaza Distribution', 'Mystery Gift Card']),
      cardId: 1000 + i, type: pick(['wc9', 'wc8', 'wc7', 'pgf', 'wc6']), generation: pick([5, 6, 7, 8, 9]), level: 50, shiny: rnd() < 0.2, egg: false, item: false,
      ot: pick(['HOME', 'Ash', 'GAME FREAK', 'Paldea']), fileName: `Card ${i}.wc9`, isItem: false, heldItem: '', moves: [], details: ['Card #: 1000', 'OT: HOME', 'Level: 50'], games: pick([[{ id: 44, name: 'Sword' }, { id: 45, name: 'Shield' }], [{ id: 10, name: 'Diamond' }, { id: 11, name: 'Pearl' }, { id: 12, name: 'Platinum' }, { id: 7, name: 'HeartGold' }, { id: 8, name: 'SoulSilver' }]]), sprite: S(id),
    };
  });

  const tools = [
    { id: 'OpenTrainerInfo', name: 'Trainer Info', category: 'Trainer', description: 'Name, money, play time and profile' },
    { id: 'OpenItemPouch', name: 'Items', category: 'Trainer', description: 'Bag pockets and quantities' },
    { id: 'OpenPokedex', name: 'Pokédex', category: 'Pokédex & Items', description: 'Seen, caught and form flags' },
    { id: 'OpenBoxLayout', name: 'Box Layout', category: 'Storage', description: 'Box names and wallpapers' },
    { id: 'OpenWondercards', name: 'Wondercards', category: 'Events', description: 'Received mystery gift cards' },
    { id: 'OpenEventFlags', name: 'Event Flags', category: 'Events', description: 'Story flags and work values' },
    { id: 'Raids', name: 'Tera Raids', category: 'Battle', description: 'Active raid dens' },
    { id: 'RaidsDLC1', name: 'Raids (Kitakami)', category: 'Battle', description: 'DLC raid dens' },
    { id: 'OpenFashion', name: 'Fashion', category: 'Trainer', description: 'Unlocked clothing' },
    { id: 'Blocks', name: 'Block Data', category: 'Data', description: 'Raw save blocks' },
    { id: 'VerifyCHK', name: 'Verify Checksums', category: 'Data', description: 'Check save integrity' },
    { id: 'VerifySaveEntities', name: 'Verify All Pokémon', category: 'Data', description: 'Legality check every stored Pokémon' },
  ];

  const delay = v => new Promise(r => setTimeout(() => r(v), 60 + Math.random() * 120));
  const mockSettings = { reducedMotion: false, hideSecrets: false, encountersInGameOnly: true, giftsInGameOnly: true };

  async function call(method, args, emit) {
    switch (method) {
      case 'app.init': return delay({ version: 'dev', save, recent: [{ path: save.filePath, name: 'main', folder: 'Scarlet', exists: true }, { path: 'C:\\Saves\\Emerald.sav', name: 'Emerald.sav', folder: 'Saves', exists: true }, { path: 'C:\\Saves\\HGSS.sav', name: 'HGSS.sav', folder: 'Saves', exists: true }], settings: mockSettings });
      case 'app.setOption': mockSettings[args.name] = args.value; return delay({ ...mockSettings });
      case 'box.get': { const b = args.box; return delay({ box: b, name: `Box ${b + 1}`, boxCount, wallpaper: '', slots: boxes[b] }); }
      case 'box.party': return delay({ slots: party });
      case 'box.move': {
        const get = r => (r.box < 0 ? party : boxes[r.box]);
        const a = get(args.from), b = get(args.to);
        const x = a[args.from.slot], y = b[args.to.slot];
        if (args.mode === 'clone') b[args.to.slot] = { ...x, box: args.to.box, slot: args.to.slot };
        else { b[args.to.slot] = { ...x, box: args.to.box, slot: args.to.slot }; a[args.from.slot] = { ...y, box: args.from.box, slot: args.from.slot }; }
        count(); save.edited = true; emit('saveChanged', save);
        return delay(true);
      }
      case 'box.delete': { const r = args.slot; const arr = r.box < 0 ? party : boxes[r.box]; arr[r.slot] = mkSlot(r.box, r.slot, 0); count(); save.edited = true; emit('saveChanged', save); return delay(true); }
      case 'box.view': { const r = args.slot; const s = (r.box < 0 ? party : boxes[r.box])[r.slot]; const e = makeEditor(s.species); e.level = s.level; e.nickname = s.nickname; e.shiny = s.shiny ? 'star' : 'none'; e.sprite = s.sprite; recalc(); emit('editorLoaded', e); return delay(e); }
      case 'box.set': return delay(true);
      case 'box.legality': return delay({ valid: true, report: 'Legal!\n\nEncounter Type: Wild Encounter (Scarlet)\nLocation: South Province (Area One)\nPID-Encryption Constant: Valid\nIVs: Valid\nMoves: All valid' });
      case 'editor.get': return delay(editor ?? makeEditor());
      case 'editor.set': {
        const { field, value } = args;
        const parts = field.split('.');
        if (parts[0] === 'stats') { const s = editor.stats.find(z => z.key === parts[1]); s[parts[2]] = value; }
        else if (parts[0] === 'moves') { const m = editor.moves[+parts[1]]; m[parts[2]] = value; if (parts[2] === 'id') { m.name = moveNames[value]; m.type = moveTypes[value] ?? 'normal'; } }
        else if (parts.length === 2) editor[parts[0]][parts[1]] = value;
        else editor[field] = value;
        if (field === 'species') { const lvl = editor.level; makeEditor(value); editor.level = lvl; }
        if (field === 'shiny' || field === 'shinySID') { editor.shiny = value === 'none' ? 'none' : value === 'square' ? 'square' : 'star'; editor.sprite = S(editor.species, value !== 'none'); }
        recalc(); editor.v = editorVersion++;
        return delay(editor);
      }
      case 'editor.legality': return delay({ valid: true, report: 'Legal!\n\nAll checks passed.' });
      case 'list.get': return delay(lists[args.name] ?? (args.name.endsWith('Locations') ? lists.locations : []));
      case 'enc.search': return delay(encounterPool());
      case 'enc.load': case 'gift.load': { const e = makeEditor(1000); emit('editorLoaded', e); return delay(e); }
      case 'gift.all': return delay(gifts);
      case 'tools.list': return delay(tools);
      case 'tools.open': emit('toast', { kind: 'info', text: `Would open “${args.id}” (classic editor)` }); return delay(true);
      case 'box.dropFile': emit('toast', { kind: 'success', text: `Placed ${args.names?.[0] ?? 'file'} in slot ${args.slot.slot + 1} (mock)` }); return delay({ placed: true, save: null });
      case 'file.open': case 'file.openPath': case 'openDropped': emit('toast', { kind: 'success', text: 'Opened (mock)' }); emit('saveLoaded', save); return delay(save);
      case 'file.exportSave': save.edited = false; emit('saveChanged', save); emit('toast', { kind: 'success', text: 'Save exported (mock)' }); return delay(true);
      default: emit('toast', { kind: 'info', text: `${method} (mock)` }); return delay(true);
    }
  }

  return { call };
})();
