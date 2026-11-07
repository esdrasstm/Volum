/* =========================================================
   Volum — janela de configurações (settings.html)
   Textos, novidades e formatação vêm do shared.js (window.Volum).
   Fala com o C# (SettingsForm.cs) por mensagens JSON:
     C# -> JS : settings | systemTheme | hotkey | water
     JS -> C# : ready | setTheme | setLanguage | setAutostart | setLightMode | hotkeyRecord | setHotkey
                | setWater | testWater | setIsland | openSite
   ========================================================= */
(() => {
  'use strict';

  const V = window.Volum;
  const $ = (id) => document.getElementById(id);
  const root = document.documentElement;
  const post = (msg) => window.chrome.webview.postMessage(msg);
  const prefersLight = window.matchMedia('(prefers-color-scheme: light)');

  let lang = 'pt';
  const t = (key) => V.t(lang, key);
  let version = '';
  let fromStore = false;   // instalado pela Microsoft Store

  /* ---------------- Seções (menu à esquerda) ---------------- */
  const navItems = [...document.querySelectorAll('.nav-item')];
  function showPage(page) {
    navItems.forEach((b) => {
      if (b.dataset.page === page) b.setAttribute('aria-current', 'page');
      else b.removeAttribute('aria-current');
    });
    document.querySelectorAll('.page').forEach((p) => { p.hidden = p.id !== 'page-' + page; });
  }
  navItems.forEach((b) => b.addEventListener('click', () => showPage(b.dataset.page)));

  /* ---------------- Tema e idioma ---------------- */
  // Marca a opção escolhida num seletor (tema ou idioma); a pílula desliza até ela
  function selectSegment(seg, value) {
    [...seg.querySelectorAll('button')].forEach((b, i) => {
      const on = b.dataset.value === value;
      b.setAttribute('aria-checked', String(on));
      if (on) seg.style.setProperty('--seg-i', i);
    });
  }

  // Tema efetivo (claro/escuro). "Sistema" segue o tema do Windows que o C# informa (systemDark)
  let systemDark = null;
  function applyScheme() {
    const mode = root.dataset.theme;
    const sysLight = systemDark === null ? prefersLight.matches : !systemDark;
    root.dataset.scheme = mode === 'light' || (mode === 'system' && sysLight) ? 'light' : 'dark';
  }
  prefersLight.addEventListener('change', applyScheme);

  function setTheme(mode) {
    root.dataset.theme = mode;
    selectSegment($('themeSeg'), mode);
    applyScheme();
  }

  function setLanguage(value) {
    lang = V.I18N[value] ? value : 'pt';
    root.lang = V.langTag(lang);
    selectSegment($('langSeg'), lang);
    V.applyI18n(lang);
    document.title = `Volum — ${t('settings')}`;
    showHotkey();
    showWater();
    showFocus();
    showAbout();
  }

  $('themeSeg').querySelectorAll('button').forEach((b) => b.addEventListener('click', () => {
    setTheme(b.dataset.value);
    post({ type: 'setTheme', mode: b.dataset.value });
  }));

  $('langSeg').querySelectorAll('button').forEach((b) => b.addEventListener('click', () => {
    setLanguage(b.dataset.value);
    post({ type: 'setLanguage', lang: b.dataset.value });
  }));

  $('autostart').addEventListener('change', (e) => post({ type: 'setAutostart', enabled: e.target.checked }));
  $('lite').addEventListener('change', (e) => post({ type: 'setLightMode', enabled: e.target.checked }));

  /* ---------------- Atalho para abrir ---------------- */
  // Quem registra o atalho no Windows é o C#; aqui só mostramos o atual e gravamos um novo.
  // Mods = soma de Alt 1, Ctrl 2, Shift 4 (como no Windows); key = código da tecla (VK); 0 = desligado.
  const hotkeyBtn = $('hotkeyBtn');
  const hotkeyHint = $('hotkeyHint');
  let hotkey = { mods: 2 | 1, key: 86 };
  let recording = false;

  const keyName = (vk) =>
    (vk >= 65 && vk <= 90) || (vk >= 48 && vk <= 57) ? String.fromCharCode(vk)
      : vk >= 112 && vk <= 135 ? 'F' + (vk - 111) : '?';

  const hotkeyLabel = (h) => !h.key ? t('hotkeyOff')
    : [h.mods & 2 && 'Ctrl', h.mods & 1 && 'Alt', h.mods & 4 && 'Shift', keyName(h.key)].filter(Boolean).join(' + ');

  // Tecla física → código do Windows: letras, números e F1–F24 (não depende do layout do teclado)
  function codeToVk(code) {
    let m = /^Key([A-Z])$/.exec(code);
    if (m) return m[1].charCodeAt(0);
    if ((m = /^Digit(\d)$/.exec(code))) return 48 + +m[1];
    if ((m = /^F(\d{1,2})$/.exec(code)) && +m[1] <= 24) return 111 + +m[1];
    return 0;
  }

  function showHotkey(hint) {
    hotkeyBtn.textContent = recording ? t('hotkeyPress') : hotkeyLabel(hotkey);
    hotkeyBtn.classList.toggle('recording', recording);
    hotkeyHint.textContent = hint || t(recording ? 'hotkeyRecordHint' : 'hotkeyHint');
  }

  // Gravando, o C# solta o atalho atual (senão apertar as mesmas teclas abriria o mixer)
  function setRecording(on) {
    if (recording === on) return;
    recording = on;
    post({ type: 'hotkeyRecord', on });
    showHotkey();
  }

  function saveHotkey(mods, key) {
    recording = false;
    showHotkey();
    post({ type: 'setHotkey', mods, key });         // o C# responde com "hotkey" (ok ou em uso)
  }

  hotkeyBtn.addEventListener('click', () => setRecording(!recording));
  hotkeyBtn.addEventListener('blur', () => setRecording(false));

  document.addEventListener('keydown', (e) => {
    if (!recording) return;
    e.preventDefault();
    if (e.key === 'Escape') return setRecording(false);
    if (e.key === 'Backspace' || e.key === 'Delete') return saveHotkey(0, 0);
    const vk = codeToVk(e.code);
    if (!vk) return;                                // só Ctrl/Alt/Shift até agora: espera a tecla
    const mods = (e.altKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.shiftKey ? 4 : 0);
    // Sem Ctrl ou Alt, a tecla (ou Shift + tecla) pararia de funcionar em todos os apps e jogos
    if (!(mods & 3)) return showHotkey(t('hotkeyNeedMod'));
    saveHotkey(mods, vk);
  });

  /* ---------------- Lembrete de água ---------------- */
  // Quem lembra e guarda tudo é o C# (Water.cs). Meta e copo: os botões − e + andam nestas listas.
  // Intervalo: livre (a pessoa digita os minutos); − e + andam de 5 em 5.
  const WATER_STEPS = {
    goalMl: [1000, 1500, 2000, 2500, 3000, 3500, 4000],
    cupMl: [150, 200, 250, 300, 350, 500],
  };
  const EVERY_MIN = 5, EVERY_MAX = 480, EVERY_STEP = 5;
  const clampEvery = (m) => Math.min(EVERY_MAX, Math.max(EVERY_MIN, Math.round(m) || 60));
  const everyInput = $('waterEvery');
  let water = { enabled: false, goalMl: 2000, intervalMin: 60, cupMl: 250, todayMl: 0, sound: true };

  // Posição na lista mais perto do valor atual (o C# pode ter guardado um valor fora da lista)
  const nearest = (list, v) => list.reduce((best, x, i) => (Math.abs(x - v) < Math.abs(list[best] - v) ? i : best), 0);

  function showWater() {
    $('waterOn').checked = water.enabled;
    $('waterSound').checked = water.sound !== false;
    $('waterOptions').hidden = !water.enabled;
    document.querySelectorAll('#waterOptions .stepper').forEach((st) => {
      const field = st.dataset.field;
      if (field === 'intervalMin') {
        if (document.activeElement !== everyInput) everyInput.value = water.intervalMin;   // não atrapalha quem está digitando
        st.querySelector('[data-step="-1"]').disabled = water.intervalMin <= EVERY_MIN;
        st.querySelector('[data-step="1"]').disabled = water.intervalMin >= EVERY_MAX;
        return;
      }
      const list = WATER_STEPS[field], i = nearest(list, water[field]);
      st.querySelector('output').textContent = V.fmtMl(water[field], lang);
      st.querySelector('[data-step="-1"]').disabled = i <= 0;
      st.querySelector('[data-step="1"]').disabled = i >= list.length - 1;
    });
    $('waterToday').textContent = water.enabled
      ? t('waterToday').replace('{0}', t('waterOf').replace('{0}', V.fmtMl(water.todayMl, lang)).replace('{1}', V.fmtMl(water.goalMl, lang)))
      : '';
  }

  const saveWater = () => post({
    type: 'setWater', enabled: water.enabled, goalMl: water.goalMl, intervalMin: water.intervalMin, cupMl: water.cupMl, sound: water.sound !== false,
  });

  $('waterOn').addEventListener('change', (e) => {
    water.enabled = e.target.checked;
    showWater();
    saveWater();
  });

  document.querySelectorAll('#waterOptions .stepper').forEach((st) => st.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const field = st.dataset.field, step = Number(b.dataset.step);
    if (field === 'intervalMin') {
      // − / +: vai para o próximo múltiplo de 5 (ex.: 47 → 45 ou 50)
      const m = water.intervalMin;
      water.intervalMin = clampEvery(step > 0 ? Math.floor(m / EVERY_STEP) * EVERY_STEP + EVERY_STEP
                                              : Math.ceil(m / EVERY_STEP) * EVERY_STEP - EVERY_STEP);
    } else {
      const list = WATER_STEPS[field];
      water[field] = list[Math.min(list.length - 1, Math.max(0, nearest(list, water[field]) + step))];
    }
    showWater();
    saveWater();
  }));

  // Intervalo digitado: vale ao sair do campo ou com Enter (de 5 a 480 minutos)
  const applyEvery = () => {
    const m = clampEvery(Number(everyInput.value));
    everyInput.value = m;
    if (m === water.intervalMin) return;
    water.intervalMin = m;
    showWater();
    saveWater();
  };
  everyInput.addEventListener('change', applyEvery);
  everyInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') everyInput.blur(); });

  $('waterSound').addEventListener('change', (e) => { water.sound = e.target.checked; saveWater(); });
  $('waterTest').addEventListener('click', () => post({ type: 'testWater' }));

  /* ---------------- Ilha ---------------- */
  let islandOn = false;
  $('islandOn').addEventListener('change', (e) => {
    islandOn = e.target.checked;
    showFocus();
    post({ type: 'setIsland', enabled: islandOn });
  });

  /* ---------------- Pomodoro e lembrete para levantar (página Ilha) ---------------- */
  const FOCUS_STEPS = {
    focusMin: [15, 20, 25, 30, 35, 40, 45, 50, 60, 75, 90],
    shortMin: [3, 5, 10, 15],
    longMin: [10, 15, 20, 25, 30],
    every: [2, 3, 4, 5, 6],
    standMin: [30, 45, 60, 75, 90, 120],
    pomoSound: ['chime', 'drops', 'bell', 'none'],   // som do fim do Pomodoro (nomes no PomodoroSound do C#)
  };
  let focus = { pomodoro: true, focusMin: 25, shortMin: 5, longMin: 15, every: 4, pomoSound: 'chime', osd: true, standUp: true, standMin: 60 };

  const fmtFocus = (v, unit) => unit === 'sound' ? t('snd_' + v) : unit === 'cycles' ? `${v} ${t('pomoEveryUnit')}`
    : unit === 'time' ? V.fmtMin(v) : `${v} min`;

  function showFocus() {
    $('pomoOn').checked = focus.pomodoro;
    $('standOn').checked = focus.standUp;
    $('osdOn').checked = focus.osd !== false;
    $('pomoOptions').hidden = !focus.pomodoro;
    $('standOptions').hidden = !focus.standUp;
    $('standTest').disabled = !islandOn;
    $('focusNote').hidden = islandOn;   // com a ilha desligada, avisa que tudo isso depende dela
    document.querySelectorAll('.fstep').forEach((st) => {
      const key = st.dataset.key, list = FOCUS_STEPS[key], i = typeof focus[key] === 'string' ? Math.max(0, list.indexOf(focus[key])) : nearest(list, focus[key]);
      st.querySelector('output').textContent = fmtFocus(focus[key], st.dataset.unit);
      st.querySelector('[data-step="-1"]').disabled = i <= 0;
      st.querySelector('[data-step="1"]').disabled = i >= list.length - 1;
    });
  }

  const saveFocus = () => post({ type: 'setFocus', ...focus });

  $('pomoOn').addEventListener('change', (e) => { focus.pomodoro = e.target.checked; showFocus(); saveFocus(); });
  $('standOn').addEventListener('change', (e) => { focus.standUp = e.target.checked; showFocus(); saveFocus(); });
  $('osdOn').addEventListener('change', (e) => { focus.osd = e.target.checked; saveFocus(); });
  document.querySelectorAll('.fstep').forEach((st) => st.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    const key = st.dataset.key, list = FOCUS_STEPS[key];
    const at = typeof focus[key] === 'string' ? Math.max(0, list.indexOf(focus[key])) : nearest(list, focus[key]);
    focus[key] = list[Math.min(list.length - 1, Math.max(0, at + Number(b.dataset.step)))];
    if (key === 'pomoSound') post({ type: 'testPomoSound', sound: focus[key] });   // toca o som escolhido
    showFocus();
    saveFocus();
  }));
  $('standTest').addEventListener('click', () => post({ type: 'testStand' }));

  /* ---------------- Sobre ---------------- */
  function showAbout() {
    $('aboutVersion').textContent = version ? t('aboutVersion').replace('{0}', version) : '';
    $('aboutNews').replaceChildren(...V.newsItems(lang, fromStore));
  }

  $('siteBtn').addEventListener('click', () => post({ type: 'openSite' }));
  $('feedbackBtn').addEventListener('click', () => post({ type: 'openSite', section: 'feedback' }));
  $('githubBtn').addEventListener('click', () => post({ type: 'openSite', section: 'github' }));
  $('aboutNews').addEventListener('click', (e) => {   // botão dentro de uma novidade (ex.: Microsoft Store)
    const b = e.target.closest('[data-site]');
    if (b) post({ type: 'openSite', section: b.dataset.site });
  });

  /* ---------------- Mensagens do C# ---------------- */
  function applySettings(s) {
    if (typeof s.systemDark === 'boolean') systemDark = s.systemDark;
    version = s.version || '';
    fromStore = !!s.store;
    if (s.hotkey) hotkey = s.hotkey;
    if (s.water) water = s.water;
    recording = false;
    $('autostart').checked = !!s.autostart;
    $('lite').checked = !!s.lite;
    $('islandOn').checked = islandOn = !!s.island;
    if (s.focus) focus = s.focus;
    setTheme(s.theme || 'system');
    setLanguage(s.language || 'pt');                // também redesenha atalho, água e "Sobre"
  }

  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m.type) {
      case 'settings':    applySettings(m); break;
      case 'systemTheme': systemDark = m.dark; applyScheme(); break;   // trocou o tema do Windows
      case 'hotkey':      hotkey = m; showHotkey(m.ok ? '' : t('hotkeyBusy')); break;   // resposta do setHotkey
      case 'water':       water = m.water; showWater(); break;          // contador mudou (mixer ou aviso)
    }
  });

  document.addEventListener('contextmenu', (e) => e.preventDefault());

  post({ type: 'ready' });
})();
