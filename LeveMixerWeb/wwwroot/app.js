/* =========================================================
   Volum — comportamento do mixer (as configurações ficam na janela própria: settings.js)
   Textos, novidades e formatação vêm do shared.js (window.Volum).
   Fala com o C# por mensagens JSON:
     C# -> JS : settings | state | peaks | open | close | water
     JS -> C# : ready | resize | setVolume | setMute | setMicDevice | openSite | openSettings
                | lockKeyboard | waterAdd | newsSeen | hide | tick
   ========================================================= */
(() => {
  'use strict';

  const MASTER = '__master';
  const MIC = '__mic';
  const isFixed = (id) => id === MASTER || id === MIC;   // linhas fixas do painel de cima
  const $ = (id) => document.getElementById(id);
  const V = window.Volum;

  let lang = 'pt';
  const t = (key) => V.t(lang, key);

  // Linhas do sistema: o nome vem da tradução, não do C#
  const SYSTEM_NAMES = { __master: 'master', __mic: 'mic', __system: 'systemSounds' };
  const displayName = (id, name) => (SYSTEM_NAMES[id] ? t(SYSTEM_NAMES[id]) : name);
  const post = (msg) => window.chrome.webview.postMessage(msg);

  const card = $('card');
  const appsEl = $('apps');
  const masterEl = $('master');
  const micEl = $('mic');
  const outEl = $('out');
  const micRowEl = $('micRow');
  const micPickBtn = $('micPickBtn');
  const micCurrentEl = $('micCurrent');
  const micListEl = $('micList');
  const micListInner = micListEl.firstElementChild;
  const emptyEl = $('empty');
  const appCountEl = $('appCount');
  const root = document.documentElement;
  const prefersLight = window.matchMedia('(prefers-color-scheme: light)');

  // Ícones de traço, todos com o mesmo peso (classe .ico no style.css)
  const svg = (body) => '<svg class="ico" viewBox="0 0 24 24" aria-hidden="true">' + body + '</svg>';
  const SPEAKER = '<path d="M11 5 6.5 9H4a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h2.5l4.5 4z"/>';
  const ICON_VOL = svg(SPEAKER + '<path d="M15.5 9a4.5 4.5 0 0 1 0 6M18.5 6.5a8.5 8.5 0 0 1 0 11"/>');
  const ICON_MUTE = svg(SPEAKER + '<path d="m16 9.5 5 5M21 9.5l-5 5"/>');

  // Ícones das linhas do sistema (não têm .exe de onde tirar o ícone)
  const SYSTEM_ICONS = {
    // Volume geral: fone de ouvido
    __master: svg('<path d="M3.5 17v-5a8.5 8.5 0 0 1 17 0v5"/>' +
      '<path d="M20.5 18a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3zM3.5 18a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2h-3z"/>'),
    // Sons do sistema: sino
    __system: svg('<path d="M6 8.5a6 6 0 0 1 12 0c0 6.5 2.5 8.5 2.5 8.5h-17S6 15 6 8.5"/><path d="M10.2 20.5a2 2 0 0 0 3.6 0"/>'),
    // Microfone
    __mic: svg('<rect x="9" y="2.5" width="6" height="11.5" rx="3"/><path d="M18.5 10.5a6.5 6.5 0 0 1-13 0M12 17v4"/>'),
  };

  // Onda do fim da linha dos apps: 7 barrinhas, cada uma num <path> para poder animar
  const WAVE =
    '<svg class="wave ico" viewBox="0 0 24 24" aria-hidden="true">' +
    ['M3 11v2', 'M6 8v8', 'M9 4v16', 'M12 7v10', 'M15 10v4', 'M18 8v8', 'M21 11v2']
      .map((d) => '<path d="' + d + '"/>').join('') + '</svg>';

  const rows = new Map();       // id -> { el, slider, ... }
  const iconCache = new Map();  // id -> data URI (o C# envia cada ícone uma única vez)

  /* ---------------- Linhas ---------------- */

  function createRow(id, name) {
    const el = document.createElement('div');
    el.className = 'row';
    el.dataset.id = id;
    el.innerHTML =
      '<div class="row-icon"></div>' +
      '<div class="row-main">' +
        '<div class="row-top"><span class="row-name"></span><span class="row-pct"></span></div>' +
        '<div class="slider">' +
          '<div class="slider-track"><div class="slider-fill"></div></div>' +
          '<div class="slider-thumb"></div>' +
          '<input class="slider-input" type="range" min="0" max="100" step="1">' +
        '</div>' +
        '<div class="meter"><div class="meter-fill"></div></div>' +
      '</div>' +
      // Volume geral e microfone: alto-falante (Figma). Apps: onda de 7 barrinhas que anda com o som (vira linha reta no mudo)
      (isFixed(id) ? '<button class="icon-btn mute"></button>'
        : '<button class="icon-btn mute wave-btn">' + WAVE + '</button>');

    const r = {
      id, el, name,
      iconEl: el.querySelector('.row-icon'),
      nameEl: el.querySelector('.row-name'),
      pct: el.querySelector('.row-pct'),
      sliderBox: el.querySelector('.slider'),        // desenho (trilho, preenchimento, bolinha)
      slider: el.querySelector('.slider-input'),     // input invisível que recebe o mouse
      meter: el.querySelector('.meter-fill'),
      mute: el.querySelector('.mute'),
      muted: false,
      dragging: false,
      peak: 0,
      iconSrc: undefined,                // diferente de null: o primeiro setIcon sempre desenha
    };

    r.nameEl.textContent = displayName(id, name);
    setIcon(r, iconCache.get(id));
    updateMuteUi(r);

    r.slider.addEventListener('pointerdown', () => {
      r.dragging = true;
      el.classList.add('dragging');                // bolinha cresce, número em destaque
    });
    r.slider.addEventListener('input', () => {
      const v = Number(r.slider.value);
      tick(r, v);
      setFill(r, v);
      post({ type: 'setVolume', id, value: v / 100 });
      if (r.muted && v > 0) {            // mexeu no slider -> tira o mute
        r.muted = false;
        updateMuteUi(r);
        post({ type: 'setMute', id, muted: false });
      }
    });
    r.mute.addEventListener('click', () => {
      r.muted = !r.muted;
      updateMuteUi(r);
      post({ type: 'setMute', id, muted: r.muted });
    });

    rows.set(id, r);
    return r;
  }

  function setIcon(r, src) {
    if (r.iconSrc === (src || null)) return;
    r.iconSrc = src || null;
    if (src) {
      r.iconEl.textContent = '';
      const img = document.createElement('img');
      img.src = src;
      img.alt = '';
      r.iconEl.appendChild(img);
    } else if (SYSTEM_ICONS[r.id]) {
      r.iconEl.innerHTML = SYSTEM_ICONS[r.id];
    } else {
      r.iconEl.textContent = (r.name[0] || '?').toUpperCase();
    }
  }

  // Som sutil: um "tic" a cada 5% (como os dentes de um botão giratório), no máximo um a cada 45 ms
  const TICK_STEP = 5;
  let lastTickAt = 0;
  function tick(r, v) {
    const step = Math.floor(v / TICK_STEP);
    const prev = Math.floor(Number(r.pct.textContent) / TICK_STEP);
    const now = performance.now();
    if (step !== prev && now - lastTickAt > 45) {
      lastTickAt = now;
      post({ type: 'tick' });
    }
  }

  // --fill vai de 0 a 100; o CSS anima a mudança (o slider desliza até o valor novo)
  function setFill(r, v) {
    r.sliderBox.style.setProperty('--fill', v);
    r.pct.textContent = String(v);
  }

  function updateMuteUi(r) {
    const mic = r.id === MIC;
    if (isFixed(r.id)) {                  // a onda (apps) só muda pelo CSS, com a classe .muted
      const html = r.muted ? ICON_MUTE : ICON_VOL;
      if (r.muteHtml !== html) { r.mute.innerHTML = html; r.muteHtml = html; }   // só troca se mudou
    }
    r.mute.title = t(mic ? (r.muted ? 'micUnmute' : 'micMute') : (r.muted ? 'unmute' : 'mute'));
    r.el.classList.toggle('muted', r.muted);
  }

  function updateRow(r, d, dim) {
    if (d.name && d.name !== r.name) { r.name = d.name; r.nameEl.textContent = displayName(r.id, d.name); }
    if (d.icon) { iconCache.set(r.id, d.icon); setIcon(r, d.icon); }
    r.muted = !!d.muted;
    updateMuteUi(r);
    if (!r.dragging) {                    // não mexe no slider enquanto o usuário arrasta
      const v = Math.round(d.volume * 100);
      r.slider.value = v;
      setFill(r, v);
    }
    r.el.classList.toggle('dim', !!dim);
  }

  /* ---------------- Mensagens do C# ---------------- */

  function applyState(s) {
    // Volume geral
    let master = rows.get(MASTER);
    if (!master) {
      master = createRow(MASTER, '');
      masterEl.appendChild(master.el);
    }
    updateRow(master, { volume: s.master.volume, muted: s.master.muted }, false);

    // Microfone padrão do Windows (o slider é o ganho). Sem microfone, a linha some.
    let mic = rows.get(MIC);
    if (s.mic) {
      if (!mic) {
        mic = createRow(MIC, '');
        micRowEl.appendChild(mic.el);
      }
      updateRow(mic, { volume: s.mic.volume, muted: s.mic.muted }, false);
      updateMicPicker(s.mic);
    }
    micEl.hidden = !s.mic;

    // Saída de áudio (fone, soundbar...): só aparece com mais de uma conectada (com uma só, não há o que trocar)
    const out = s.output;
    // (na 1ª leitura a lista vem vazia: mantém como estava, para o mixer não mudar de altura ao abrir)
    const showOut = !!out && (out.devices.length ? out.devices.length > 1 : !outEl.hidden);
    if (showOut) outPicker.update(out);
    outEl.hidden = !showOut;

    // Apps
    const ids = s.apps.map((a) => a.id);
    for (const [id, r] of [...rows]) {
      if (!isFixed(id) && !ids.includes(id)) { r.el.remove(); rows.delete(id); }
    }
    for (const a of s.apps) {
      const r = rows.get(a.id) || createRow(a.id, a.name);
      updateRow(r, a, !a.active);
    }

    // Reordena só se a ordem mudou (mexer no DOM durante o arrasto cancelaria o slider)
    const current = [...appsEl.children].map((c) => c.dataset.id);
    if (ids.join('|') !== current.join('|')) {
      ids.forEach((id, i) => {
        const r = rows.get(id);
        r.el.style.setProperty('--i', i + 1);
        appsEl.appendChild(r.el);
      });
    }

    emptyEl.hidden = ids.length > 0;
    appCountEl.textContent = String(ids.length);
    reportHeight();
  }

  /* ---------------- Escolha do microfone ---------------- */

  const ICON_CHECK =
    '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 8.5l3 3 6-7" fill="none" ' +
    'stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  // Lista para escolher o dispositivo padrão do Windows (microfone ou saída de áudio).
  // Mostra o atual e só (re)monta a lista quando os dispositivos conectados mudam.
  function makePicker(btn, currentEl, listEl, msgType) {
    const inner = listEl.firstElementChild;
    let sig = '';
    const setOpen = (open) => {
      listEl.classList.toggle('open', open);
      listEl.inert = !open;
      btn.setAttribute('aria-expanded', String(open));
    };
    btn.addEventListener('click', () => {
      const open = !listEl.classList.contains('open');
      closePickers();          // uma lista aberta por vez
      setOpen(open);
    });
    const update = (m) => {
      if (m.device) currentEl.textContent = m.device;   // vazio = ainda lendo (chega no próximo estado)
      const s = m.id + '|' + m.devices.map((d) => d.id + '=' + d.name).join('|');
      if (s === sig) return;
      sig = s;
      inner.textContent = '';
      for (const d of m.devices) {
        const b = document.createElement('button');
        b.className = 'mic-option';
        b.setAttribute('role', 'option');
        b.setAttribute('aria-selected', String(d.id === m.id));
        b.innerHTML = '<span></span>' + ICON_CHECK;
        b.firstChild.textContent = d.name;
        b.title = d.name;
        b.addEventListener('click', () => {
          // Resposta imediata na tela; o C# troca o padrão do Windows e manda o estado novo
          inner.querySelectorAll('.mic-option').forEach((o) => o.setAttribute('aria-selected', String(o === b)));
          currentEl.textContent = d.name;
          setOpen(false);
          post({ type: msgType, id: d.id });
        });
        inner.appendChild(b);
      }
    };
    return { update, setOpen };
  }
  const micPicker = makePicker(micPickBtn, micCurrentEl, micListEl, 'setMicDevice');
  const outPicker = makePicker($('outPickBtn'), $('outCurrent'), $('outList'), 'setOutputDevice');
  function closePickers() { micPicker.setOpen(false); outPicker.setOpen(false); }
  const updateMicPicker = (m) => micPicker.update(m);
  const setMicListOpen = () => closePickers();

  // Medidor do microfone: ignora o chiado de fundo (silêncio = barra parada) e,
  // como a voz tem nível baixo, usa a raiz para a barra crescer bem visível ao falar.
  const MIC_GATE = 0.01;   // aumente se a barra se mexer sem você falar
  const micLevel = (raw) => (raw > MIC_GATE ? Math.sqrt((raw - MIC_GATE) / (1 - MIC_GATE)) : 0);

  function applyPeaks(p) {
    const smooth = (r, raw) => {
      r.peak = raw > r.peak ? raw : r.peak * 0.85;   // sobe rápido, desce suave
      const lvl = Math.min(1, r.peak);
      r.meter.style.transform = 'scaleX(' + lvl + ')';
      // Onda do fim da linha: altura das barrinhas acompanha o som; parada e baixa sem som
      if (!isFixed(r.id)) {
        r.mute.style.setProperty('--lvl', lvl.toFixed(2));
        r.mute.classList.toggle('sounding', lvl > 0.02);
      }
    };
    const m = rows.get(MASTER);
    if (m) smooth(m, p.master);
    const mic = rows.get(MIC);
    if (mic) smooth(mic, micLevel(p.mic || 0));
    for (const [id, r] of rows) if (!isFixed(id)) smooth(r, p.apps[id] || 0);
  }

  function applySettings(s) {
    setLanguage(s.language || 'pt');
    if (typeof s.systemDark === 'boolean') systemDark = s.systemDark;   // tema do Windows (o C# lê)
    setTheme(s.theme);
    glassSupported = !!s.glass;
    setLite(!!s.lite);
    if (s.water) water = s.water;                   // lembrete de água: o contador de hoje
    showWater();
    setMicListOpen(false);                          // sempre abre com a lista de microfones fechada
    // Novidades: a partir da versão do NEWS, para quem ainda não as viu (quem já viu guardou uma versão igual
    // ou maior). Não depende do número exato publicado. A demo do site não manda versão: nunca mostra.
    appVersion = s.version || '';
    fromStore = !!s.store;   // instalado pela Microsoft Store: sem o aviso "está na Store"
    setNewsOpen(!!s.version && V.compareVersions(s.version, V.NEWS.version) >= 0 &&
      (!s.newsSeen || V.compareVersions(s.newsSeen, V.NEWS.version) < 0));
    forceResize = true;                             // o C# espera um "resize" para mostrar a janela
  }

  function replay(cls) {
    card.classList.remove('enter', 'leave');
    void card.offsetWidth;                          // reinicia a animação
    card.classList.add(cls);
  }

  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m.type) {
      case 'settings': applySettings(m); break;
      case 'systemTheme': systemDark = m.dark; applyScheme(); break;   // trocou o tema do Windows
      case 'state':    applyState(m);    break;
      case 'peaks':    applyPeaks(m);    break;
      case 'open':     replay('enter');  break;
      case 'close':    replay('leave');  break;
      case 'water':    water = m.water; showWater(); break;   // contador mudou por fora ("Bebi um copo" no aviso)
    }
  });

  /* ---------------- Tema e idioma (escolhidos na janela de configurações) ---------------- */

  function setTheme(mode) {
    root.dataset.theme = mode;
    applyScheme();
  }

  // Troca todos os textos do mixer para o idioma escolhido
  function setLanguage(value) {
    lang = V.I18N[value] ? value : 'pt';
    root.lang = V.langTag(lang);
    V.applyI18n(lang);
    rows.forEach((r) => {
      if (SYSTEM_NAMES[r.id]) r.nameEl.textContent = displayName(r.id, r.name);
      updateMuteUi(r);
    });
    showWater();
    if (newsOpen) renderNews();
  }

  // Tema efetivo (claro/escuro). "Sistema" segue o tema do Windows que o C# informa (systemDark);
  // sem C# (a demo do site), usa o prefers-color-scheme do navegador.
  let systemDark = null;
  function applyScheme() {
    const mode = root.dataset.theme;
    const sysLight = systemDark === null ? prefersLight.matches : !systemDark;
    const light = mode === 'light' || (mode === 'system' && sysLight);
    root.dataset.scheme = light ? 'light' : 'dark';
  }
  prefersLight.addEventListener('change', applyScheme);

  // Vidro só quando o Windows suporta e o modo leve está desligado
  let glassSupported = false;
  function setLite(on) {
    root.classList.toggle('lite', on);
    root.classList.toggle('glass', glassSupported && !on);
  }

  /* ---------------- Novidades (popup depois de atualizar) ---------------- */
  // Aparece uma vez, na primeira abertura depois de atualizar para V.NEWS.version (textos no shared.js).
  // Mascote em pixel art (o mesmo da ilha): ao lado do "Volum" no cabeçalho e no popup de novidades.
  // Desenho fixo do shared.js; as animações ficam no mascot.css.
  for (const id of ['brandMascot', 'newsMascot']) { const el = $(id); if (el && V.MASCOT_PX) el.innerHTML = V.MASCOT_PX; }

  const newsEl = $('news');
  let newsOpen = false;
  let fromStore = false;
  let appVersion = '';                              // versão instalada (vem no "settings"): é a que o popup mostra

  function renderNews() {
    if (!newsEl) return;
    const title = $('newsTitle');
    title.firstElementChild.textContent = `${t('newsEyebrow')} ${appVersion || V.NEWS.version}`;
    title.lastElementChild.textContent = t('newsTitle');
    $('newsList').replaceChildren(...V.newsItems(lang, fromStore));
    fitNews();
  }

  // O mixer cresce o bastante para o popup caber inteiro (com poucos apps, o "Entendi!" ficava fora da janela)
  function fitNews() {
    if (newsOpen) card.style.setProperty('--news-h', (newsEl.firstElementChild.offsetHeight + 28) + 'px');
  }

  function setNewsOpen(on) {
    if (!newsEl) return;                            // sem o popup no HTML (ex.: demo antiga): nada a fazer
    newsOpen = on;
    newsEl.hidden = !on;
    if (on) renderNews();
    card.classList.toggle('has-news', on);
  }

  function dismissNews() {
    setNewsOpen(false);
    post({ type: 'newsSeen' });                     // o C# guarda que esta versão já foi vista
  }
  $('newsOk')?.addEventListener('click', dismissNews);
  // Botão dentro de uma novidade (ex.: "Ver na Microsoft Store"): o C# abre o endereço
  $('newsList')?.addEventListener('click', (e) => {
    const b = e.target.closest('[data-site]');
    if (b) post({ type: 'openSite', section: b.dataset.site });
  });

  /* ---------------- Água bebida hoje ---------------- */
  // Quem lembra e guarda o contador é o C# (Water.cs). As opções ficam na janela de configurações.
  const waterEl = $('water');
  let water = { enabled: false, goalMl: 2000, intervalMin: 60, cupMl: 250, todayMl: 0 };
  const fmtMl = (ml) => V.fmtMl(ml, lang).replace(' ', '');   // compacto como no Figma: "2L", "150ml"

  function showWater() {
    if (!waterEl) return;                           // sem o contador no HTML (ex.: demo antiga)
    waterEl.hidden = !water.enabled;
    const done = water.todayMl >= water.goalMl;
    const amount = $('waterAmount');
    amount.textContent = done
      ? t('waterDone').replace('{0}', fmtMl(water.todayMl))
      : t('waterOf').replace('{0}', fmtMl(water.todayMl)).replace('{1}', fmtMl(water.goalMl));
    amount.classList.toggle('done', done);
    $('waterFill').style.width = Math.min(100, (water.todayMl / water.goalMl) * 100) + '%';
    $('waterPlus').textContent = '+ ' + fmtMl(water.cupMl);
    $('waterMinus').disabled = water.todayMl <= 0;
  }

  $('waterPlus')?.addEventListener('click', () => {
    water.todayMl += water.cupMl;
    showWater();
    post({ type: 'waterAdd', ml: water.cupMl });
    post({ type: 'tick' });                         // o mesmo "tic" do volume, para confirmar
  });

  $('waterMinus')?.addEventListener('click', () => {
    const ml = Math.min(water.cupMl, water.todayMl);
    if (!ml) return;
    water.todayMl -= ml;
    showWater();
    post({ type: 'waterAdd', ml: -ml });
  });

  /* ---------------- Botões do cabeçalho ---------------- */
  $('settingsBtn').addEventListener('click', () => post({ type: 'openSettings' }));   // o C# abre a janela de configurações
  $('supportBtn')?.addEventListener('click', () => post({ type: 'openSupport' }));    // coração: configurações → Apoiar   // o C# abre a janela de configurações
  $('minimizeBtn').addEventListener('click', () => post({ type: 'hide' }));
  $('feedbackBtn')?.addEventListener('click', () => post({ type: 'openSite', section: 'feedback' }));   // sugestões e bugs, no site
  $('kbLockBtn')?.addEventListener('click', () => post({ type: 'lockKeyboard' }));   // o C# fecha o mixer e trava o teclado

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    if (newsOpen) dismissNews();                    // com as novidades abertas, Esc fecha só o popup
    else post({ type: 'hide' });                    // Esc fecha o mixer (prático no meio de um jogo)
  });

  const endDrag = () => rows.forEach((r) => { r.dragging = false; r.el.classList.remove('dragging'); });
  window.addEventListener('pointerup', endDrag);
  window.addEventListener('pointercancel', endDrag);

  /* ---------------- Tamanho da janela ---------------- */
  // O C# redimensiona a janela para caber exatamente no conteúdo.

  let lastH = 0;
  let forceResize = true;

  function reportHeight() {
    const h = card.offsetHeight;                    // offsetHeight ignora transform (animações)
    if (h !== lastH || forceResize) {
      lastH = h;
      forceResize = false;
      post({ type: 'resize', height: h });
    }
  }
  new ResizeObserver(reportHeight).observe(card);

  document.addEventListener('contextmenu', (e) => e.preventDefault());

  post({ type: 'ready' });
})();
