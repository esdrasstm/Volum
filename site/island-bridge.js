/* =========================================================
   Volum — ponte de demonstração da ILHA (só para a landing page)
   No app, o C# (IslandForm.cs) conversa com a ilha por window.chrome.webview.
   Aqui este arquivo faz esse papel: música tocando (capas desenhadas aqui, sem marcas), onda do som,
   Pomodoro, água, agenda, bateria, avisos (faixas) e o mascote quando ninguém mexe.
   A página de fora (index.html) manda ações pelos botões da barra de tarefas desenhada:
     { volumIsland: true, action: 'music' | 'water' | 'pomo' | 'calendar' | 'charger' | 'headphones' | 'camera' }
   e recebe { volumIsland: true, type: 'openMixer' } quando seguram a ilha.
   Precisa ser carregado ANTES do island.js.
   ========================================================= */
(() => {
  'use strict';

  const listeners = [];
  const send = (msg) => setTimeout(() => listeners.forEach((fn) => fn({ data: msg })), 0);
  const toPage = (msg) => { try { window.parent.postMessage(Object.assign({ volumIsland: true }, msg), '*'); } catch (e) { /* sem página de fora */ } };

  // ---------- Músicas de exemplo (capas em degradê desenhadas aqui) ----------
  const cover = (a, b, c) => 'data:image/svg+xml;utf8,' + encodeURIComponent(
    `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 40 40'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>` +
    `<stop offset='0' stop-color='${a}'/><stop offset='.55' stop-color='${b}'/><stop offset='1' stop-color='${c}'/></linearGradient></defs>` +
    `<rect width='40' height='40' fill='url(#g)'/><circle cx='28' cy='13' r='6' fill='#fff' opacity='.35'/>` +
    `<path d='M0 30 Q10 24 20 29 T40 27 V40 H0z' fill='#000' opacity='.25'/></svg>`);
  const SONGS = [
    { title: 'Noites de Verão', artist: 'Lua Azul', album: 'Ao Vivo', cover: cover('#ff8a65', '#ba68c8', '#283593'), duration: 214 },
    { title: 'Céu de Neon', artist: 'Os Satélites', album: 'Órbita', cover: cover('#22d3ee', '#6366f1', '#1e1b4b'), duration: 187 },
    { title: 'Café às Sete', artist: 'Marina Sol', album: 'Manhãs', cover: cover('#fde047', '#f97316', '#7c2d12'), duration: 241 },
    { title: 'Onda Leve', artist: 'Trio Maré', album: 'Litoral', cover: cover('#86efac', '#14b8a6', '#134e4a'), duration: 199 },
  ];
  let song = 0, playing = true, position = 52, positionAt = Date.now();
  const nowPos = () => Math.min(SONGS[song].duration, position + (playing ? (Date.now() - positionAt) / 1000 : 0));
  const mediaMsg = () => {
    const s = SONGS[song];
    return { type: 'media', media: { title: s.title, artist: s.artist, album: s.album, app: 'Player', playing, position: nowPos(),
      duration: s.duration, updatedAt: Date.now(), cover: s.cover, canPrev: true, canNext: true, canSeek: true } };
  };
  function changeSong(step) {
    song = (song + step + SONGS.length) % SONGS.length;
    position = 0; positionAt = Date.now(); playing = true;
    send(mediaMsg());
    syncSound();
  }
  function togglePlay() {
    position = nowPos(); positionAt = Date.now(); playing = !playing;
    send(mediaMsg());
    syncSound();
  }

  // Onda do som: com música tocando, manda o nível ~10 vezes por segundo (como o C# faz com o som de verdade)
  let levelTimer = 0, t = 0;
  function syncSound() {
    send({ type: 'sound', on: playing });
    clearInterval(levelTimer);
    if (!playing) return;
    levelTimer = setInterval(() => {
      t += 0.33;
      const v = 0.25 + 0.45 * Math.abs(Math.sin(t)) * (0.6 + Math.random() * 0.4) + (Math.random() < 0.15 ? 0.25 : 0);
      send({ type: 'level', v: Math.min(1, v) });
    }, 110);
  }

  // ---------- Estado ----------
  let mode = 'closed', seq = 0, hover = false, notice = null, noticeTimer = 0, volume = 0.6;
  const water = { enabled: true, goalMl: 2000, cupMl: 250, todayMl: 1250 };
  let pomo = { phase: 'idle', paused: false, leftMs: 0, totalMs: 1500000, done: 1, every: 4, focusMin: 25 };
  let pomoStartedAt = 0;
  const pomoMsg = () => Object.assign({ type: 'pomodoro' }, pomo,
    { leftMs: pomo.phase !== 'idle' && !pomo.paused ? Math.max(0, pomo.leftMs - (Date.now() - pomoStartedAt)) : pomo.leftMs });

  function setMode(m) {
    if (m === mode) return;
    mode = m;
    send({ type: 'mode', mode: m, seq: ++seq });
  }
  const resting = () => (hover && !notice ? 'open' : notice ? 'notice' : idle ? 'idle' : 'closed');

  // Faixa de aviso (água, Pomodoro, atividades) por alguns segundos
  function showNotice(n, ms) {
    notice = n;
    send(Object.assign({ type: 'notice', action: null, level: -1, text: '' }, n));
    setMode('notice');
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(endNotice, ms || 4000);
  }
  function endNotice() {
    clearTimeout(noticeTimer);
    if (!notice) return;
    notice = null;
    send({ type: 'notice', icon: null });
    setMode(resting());
  }

  // Mascote: depois de um tempo sem ninguém mexer, a ilha fica só com ele (no app: PC parado há 10 min)
  let idle = false, idleTimer = 0;
  function poke() {
    clearTimeout(idleTimer);
    if (idle) { idle = false; send({ type: 'pet', on: false }); if (mode === 'idle') setMode(resting()); }
    idleTimer = setTimeout(() => { if (!hover && !notice) { idle = true; send({ type: 'pet', on: true, ms: 0 }); } }, 25000);
  }

  // ---------- Hover só em cima da forma (a janela da demo é maior que a ilha) ----------
  function insideShape(x, y) {
    const shape = document.querySelector('.shape');
    if (!shape) return false;
    const r = shape.getBoundingClientRect();
    return x >= r.left - 6 && x <= r.right + 6 && y >= r.top - 4 && y <= r.bottom + 8;
  }
  function setHover(on) {
    if (on === hover) return;
    hover = on;
    poke();
    if (!notice) setMode(on ? 'open' : (idle ? 'idle' : 'closed'));
  }
  addEventListener('mousemove', (e) => setHover(insideShape(e.clientX, e.clientY)));
  document.addEventListener('mouseleave', () => setHover(false));
  addEventListener('blur', () => setHover(false));

  // ---------- Ações vindas da página (botões da barra de tarefas) ----------
  const ACTS = {
    music: () => { togglePlay(); showNotice({ icon: playing ? 'play' : 'pause', kind: 'osd', title: playing ? 'Tocando' : 'Pausado', text: SONGS[song].title }, 1800); },
    water: () => showNotice({ icon: 'drop', kind: 'drop', title: 'Hora da água', text: 'Beba um copo', action: '+250 ml' }, 7000),
    pomo: () => {
      pomo = { phase: 'focus', paused: false, leftMs: 25 * 60000, totalMs: 25 * 60000, done: 1, every: 4, focusMin: 25 };
      pomoStartedAt = Date.now();
      send(pomoMsg());
      showNotice({ icon: 'timer', kind: 'act', title: 'Foco', text: '25 min · bora!' }, 2600);
    },
    calendar: () => showNotice({ icon: 'calendar', kind: 'act', title: 'Em 10 min', text: 'Reunião de design' }, 5000),
    charger: () => showNotice({ icon: 'charging', kind: 'act', title: 'Carregando', text: '82%', level: 0.82 }, 2600),
    headphones: () => showNotice({ icon: 'headphones', kind: 'act', title: 'Fone conectado', text: 'Fone sem fio' }, 2600),
    camera: () => {
      camOn = !camOn;
      send({ type: 'privacy', mic: camOn ? ['Chamada'] : [], cam: camOn ? ['Chamada'] : [] });
      if (camOn) showNotice({ icon: 'camera', kind: 'act', title: 'Câmera em uso', text: 'Chamada' }, 2600);
    },
  };
  let camOn = false;
  addEventListener('message', (e) => {
    const m = e.data || {};
    if (!m.volumIsland || !m.action) return;
    poke();
    ACTS[m.action]?.();
  });

  // ---------- O "C#" ----------
  function onUi(m) {
    switch (m.type) {
      case 'ready': {
        const now = Date.now();
        send({ type: 'settings', language: 'pt', lite: false, pomodoro: true, style: 'float' });
        send({ type: 'battery', present: true, percent: 82, charging: false });
        send({ type: 'water', water });
        send(mediaMsg());
        syncSound();
        send(pomoMsg());
        // Clima de exemplo (no app: Open-Meteo, pela cidade das configurações)
        const hr = new Date().getHours();
        send({ type: 'weather', on: true, unit: 'c', show: 'both', weather: { place: 'São Paulo', temp: 24, code: 2, day: hr >= 6 && hr < 18, max: 27, min: 18,
          hours: [0, 1, 2, 3, 4].map((i) => {
            const h = (hr + 1 + i) % 24;
            return { hour: String(h).padStart(2, '0'), temp: [24, 25, 23, 21, 20][i], code: [2, 1, 3, 61, 61][i], day: h >= 6 && h < 18 };
          }) } });
        send({ type: 'agenda', on: true, events: [
          { title: 'Reunião de design', start: now + 40 * 60000, end: now + 100 * 60000, allDay: false },
          { title: 'Academia', start: now + 4 * 3600000, end: now + 5 * 3600000, allDay: false },
          { title: 'Aniversário da Ana', start: now + 20 * 3600000, end: now + 44 * 3600000, allDay: true },
        ] });
        send({ type: 'mode', mode: 'closed', seq: ++seq });
        // Dá oi ao abrir a página (no app: quando o PC liga)
        const h = new Date().getHours();
        setTimeout(() => showNotice({ icon: 'greet', kind: 'greet', title: h < 12 && h >= 5 ? 'Bom dia!' : h < 18 && h >= 12 ? 'Boa tarde!' : 'Boa noite!' }, 3200), 900);
        poke();
        break;
      }
      case 'hover': break;   // a demo decide o hover pela posição do mouse (acima)
      case 'playPause': togglePlay(); break;
      case 'next': changeSong(1); break;
      case 'seek': position = Math.min(SONGS[song].duration, Math.max(0, m.s)); positionAt = Date.now(); send(mediaMsg()); break;
      case 'prev': changeSong(-1); break;
      case 'volStep': volume = Math.min(1, Math.max(0, volume + m.d * 0.02)); send({ type: 'volume', v: volume, muted: false }); break;
      case 'openMixer': toPage({ type: 'openMixer' }); break;
      case 'waterAdd': water.todayMl = Math.max(0, water.todayMl + m.ml); send({ type: 'water', water }); break;
      case 'noticeAction': { const was = notice; endNotice(); if (was && was.icon === 'drop') onUi({ type: 'waterAdd', ml: 250 }); break; }
      case 'noticeClose': endNotice(); break;
      case 'pomoStart': ACTS.pomo(); break;
      case 'pomoPause': pomo.leftMs = pomoMsg().leftMs; pomo.paused = true; send(pomoMsg()); break;
      case 'pomoResume': pomo.paused = false; pomoStartedAt = Date.now(); send(pomoMsg()); break;
      case 'pomoSkip': case 'pomoStop': pomo = { phase: 'idle', paused: false, leftMs: 0, totalMs: 1500000, done: 1, every: 4, focusMin: 25 }; send(pomoMsg()); break;
      case 'idle': break;
    }
  }

  window.chrome = { webview: {
    postMessage: (m) => onUi(m),
    addEventListener: (type, fn) => { if (type === 'message') listeners.push(fn); },
  } };
})();
