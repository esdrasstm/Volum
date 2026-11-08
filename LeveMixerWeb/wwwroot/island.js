/* =========================================================
   Volum — ilha (island.html). Textos e formatação vêm do shared.js (window.Volum).
   Fala com o C# (IslandForm.cs) por mensagens JSON:
     C# -> JS : settings | mode | media | water | battery | notice
     JS -> C# : ready | height | hover | playPause | next | prev | openMixer | waterAdd | noticeAction | noticeClose
   ========================================================= */
(() => {
  'use strict';

  const V = window.Volum;
  const $ = (id) => document.getElementById(id);
  const post = (msg) => window.chrome.webview.postMessage(msg);
  const island = $('island');

  let lang = 'pt';
  const t = (key) => V.t(lang, key);
  let mode = 'closed';
  let modeSeq = 0;   // número da última troca de estado (volta no "animDone")
  let media = null;
  let water = { enabled: false, goalMl: 2000, cupMl: 250, todayMl: 0 };
  let battery = { present: false, percent: 100, charging: false };
  let soundOn = false;   // tem som saindo agora (o C# lê o medidor do Windows)

  /* ---------------- Hora e data (atualiza só na virada do minuto) ---------------- */
  // Data curta sem pontos, como "qua 4 nov 2026"
  function shortDate(d) {
    const parts = new Intl.DateTimeFormat(V.langTag(lang), { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
      .formatToParts(d).filter((p) => p.type !== 'literal').map((p) => p.value.replace('.', ''));
    return parts.join(' ');
  }

  let clockTimer = 0;
  function tickClock() {
    clearTimeout(clockTimer);
    const now = new Date();
    const time = now.toLocaleTimeString(V.langTag(lang), { hour: '2-digit', minute: '2-digit' });
    $('clock').textContent = time;
    $('time').textContent = time;
    $('date').textContent = shortDate(now);
    clockTimer = setTimeout(tickClock, (60 - now.getSeconds()) * 1000 + 50);
  }

  /* ---------------- Bateria ---------------- */
  function batterySvg() {
    const low = battery.percent <= 20 && !battery.charging;
    const color = low ? 'var(--battery-low)' : 'var(--battery)';
    const c = 2 * Math.PI * 8;
    const bolt = battery.charging
      ? '<path d="M10.8 5.5 7.6 10.6h2.6l-1 3.9 3.2-5.1h-2.6z" fill="' + color + '"/>'
      : '<text x="10" y="13" text-anchor="middle" font-size="7" font-weight="700" fill="' + color + '">' + battery.percent + '</text>';
    return '<svg viewBox="0 0 20 20"><circle cx="10" cy="10" r="8" fill="none" stroke="rgba(255,255,255,.18)" stroke-width="2"/>' +
      '<circle cx="10" cy="10" r="8" fill="none" stroke="' + color + '" stroke-width="2" stroke-linecap="round" ' +
      'stroke-dasharray="' + (c * battery.percent / 100) + ' ' + c + '" transform="rotate(-90 10 10)"/>' + bolt + '</svg>';
  }

  function showBattery() {
    const svg = batterySvg();   // feito aqui só com números: nada de fora entra no HTML
    for (const el of [$('battery'), $('miniBattery')]) el.innerHTML = svg;
    $('battery').hidden = !battery.present;
    showMini();
  }

  // Fechada, à esquerda: o anel do Pomodoro (rodando), a capa (com música), o mascote (ilha ociosa) ou a bateria
  function showMini() {
    const ring = pomoActive();
    const pet = !ring && idle;   // ociosa: só o mascote (a capa seria de uma música parada)
    const cover = !pet && !!(media && media.cover);   // com o Pomodoro rodando, aparecem os dois (anel e capa)
    $('miniRing').toggleAttribute('hidden', !ring);   // é um <svg>: a propriedade .hidden não vale nele
    $('miniCover').hidden = !cover;
    $('miniMascot').hidden = !pet;
    $('miniBattery').hidden = ring || cover || pet || !battery.present;
  }

  /* ---------------- Mascote ----------------
     O desenho do Esdras (rosto branco com os olhos em degradê, sem os cílios). Pisca sozinho (CSS);
     ao dar oi quando o PC liga, também pula e balança. */
  const MASCOT =
    '<svg class="mascot" viewBox="8 92 441 272" aria-hidden="true">' +
    '<rect class="m-face" x="12.26" y="97.78" width="432.3" height="261.24" rx="35.2" fill="#f9f9f9"/>' +
    '<g class="m-eyes">' +
      // Olhos normais (piscam) e olhos felizes em arquinho (^ ^): o CSS alterna entre os dois
      '<g class="m-normal">' +
      '<rect class="m-eye" x="80.94" y="149.37" width="49.87" height="111.03" rx="13.53" fill="url(#mEyeL)"/>' +
      '<rect class="m-eye" x="318.05" y="149.37" width="49.87" height="111.03" rx="13.53" fill="url(#mEyeR)"/></g>' +
      '<g class="m-happy"><path d="M78 222Q105.9 166 133.8 222"/><path d="M315 222Q343 166 371 222"/></g>' +
    '</g></svg>';
  /* ---------------- Pixel art (estilo 16 bits) ----------------
     Os desenhos são mapas de caracteres: cada letra é um pixel de uma cor da paleta e "." é vazio.
     Viram <rect> num SVG com bordas retas (crispEdges); a animação troca quadros inteiros (steps),
     como nos jogos antigos. As animações do mascote ficam no mascot.css. PIXEL_ART = false volta para o mascote vetorial acima. */
  const PIXEL_ART = true;

  // O desenho do mascote e a função de pixels ficam no shared.js (o mixer usa o mesmo mascote)
  const pixels = V.pixels, MASCOT_PX = V.MASCOT_PX;

  // Cena do aviso de levantar: mesa com monitor, cadeira e o bonequinho (cabelo, pele, camiseta, calça, tênis)
  const W_PAL = { H: '#5a3a28', S: '#f1c58f', E: '#1d1d1d', T: '#4f86c6', t: '#3a6aa3', P: '#3b3f58', K: '#8d8d99',
                  M: '#9aa3ad', B: '#5fa8e8', D: '#a0703f', d: '#7a5230', C: '#6c6c78', Z: '#ffffff',
                  Y: '#ffd34d', y: '#f2a93b', G: '#4caf50', g: '#2e7d32' };
  const HEAD_R = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SSSSES..', '..SSSSSS..', '...SSSS...'];   // olhando para a direita
  const HEAD_L = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SESSSS..', '..SSSSSS..', '...SSSS...'];   // para a esquerda
  const HEAD_Y = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SEESSE..', '..SSEESS..', '...SSSS...'];   // bocejando
  const W_FIG = {
    // Sentado olhando a mesa (à esquerda), mãos no teclado (dois quadros: digitando)
    sit1: [...HEAD_L, '..TTTTTT..', 'STTTTTTT..', '.tTTTTTT..', '..TTTTTT..', '..TTTTTT..', 'PPPPPPPP..', 'PPPPPPPP..', 'PP........', 'PP........', 'KKK.......'],
    sit2: [...HEAD_L, '..TTTTTT..', '.tTTTTTT..', 'STTTTTTT..', '..TTTTTT..', '..TTTTTT..', 'PPPPPPPP..', 'PPPPPPPP..', 'PP........', 'PP........', 'KKK.......'],
    // Levantando: ainda agachado, joelhos dobrados
    rise: ['..........', '..........', ...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '.STTTTTTS.', '..TTTTTT..', '..PPPPPP..', '.PP....PP.', '.PP....PP.', 'KKK....KKK'],
    stand: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '.tTTTTTTt.', '.STTTTTTS.', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    // Espreguiçando: braços abrindo (up0), lá em cima (up1) e bocejando (up2)
    up0: [...HEAD_R, 'SttTTTTttS', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    up1: ['.S.HHHH.S.', '.tHHHHHHt.', '.THSSSSHT.', '.TSSSSEST.', '..SSSSSS..', '...SSSS...', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    up2: ['S..HHHH..S', 't.HHHHHH.t', '.THSSSSHT.', '.TSEESSET.', '..SSEESS..', '...SSSS...', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    // Andando: passada aberta, junta, aberta trocada, junta (a cabeça desce 1 pixel na junta)
    w1: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', 'StTTTTTT..', '..TTTTTTS.', '..TTTTTT..', '..PPPPPP..', '.PPP..PPP.', 'PP......PP', 'PP......PP', 'KK......KK'],
    w2: ['..........', ...HEAD_R, '..TTTTTT..', '.TTTTTTT..', '.tTTTTTS..', '..TTTTTT..', '..PPPPPP..', '...PPPP...', '...PPPP...', '...PPPP...', '...KKKKK..'],
    w3: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '..TTTTTTtS', '.STTTTTT..', '..TTTTTT..', '..PPPPPP..', '.PPP..PPP.', 'PP......PP', 'PP......PP', 'KK......KK'],
  };
  const W_DESK = [
    '...MMMMMMMMM....', '...MBBBBBBBM....', '...MBBZBBBBM....', '...MBBBBBBBM....', '...MMMMMMMMM....', '.......MM.......',
    'DDDDDDDDDDDDDDDD', 'd..............d', 'd..............d', 'd..............d',
  ];
  // Sol (para onde ele vai andar, "tomar um sol") e um pouco de grama no chão
  const W_SUN = ['....y....', '.y..Y..y.', '...YYY...', '..YYYYY..', 'yYYYYYYYy', '..YYYYY..', '...YYY...', '.y..Y..y.', '....y....'];
  const W_GRASS = ['.G...G.....G..G...G....', 'gGG.gGG..GgG.GGg.gGG.Gg'];
  const W_CHAIR = ['.....C', '.....C', '.....C', '.....C', 'CCCCCC', 'C....C', 'C....C', 'C....C', 'C....C', 'C....C'];
  const FIG_PX = Object.fromEntries(Object.entries(W_FIG).map(([k, rows]) => [k, pixels(rows, W_PAL)]));
  // Escala 2: cada pixel vira 2×2 na ilha (54 px de altura = 27 pixels de cena; chão na linha 25)
  const WALK_PX =
    '<svg class="walk-scene px" id="walkScene" viewBox="0 0 200 27" aria-hidden="true">' +
    `<g class="w-props">${pixels(W_DESK, W_PAL, 4, 15)}${pixels(W_CHAIR, W_PAL, 22, 15)}` +
    `${pixels(W_SUN, W_PAL, 110, 2)}${pixels(W_GRASS, W_PAL, 88, 23)}</g>` +
    '<g id="wFig"></g></svg>';

  $('miniMascot').innerHTML = PIXEL_ART ? MASCOT_PX : MASCOT;   // desenhos fixos daqui: nada de fora entra no HTML
  if (PIXEL_ART) $('walkScene').outerHTML = WALK_PX;

  /* Roteiro da cena de levantar, tocado quadro a quadro (só enquanto o aviso está na tela):
     cada trecho tem quanto dura, onde ele está (x) e quais quadros mostra, em quantos quadros por segundo.
     Um só quadro na tela por vez (nada de duas poses juntas) e ele anda 1 pixel por vez. */
  const WALK_SCRIPT = [
    { ms: 1400, x: 17, frames: ['sit1', 'sit2'], fps: 4 },                                       // digitando
    { ms: 260, x: 18, frames: ['rise'] },                                                         // levantando
    { ms: 320, x: 18, frames: ['stand'] },
    { ms: 1700, x: 18, seq: [['up0', 160], ['up1', 300], ['up2', 560], ['up1', 280], ['up0', 160], ['stand', 240]] },   // espreguiça
    { ms: 4000, x: 18, to: 98, frames: ['w1', 'w2', 'w3', 'w2'], fps: 8 },                       // anda devagar até o sol
    { ms: 2000, x: 98, seq: [['stand', 420], ['up0', 120], ['up1', 700], ['up0', 120], ['stand', 640]] },               // curte o sol
  ];
  let walkRaf = 0;
  function playWalk() {
    cancelAnimationFrame(walkRaf);
    const bar = $('notice'), figEl = $('wFig');
    bar.classList.remove('scene-done');
    const t0 = performance.now();
    let last = '';
    const step = (now) => {
      if (bar.dataset.kind !== 'walk' || bar.hidden) return;   // o aviso fechou: para
      let t = now - t0, seg = null;
      for (const s of WALK_SCRIPT) { if (t < s.ms) { seg = s; break; } t -= s.ms; }
      if (!seg) { bar.classList.add('scene-done'); return; }    // acabou: some a cena e entram o texto e o botão
      let frame;
      if (seg.seq) {
        let u = t;
        frame = seg.seq[seg.seq.length - 1][0];
        for (const [k, d] of seg.seq) { if (u < d) { frame = k; break; } u -= d; }
      } else {
        frame = seg.frames[Math.floor(t / (1000 / (seg.fps || 1))) % seg.frames.length];
      }
      const x = seg.to ? Math.round(seg.x + (seg.to - seg.x) * (t / seg.ms)) : seg.x;
      const key = frame + '@' + x;
      if (key !== last) {   // só redesenha quando muda o quadro ou a posição
        last = key;
        figEl.setAttribute('transform', `translate(${x} 9)`);
        figEl.innerHTML = FIG_PX[frame];
      }
      walkRaf = requestAnimationFrame(step);
    };
    walkRaf = requestAnimationFrame(step);
  }

  // Mascote: quem decide quando ele aparece é o C# (mensagem "pet"): com o PC parado há 10 min (fica até
  // alguém mexer) e numa passadinha rápida a cada 30 min. Também logo depois do "bom dia".
  // Com ele na tela, a ilha desce um pouco (o C# muda o tamanho ao receber "idle").
  let idle = false, petTimer = 0;
  function rest(ms) {
    clearTimeout(petTimer);
    if (idle || pomoActive() || soundOn || mode !== 'closed') return;   // ilha ocupada: fica para a próxima
    idle = true;
    showMini();
    post({ type: 'idle', on: true });
    if (ms) petTimer = setTimeout(wake, ms);
  }

  // Alguma atividade na ilha (abriu, aviso, som, Pomodoro): o mascote sai e a ilha volta ao normal
  function wake() {
    clearTimeout(petTimer);
    if (idle) { idle = false; showMini(); post({ type: 'idle', on: false }); }
  }
  /* ---------------- Onda de áudio: só aparece (e anda) com som saindo de verdade ---------------- */
  function showWave() {
    for (const w of [$('wave'), $('miniWave')]) {
      w.hidden = !soundOn;
      w.classList.toggle('playing', soundOn);
    }
    if (soundOn) wake();   // som tocando: a ilha não está ociosa
  }

  /* ---------------- Onda ao vivo ----------------
     Com som saindo, o C# manda o nível (~10 por segundo). Cada barrinha estica conforme o nível, com um peso
     próprio que muda um pouco a cada vez (parece a música). Se o nível parar de chegar, volta a animação de sempre. */
  const WAVE_SHAPE = [0.55, 0.85, 1, 0.75, 0.5];
  let liveTimer = 0;
  function onLevel(v) {
    const lvl = Math.min(1, Math.sqrt(v) * 1.15);   // raiz: sons baixos ainda mexem a onda
    for (const w of [$('wave'), $('miniWave')]) {
      w.classList.add('live');
      [...w.children].forEach((bar, i) => {
        const s = 0.28 + lvl * WAVE_SHAPE[i] * (0.7 + Math.random() * 0.5);
        bar.style.transform = `scaleY(${Math.min(1, s).toFixed(2)})`;
      });
    }
    clearTimeout(liveTimer);
    liveTimer = setTimeout(() => {
      for (const w of [$('wave'), $('miniWave')]) { w.classList.remove('live'); [...w.children].forEach((b) => { b.style.transform = ''; }); }
    }, 700);
  }

  /* ---------------- Cor da música (tirada da capa) ----------------
     Lê a capa num quadradinho de 12×12 e pega a cor mais viva; deixa clara o bastante para aparecer no preto.
     Capa sem cor (preto e branco): volta ao branco. Roda uma vez por capa. */
  let tintFor = '';
  function tintFromCover(src) {
    if (!src || src === tintFor) return;
    tintFor = src;
    const img = new Image();
    img.onload = () => {
      try {
        const c = document.createElement('canvas');
        c.width = c.height = 12;
        const g = c.getContext('2d', { willReadFrequently: true });
        g.drawImage(img, 0, 0, 12, 12);
        const d = g.getImageData(0, 0, 12, 12).data;
        let best = null, bestScore = 0;
        for (let i = 0; i < d.length; i += 4) {
          const [h, s, l] = rgbToHsl(d[i], d[i + 1], d[i + 2]);
          const score = s * (1 - Math.abs(l - 0.5) * 1.6);   // vivo e nem muito escuro nem muito claro
          if (score > bestScore) { bestScore = score; best = [h, s, l]; }
        }
        if (!best || bestScore < 0.12) { island.style.removeProperty('--tint'); island.classList.remove('tinted'); return; }
        const [h, s] = best;
        island.style.setProperty('--tint', `hsl(${Math.round(h * 360)} ${Math.round(Math.max(0.55, s) * 100)}% 68%)`);
        island.classList.add('tinted');
      } catch { /* capa de outro endereço: fica branco */ }
    };
    img.src = src;
  }
  function rgbToHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
    if (max === min) return [0, 0, l];
    const dd = max - min, s = l > 0.5 ? dd / (2 - max - min) : dd / (max + min);
    const h = max === r ? (g - b) / dd + (g < b ? 6 : 0) : max === g ? (b - r) / dd + 2 : (r - g) / dd + 4;
    return [h / 6, s, l];
  }

  /* ---------------- Agenda ----------------
     O C# manda os próximos compromissos de hoje e amanhã (já em horário local, em ms). Montado com textContent:
     o título vem da agenda da pessoa e nunca vira HTML. */
  let agendaOn = false, agenda = [];
  function showAgenda() {
    const list = $('agendaList');
    const now = Date.now();
    const today = new Date().toDateString();
    const fmt = (ms) => new Date(ms).toLocaleTimeString(V.langTag(lang), { hour: '2-digit', minute: '2-digit' });
    const items = agenda.slice(0, 3).map((e) => {
      const li = document.createElement('li');
      const when = document.createElement('span');
      when.className = 'agenda-when';
      const tomorrow = new Date(e.start).toDateString() !== today;
      when.textContent = e.allDay ? t('agendaAllDay') : e.start <= now ? t('agendaNow') : (tomorrow ? t('agendaTomorrow') + ' ' : '') + fmt(e.start);
      if (!e.allDay && e.start <= now) li.classList.add('now');
      const title = document.createElement('span');
      title.className = 'agenda-title';
      title.textContent = e.title;
      li.append(when, title);
      return li;
    });
    if (!items.length) {
      const li = document.createElement('li');
      li.className = 'agenda-empty';
      li.textContent = t('agendaEmpty');
      items.push(li);
    }
    list.replaceChildren(...items);
  }

  /* ---------------- Clima ----------------
     O C# manda o clima de agora e as próximas 5 horas (Open-Meteo, códigos WMO). Ícones de traço, como os outros. */
  let weatherOn = false, weather = null;
  const WX_PATHS = {
    sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4"/>',
    moon: '<path d="M19.5 14.5A7.5 7.5 0 1 1 9.5 4.5a6 6 0 0 0 10 10z"/>',
    sunCloud: '<path d="M8 2.8v1.4M3.3 4.7l1 1M1.8 9.3h1.4M12.7 4.7l-1 1"/><path d="M4.7 12a3.6 3.6 0 0 1 6.8-3.9"/><path d="M9 20h8.5a3.5 3.5 0 0 0 .5-6.96A5 5 0 0 0 8.4 13.8 3.1 3.1 0 0 0 9 20z"/>',
    moonCloud: '<path d="M11.5 3.5a4.6 4.6 0 0 0-6.2 6.9"/><path d="M9 20h8.5a3.5 3.5 0 0 0 .5-6.96A5 5 0 0 0 8.4 13.8 3.1 3.1 0 0 0 9 20z"/>',
    cloud: '<path d="M7 18.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 11.6 3.5 3.5 0 0 0 7 18.5z"/>',
    fog: '<path d="M7 12.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 5.6"/><path d="M4 16h16M6 19.5h12"/>',
    drizzle: '<path d="M7 14.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 7.6 3.5 3.5 0 0 0 7 14.5z"/><path d="M9 18v.01M13 18v.01M11 21v.01M15 21v.01"/>',
    rain: '<path d="M7 14.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 7.6 3.5 3.5 0 0 0 7 14.5z"/><path d="M9 17.5l-1 3M13 17.5l-1 3M17 17.5l-1 3"/>',
    snow: '<path d="M7 14.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 7.6 3.5 3.5 0 0 0 7 14.5z"/><path d="M8.5 18.5v.01M12 18.5v.01M15.5 18.5v.01M10.2 21.5v.01M13.8 21.5v.01"/>',
    storm: '<path d="M7 14.5h10a4 4 0 0 0 .6-7.95A6 6 0 0 0 6.2 7.6 3.5 3.5 0 0 0 7 14.5z"/><path d="M12.5 16l-2 3h3l-2 3"/>',
  };
  // Código WMO → grupo (texto) e ícone (dia/noite)
  const wxGroup = (c) => c === 0 ? 'clear' : c === 1 ? 'mostly' : c === 2 ? 'partly' : c === 3 ? 'cloudy'
    : c === 45 || c === 48 ? 'fog' : c >= 51 && c <= 57 ? 'drizzle' : (c >= 61 && c <= 67) || (c >= 80 && c <= 82) ? 'rain'
    : (c >= 71 && c <= 77) || c === 85 || c === 86 ? 'snow' : c >= 95 ? 'storm' : 'cloudy';
  const wxIcon = (c, day) => {
    const g = wxGroup(c);
    const k = g === 'clear' ? (day ? 'sun' : 'moon') : g === 'mostly' || g === 'partly' ? (day ? 'sunCloud' : 'moonCloud') : g;
    return '<svg class="wx-svg" viewBox="0 0 24 24" aria-hidden="true">' + WX_PATHS[k] + '</svg>';
  };
  // A previsão vem em °C; com a unidade "f" (região que usa Fahrenheit ou escolhida nas configurações) converte aqui
  let wxUnit = 'c', wxShow = 'both';
  const deg = (v) => Math.round(wxUnit === 'f' ? v * 9 / 5 + 32 : v) + '°';

  // Ilha fechada: ícone do tempo de agora e a temperatura (ou só o ícone), à direita
  function showMiniWeather() {
    const el = $('miniWx'), w = weather;
    const on = weatherOn && !!w && wxShow !== 'off';
    el.hidden = !on;
    if (!on) return;
    $('miniWxIcon').innerHTML = wxIcon(w.code, w.day);
    $('miniWxTemp').textContent = wxShow === 'both' ? deg(w.temp) + (wxUnit === 'f' ? 'F' : 'C') : '';
    el.title = w.place + ' · ' + t('wx_' + wxGroup(w.code)) + ' · ' + deg(w.temp) + (wxUnit === 'f' ? 'F' : 'C');
    el.classList.toggle('icon-only', wxShow !== 'both');
  }
  function showWeather() {
    const w = weather;
    if (!w) return;
    $('wxPlace').textContent = w.place;
    $('wxTemp').textContent = deg(w.temp);
    $('wxIcon').innerHTML = wxIcon(w.code, w.day);
    $('wxCond').textContent = t('wx_' + wxGroup(w.code));
    $('wxRange').textContent = t('wxMax') + ' ' + deg(w.max) + '  ' + t('wxMin') + ' ' + deg(w.min);
    const hours = (w.hours || []).map((h) => {
      const li = document.createElement('li');
      const hr = document.createElement('span');
      hr.className = 'wx-hour';
      hr.textContent = lang === 'en' ? ((+h.hour % 12) || 12) + (+h.hour < 12 ? ' AM' : ' PM') : h.hour + 'h';
      const ic = document.createElement('span');
      ic.className = 'wx-hicon';
      ic.innerHTML = wxIcon(h.code, h.day);
      const tp = document.createElement('span');
      tp.className = 'wx-htemp';
      tp.textContent = deg(h.temp);
      li.append(hr, ic, tp);
      return li;
    });
    $('wxHours').replaceChildren(...hours);
  }

  /* ---------------- Microfone e câmera em uso ---------------- */
  function showPrivacy(m) {
    const mic = (m.mic || []).length > 0, cam = (m.cam || []).length > 0;
    const tip = [...(m.cam || []).map((a) => t('privCam') + ': ' + a), ...(m.mic || []).map((a) => t('privMic') + ': ' + a)].join('\n');
    for (const el of [$('privacy'), $('miniPrivacy')]) {
      el.hidden = !mic && !cam;
      el.classList.toggle('mic', mic);
      el.classList.toggle('cam', cam);
      el.title = tip;
    }
  }

  /* ---------------- Música ---------------- */
  const fmtTime = (s) => {
    s = Math.max(0, Math.floor(s));
    const m = Math.floor(s / 60), r = String(s % 60).padStart(2, '0');
    return m >= 60 ? `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}:${r}` : `${String(m).padStart(2, '0')}:${r}`;
  };

  // Posição agora: a que o player informou + o tempo que passou desde então (se está tocando)
  const position = () => {
    if (!media) return 0;
    const p = media.position + (media.playing ? (Date.now() - media.updatedAt) / 1000 : 0);
    return media.duration > 0 ? Math.min(p, media.duration) : p;
  };

  function showProgress() {
    const has = media && media.duration > 0;
    $('progress').hidden = !has;
    if (!has) return;
    seekBar.classList.toggle('seekable', !!media.canSeek);   // alguns players não deixam mudar o ponto
    if (seeking) return;                                     // enquanto arrasta, quem manda é o dedo
    const p = position();
    $('elapsed').textContent = fmtTime(p);
    $('remaining').textContent = '-' + fmtTime(media.duration - p);
    $('barFill').style.width = (p / media.duration) * 100 + '%';
  }

  // Arrastar (ou clicar) na barra do tempo muda o ponto da música
  const seekBar = document.querySelector('.progress .bar');
  let seeking = false;
  const seekAt = (e) => {
    const r = seekBar.getBoundingClientRect();
    return Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) * media.duration;
  };
  const seekShow = (s) => {
    $('elapsed').textContent = fmtTime(s);
    $('remaining').textContent = '-' + fmtTime(media.duration - s);
    $('barFill').style.width = (s / media.duration) * 100 + '%';
  };
  seekBar.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || !media || !media.canSeek || !(media.duration > 0)) return;
    e.stopPropagation();
    seeking = true;
    seekBar.classList.add('seeking');
    seekBar.setPointerCapture(e.pointerId);
    seekShow(seekAt(e));
  });
  seekBar.addEventListener('pointermove', (e) => { if (seeking) seekShow(seekAt(e)); });
  const endSeek = (e, apply) => {
    if (!seeking) return;
    seeking = false;
    seekBar.classList.remove('seeking');
    if (!apply) { showProgress(); return; }
    const s = seekAt(e);
    media.position = s;                 // resposta na hora; o player confirma depois
    media.updatedAt = Date.now();
    showProgress();
    post({ type: 'seek', s });
  };
  seekBar.addEventListener('pointerup', (e) => endSeek(e, true));
  seekBar.addEventListener('pointercancel', (e) => endSeek(e, false));

  // O tempo só anda com a ilha aberta e a música tocando
  let progressTimer = 0;
  function syncProgressTimer() {
    clearInterval(progressTimer);
    if (mode === 'open' && media && media.playing && media.duration > 0) progressTimer = setInterval(showProgress, 1000);
  }

  function showMedia() {
    const has = !!media;
    showWave();
    for (const img of [$('cover'), $('miniCover')]) {
      img.hidden = !(has && media.cover);
      if (has && media.cover && img.src !== media.cover) img.src = media.cover;
    }
    if (has && media.cover) tintFromCover(media.cover);
    else { tintFor = ''; island.style.removeProperty('--tint'); island.classList.remove('tinted'); }
    $('title').textContent = has ? media.title : t('islandNothing');
    $('subtitle').textContent = has ? [media.artist, media.album].filter(Boolean).join(' - ') || media.app : '';
    $('play').classList.toggle('playing', has && media.playing);
    $('play').disabled = !has;
    $('prev').disabled = !has || !media.canPrev;
    $('next').disabled = !has || !media.canNext;
    showMini();
    showProgress();
    syncProgressTimer();
    reportHeight();
  }

  $('play').addEventListener('click', () => post({ type: 'playPause' }));
  $('next').addEventListener('click', () => post({ type: 'next' }));
  $('prev').addEventListener('click', () => post({ type: 'prev' }));
  $('openMixer').addEventListener('click', () => post({ type: 'openMixer' }));

  /* ---------------- Água ---------------- */
  function showWater() {
    const done = water.todayMl >= water.goalMl;
    $('waterAmount').textContent = done
      ? t('waterDone').replace('{0}', V.fmtMl(water.todayMl, lang))
      : t('waterOf').replace('{0}', V.fmtMl(water.todayMl, lang)).replace('{1}', V.fmtMl(water.goalMl, lang));
    $('waterFill').style.width = Math.min(100, (water.todayMl / water.goalMl) * 100) + '%';
    $('waterPlus').textContent = '+ ' + V.fmtMl(water.cupMl, lang);
    $('waterMinus').disabled = water.todayMl <= 0;
    $('waterQuick').hidden = !water.enabled;
    $('waterQuick').title = '+ ' + V.fmtMl(water.cupMl, lang);
    showPages();
  }

  const addWater = (ml) => {
    water.todayMl = Math.max(0, water.todayMl + ml);
    showWater();
    post({ type: 'waterAdd', ml });
  };
  $('waterPlus').addEventListener('click', () => addWater(water.cupMl));
  $('waterQuick').addEventListener('click', () => addWater(water.cupMl));
  $('waterMinus').addEventListener('click', () => addWater(-Math.min(water.cupMl, water.todayMl)));

  /* ---------------- Pomodoro ----------------
     O C# manda o estado só quando muda (começou, pausou, trocou de fase). O tempo que falta é contado
     aqui: o anel enche pelo CSS (uma transição até o fim) e o número só anda com a ilha aberta. */
  let pomoOn = true;   // ligado nas configurações
  let pomo = { phase: 'idle', paused: false, leftMs: 0, totalMs: 25 * 60000, done: 0, every: 4, focusMin: 25 };
  let pomoEnds = 0;    // quando a fase acaba (relógio daqui)
  const pomoActive = () => pomoOn && pomo.phase !== 'idle';
  const pomoLeft = () => (pomo.phase === 'idle' ? pomo.totalMs : pomo.paused ? pomo.leftMs : Math.max(0, pomoEnds - Date.now()));
  const fmtClock = (ms) => {
    const s = Math.ceil(ms / 1000);
    return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
  };

  // Anel: começa onde está agora e, rodando, enche até o fim no tempo que falta
  function setRing(el, r) {
    const c = 2 * Math.PI * r;
    const left = pomoLeft(), total = pomo.totalMs || 1;
    el.style.strokeDasharray = c;
    el.style.transition = 'none';
    el.style.strokeDashoffset = pomo.phase === 'idle' ? c : c * (left / total);
    if (pomo.phase === 'idle' || pomo.paused || !left) return;
    void el.getBoundingClientRect();   // aplica a posição atual antes de animar
    el.style.transition = `stroke-dashoffset ${left}ms linear`;
    el.style.strokeDashoffset = 0;
  }

  function showFocusTime() {
    $('focusTime').textContent = fmtClock(pomoLeft());
  }

  let focusTimer = 0;
  function syncFocusTimer() {
    clearInterval(focusTimer);
    if (mode === 'open' && pomoActive() && !pomo.paused) focusTimer = setInterval(showFocusTime, 1000);
  }

  function showPomodoro() {
    const brk = pomo.phase === 'short' || pomo.phase === 'long';
    for (const el of [$('miniRing'), document.querySelector('.focus-ring')]) el.classList.toggle('break', brk);
    setRing($('miniRingFg'), 7.5);
    setRing($('focusRingFg'), 20);
    const phaseText = { idle: t('focusReady'), focus: t('focus'), short: t('focusShort'), long: t('focusLong') }[pomo.phase];
    $('focusPhase').textContent = pomo.paused ? `${phaseText} · ${t('focusPaused')}` : phaseText;
    $('focusCycle').textContent = pomo.phase === 'idle'
      ? t('focusReadyText').replace('{0}', pomo.focusMin)
      : t('focusCycle').replace('{0}', Math.min(pomo.done + (pomo.phase === 'focus' ? 1 : 0), pomo.every) || pomo.every).replace('{1}', pomo.every);
    const running = pomoActive() && !pomo.paused;
    const main = $('focusMain');
    main.classList.toggle('running', running);
    main.title = t(pomo.phase === 'idle' ? 'focusStart' : running ? 'focusPause' : 'focusResume');
    $('focusStop').disabled = pomo.phase === 'idle';
    $('focusSkip').disabled = pomo.phase === 'idle';
    showFocusTime();
    syncFocusTimer();
    showMini();
  }

  $('focusMain').addEventListener('click', () => {
    if (pomo.phase === 'idle') post({ type: 'pomoStart' });
    else post({ type: pomo.paused ? 'pomoResume' : 'pomoPause' });
  });
  $('focusStop').addEventListener('click', () => post({ type: 'pomoStop' }));
  $('focusSkip').addEventListener('click', () => post({ type: 'pomoSkip' }));

  function onPomodoro(m) {
    const started = pomo.phase === 'idle' && m.phase !== 'idle';
    pomo = m;
    pomoEnds = Date.now() + m.leftMs;
    if (started) goToPage('focus');   // começou um foco: abrir a ilha já mostra o Pomodoro
    showPomodoro();
  }

  /* ---------------- Páginas (setas ‹ › e bolinhas) ---------------- */
  let page = 0;
  const pages = () => [...document.querySelectorAll('.page')].filter((p) =>
    (p.dataset.page !== 'water' || water.enabled) && (p.dataset.page !== 'focus' || pomoOn) && (p.dataset.page !== 'agenda' || agendaOn) && (p.dataset.page !== 'weather' || weatherOn));
  function goToPage(name) {
    const i = pages().findIndex((p) => p.dataset.page === name);
    if (i >= 0) { page = i; showPages(); }
  }

  function showPages() {
    const list = pages();
    page = Math.min(page, list.length - 1);
    // Todas as páginas ficam empilhadas no mesmo lugar (só a atual aparece): a ilha aberta tem sempre
    // a altura da maior, e não muda de tamanho ao trocar de player para o Pomodoro ou para a água
    document.querySelectorAll('.page').forEach((p) => {
      p.hidden = !list.includes(p);          // desligada nas configurações: fora da conta
      p.classList.toggle('off', p !== list[page]);
    });
    const many = list.length > 1;
    $('pagePrev').hidden = !many;
    $('pageNext').hidden = !many;
    $('dots').replaceChildren(...(many ? list.map((_, i) => {
      const dot = document.createElement('i');
      dot.className = i === page ? 'on' : '';
      dot.addEventListener('click', () => { const step = i - page; if (!step) return; page = i; showPages(); slideIn(step); });
      return dot;
    }) : []));
    reportHeight();
  }

  // A página nova entra deslizando do lado de onde veio (anima só a que aparece)
  function slideIn(step) {
    const cur = pages()[page];
    if (!cur) return;
    cur.classList.remove('enter-next', 'enter-prev');
    void cur.offsetWidth;   // recomeça a animação
    cur.classList.add(step > 0 ? 'enter-next' : 'enter-prev');
  }
  const turn = (step) => { const n = pages().length; if (n < 2) return; page = (page + step + n) % n; showPages(); slideIn(step); };
  $('pagePrev').addEventListener('click', () => turn(-1));
  $('pageNext').addEventListener('click', () => turn(1));

  /* ---------------- Gestos ----------------
     Rodinha: volume geral (o C# muda e responde com "volume"). Deslizar para o lado (arrastando com o mouse
     ou com dois dedos no touchpad): no player troca a música; nas outras páginas, troca de página.
     Apertar e segurar: abre o mixer. */
  function swipe(dir) {   // dir 1 = próxima, -1 = anterior
    const cur = pages()[page];
    if (cur && cur.dataset.page === 'media' && media && media.title) {
      if (dir > 0 ? media.canNext !== false : media.canPrev !== false) post({ type: dir > 0 ? 'next' : 'prev' });
    } else turn(dir);
  }

  let wheelAcc = 0, swipeAcc = 0, swipeLock = 0;
  document.addEventListener('wheel', (e) => {
    if (mode !== 'open') return;
    e.preventDefault();
    if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) {   // touchpad: dois dedos para o lado
      swipeAcc += e.deltaX;
      if (Math.abs(swipeAcc) > 90 && Date.now() > swipeLock) { swipe(swipeAcc > 0 ? 1 : -1); swipeAcc = 0; swipeLock = Date.now() + 600; }
      return;
    }
    wheelAcc += e.deltaY;
    while (Math.abs(wheelAcc) >= 60) {   // um "clique" da rodinha (100) ≈ 2 passos de 2%
      const d = wheelAcc > 0 ? -1 : 1;
      wheelAcc += d * 60;
      post({ type: 'volStep', d });
    }
  }, { passive: false });

  let drag = null;
  const rubber = (dx) => Math.sign(dx) * Math.min(60, Math.abs(dx) * 0.4);   // a página vem junto, mas "pesada"
  $('full').addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || mode !== 'open' || e.target.closest('button, .dots, .bar.seekable')) return;
    const cur = pages()[page];
    drag = { x: e.clientX, dx: 0, moved: false, cur };
    drag.hold = setTimeout(() => {   // segurou sem arrastar: abre o mixer
      if (!drag || drag.moved) return;
      island.classList.remove('pressing');
      drag = null;
      post({ type: 'openMixer' });
    }, 550);
    island.classList.add('pressing');
    $('full').setPointerCapture(e.pointerId);
  });
  $('full').addEventListener('pointermove', (e) => {
    if (!drag) return;
    drag.dx = e.clientX - drag.x;
    if (!drag.moved && Math.abs(drag.dx) > 6) {
      drag.moved = true;
      clearTimeout(drag.hold);
      island.classList.remove('pressing');
      drag.cur?.classList.add('dragging');
    }
    if (drag.moved && drag.cur) drag.cur.style.transform = `translateX(${rubber(drag.dx)}px)`;
  });
  const endDrag = () => {
    if (!drag) return;
    clearTimeout(drag.hold);
    island.classList.remove('pressing');
    if (drag.cur) { drag.cur.classList.remove('dragging'); drag.cur.style.transform = ''; }   // volta com a mola
    if (drag.moved && Math.abs(drag.dx) > 50) swipe(drag.dx < 0 ? 1 : -1);
    drag = null;
  };
  $('full').addEventListener('pointerup', endDrag);
  $('full').addEventListener('pointercancel', endDrag);

  // Volume mudou pela rodinha (ou pelo teclado com a ilha aberta): aparece no lugar da data
  let volTimer = 0;
  function showVolume(m) {
    const st = document.querySelector('.status');
    $('volPct').textContent = m.muted ? '0%' : Math.round(m.v * 100) + '%';
    $('volFill').style.width = (m.muted ? 0 : m.v * 100) + '%';
    st.classList.add('showing-vol');
    clearTimeout(volTimer);
    volTimer = setTimeout(() => st.classList.remove('showing-vol'), 1300);
  }

  /* ---------------- Avisos do Volum ---------------- */
  const NOTICE_ICONS = {
    drop: '<svg viewBox="0 0 24 24"><path d="M12 2.8c3.6 4.4 6.2 7.8 6.2 11a6.2 6.2 0 0 1-12.4 0c0-3.2 2.6-6.6 6.2-11z" fill="#38bdf8"/></svg>',
    keyboard: '<svg viewBox="0 0 16 16"><g fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round"><rect x="1.75" y="4" width="12.5" height="8" rx="1.5"/><path d="M4.5 6.75h.01M7 6.75h.01M9.5 6.75h.01M12 6.75h.01M5.5 9.5h5"/></g></svg>',
    // Indicadores (Caps Lock, Num Lock, volume, microfone)
    caps: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M12 4 4.5 12H9v4h6v-4h4.5z"/><path d="M9 20h6"/></g></svg>',
    num: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="14" rx="3"/><path d="M10 9v6M8.5 10.2 10 9M13.5 9h2.5l-2.5 6"/></g></svg>',
    vol: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M11 5 6.5 9H4a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h2.5l4.5 4z"/><path d="M15.5 9a4.5 4.5 0 0 1 0 6M18.5 6.5a8.5 8.5 0 0 1 0 11"/></g></svg>',
    volmute: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M11 5 6.5 9H4a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h2.5l4.5 4z"/><path d="m16 9.5 5 5M21 9.5l-5 5"/></g></svg>',
    mic: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="2.5" width="6" height="11.5" rx="3"/><path d="M18.5 10.5a6.5 6.5 0 0 1-13 0M12 17v4"/></g></svg>',
    micoff: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ff6b6b" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="2.5" width="6" height="11.5" rx="3"/><path d="M18.5 10.5a6.5 6.5 0 0 1-13 0M12 17v4M4 3l16 18"/></g></svg>',
    // Teclas de mídia e brilho (no lugar dos avisos do Windows)
    play: '<svg viewBox="0 0 24 24"><path d="M8 5.5v13l10.5-6.5z" fill="#ffffff" stroke="#ffffff" stroke-width="1.6" stroke-linejoin="round"/></svg>',
    pause: '<svg viewBox="0 0 24 24"><g fill="#ffffff"><rect x="6.5" y="5" width="4" height="14" rx="1.2"/><rect x="13.5" y="5" width="4" height="14" rx="1.2"/></g></svg>',
    next: '<svg viewBox="0 0 24 24"><g fill="#ffffff"><path d="M4 6.5v11l7.5-5.5zM12 6.5v11l7.5-5.5z"/></g></svg>',
    prev: '<svg viewBox="0 0 24 24"><g fill="#ffffff"><path d="M20 6.5v11L12.5 12zM12 6.5v11L4.5 12z"/></g></svg>',
    // Atividades: carregador, bateria, fone, alto-falante, câmera
    charging: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#34d058" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="7" width="17" height="10" rx="2.5"/><path d="M21.5 10.5v3"/><path d="M11.5 8.8 9 12.2h3.2L10.5 15.4" stroke-width="1.5"/></g></svg>',
    battery: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="7" width="17" height="10" rx="2.5"/><path d="M21.5 10.5v3"/></g><rect x="5" y="9.5" width="8" height="5" rx="1" fill="#ffffff"/></svg>',
    headphones: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M4 14v-2a8 8 0 0 1 16 0v2"/><rect x="3.5" y="13.5" width="4.5" height="7" rx="1.8"/><rect x="16" y="13.5" width="4.5" height="7" rx="1.8"/></g></svg>',
    speaker: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="2.5" width="12" height="19" rx="2.5"/><circle cx="12" cy="14.5" r="3.2"/><path d="M12 6.8h.01"/></g></svg>',
    calendar: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="15.5" rx="3"/><path d="M3.5 10h17M8 3v4M16 3v4"/></g><circle cx="8.5" cy="14.5" r="1.3" fill="#ffffff"/></svg>',
    camera: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#30d158" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="2.5" y="6.5" width="13" height="11" rx="2.5"/><path d="m15.5 10.5 6-3.5v10l-6-3.5"/></g></svg>',
    bright: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4"/></g></svg>',
    timer: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="13.5" r="7.5"/><path d="M12 9.5v4l2.5 2M9.5 2.5h5M12 2.5V6"/></g></svg>',
    walk: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="13" cy="4" r="1.8"/><path d="M9 21l3-6 2.5 2.5V21M12 15l-1.5-5 3.5-2 2.5 3 2.5 1M10.5 10l-3 1.5-1 3"/></g></svg>',
    sparkle: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M10 3c.8 4.5 2.5 6.2 7 7-4.5.8-6.2 2.5-7 7-.8-4.5-2.5-6.2-7-7 4.5-.8 6.2-2.5 7-7z"/><path d="M19 15v6M16 18h6"/></g></svg>',
    bell: '<svg viewBox="0 0 16 16"><path d="M4 11V7a4 4 0 0 1 8 0v4l1 1.5H3zM6.5 14h3" fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  };

  let lastNotice = '';
  function showNotice(n) {
    const on = !!n.icon;
    if (!on && lastNotice === 'greet') setTimeout(() => rest(15000), 0);   // acabou o "bom dia": o mascote fica um pouquinho
    lastNotice = n.icon || '';
    island.classList.toggle('greeting', n.icon === 'greet');   // "bom dia": do tamanho da ilha com o mascote
    island.classList.toggle('walking', n.icon === 'walk');     // levantar: ilha mais alta, com a cena do bonequinho
    if (n.icon === 'walk') requestAnimationFrame(playWalk);   // a cena começa do início (depois de o aviso aparecer)
    $('notice').hidden = !on;
    if (!on) return;
    $('notice').dataset.kind = n.kind || n.icon;   // água: botão azul; o resto, branco; greet: só a frase; osd: indicador rápido
    island.classList.toggle('osd', n.kind === 'osd');
    island.classList.toggle('act', n.kind === 'act');   // atividade: faixa um pouco mais larga
    const bar = $('osdBar');   // volume: barrinha com o nível
    bar.hidden = !((n.kind === 'osd' || n.kind === 'act') && n.level >= 0);
    if (!bar.hidden) bar.firstElementChild.style.width = Math.round(n.level * 100) + '%';
    $('noticeIcon').innerHTML = n.icon === 'greet' ? '' : NOTICE_ICONS[n.icon] || NOTICE_ICONS.bell;   // ícones fixos daqui; o "bom dia" é só a frase
    if (n.img && /^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(n.img)) {   // notificação de outro app: o ícone dele (só imagem embutida)
      const img = document.createElement('img');
      img.src = n.img;
      img.alt = '';
      $('noticeIcon').replaceChildren(img);
    }
    $('noticeTitle').textContent = n.title || '';
    $('noticeBody').textContent = n.text || '';
    $('noticeAction').hidden = !n.action;
    $('noticeAction').textContent = n.action || '';
  }

  $('noticeAction').addEventListener('click', () => post({ type: 'noticeAction' }));
  $('noticeClose').addEventListener('click', () => post({ type: 'noticeClose' }));

  /* ---------------- Abrir e fechar ---------------- */
  // O C# cresce a janela e manda "mode"; aqui a pílula anima (CSS) até o tamanho novo.
  document.documentElement.addEventListener('mouseenter', () => post({ type: 'hover', on: true }));
  document.documentElement.addEventListener('mouseleave', () => post({ type: 'hover', on: false }));

  function setMode(m) {
    mode = m;
    island.classList.remove('tucked', 'closed', 'idle', 'alerting', 'open');
    island.classList.add(m === 'notice' ? 'alerting' : m);   // "notice" vira "alerting": a classe .notice já tem outro uso
    if (m === 'open') { showProgress(); showFocusTime(); }
    if (m !== 'closed' && m !== 'idle') wake();   // abriu, recolheu ou chegou aviso: zera o tempo ocioso
    syncProgressTimer();
    syncFocusTimer();
  }

  // Altura da ilha aberta (muda com a página e com a água): o C# usa para o tamanho da janela
  let lastH = 0;
  function reportHeight() {
    const h = Math.ceil($('full').offsetHeight);
    island.style.setProperty('--open-h', h + 'px');
    if (h && h !== lastH) { lastH = h; post({ type: 'height', h }); }
  }

  /* ---------------- Forma: mola física + cantos contínuos ----------------
     Cada estado define no CSS o tamanho-alvo (--w, --h, --r, --ear-size). Quando muda (classe da ilha, altura da
     página, formato), a mola leva a forma até lá; o requestAnimationFrame só roda enquanto ela se mexe.
     Rígida e pouco elástica, medida no Alcove: chega em ~250 ms e passa só ~2% do tamanho. Interrompida no meio
     (o mouse saiu), ela muda de direção sem tranco, porque continua da velocidade em que estava. */
  const SPRING_K = 380, SPRING_C = 28;   // rigidez e amortecimento (massa 1)
  const shapeBox = document.querySelector('.shape'), shapeSvg = document.querySelector('.shape-svg'), shapePath = $('shapePath');
  const KEYS = ['w', 'h', 'r', 'e'];
  let cur = null, goal = null, vel = { w: 0, h: 0, r: 0, e: 0 }, springRaf = 0, springT = 0;

  function readGoal() {
    const cs = getComputedStyle(island);
    const n = (p) => parseFloat(cs.getPropertyValue(p)) || 0;
    const floating = document.documentElement.classList.contains('floating');
    return { w: n('--w'), h: n('--h'), r: n('--r'), e: floating ? 0 : n('--ear-size') };
  }

  // Canto contínuo (como os da Apple): a curva começa mais longe do canto e aperta no fim,
  // em vez do quarto de círculo. p = quanto o canto "come" de cada lado; c = puxada das alças.
  function corner(r, maxP) { const p = Math.max(0, Math.min(r * 1.35, maxP)); return [p, p * 0.32]; }

  function drawShape() {
    const { w, h, r } = cur;
    const e = Math.max(0, cur.e);
    const floating = document.documentElement.classList.contains('floating');
    shapeSvg.setAttribute('width', Math.max(1, w + 2 * e));
    shapeSvg.setAttribute('height', Math.max(1, h));
    shapeSvg.style.left = (-e) + 'px';
    shapeBox.style.width = w + 'px';    // a caixa acompanha a mola (centralizada pelo CSS), o desenho vai junto
    shapeBox.style.height = h + 'px';
    const L = e, R = e + w;
    let d;
    if (floating) {   // pílula solta: quatro cantos contínuos
      const [p, c] = corner(r, Math.min(w, h) / 2);
      d = `M${L + p},0 H${R - p} C${R - c},0 ${R},${c} ${R},${p} V${h - p} C${R},${h - c} ${R - c},${h} ${R - p},${h}` +
          ` H${L + p} C${L + c},${h} ${L},${h - c} ${L},${h - p} V${p} C${L},${c} ${L + c},0 ${L + p},0 Z`;
    } else {          // grudada no topo: borda de cima reta, orelhas côncavas ligando à borda, cantos de baixo contínuos
      const [p, c] = corner(r, Math.min(w / 2, h));
      d = `M0,0 H${R + e} Q${R},0 ${R},${e} V${h - p} C${R},${h - c} ${R - c},${h} ${R - p},${h}` +
          ` H${L + p} C${L + c},${h} ${L},${h - c} ${L},${h - p} V${e} Q${L},0 0,0 Z`;
    }
    shapePath.setAttribute('d', d);
  }

  function springTo() {
    goal = readGoal();
    const still = document.documentElement.classList.contains('lite') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!cur || still) { cur = { ...goal }; drawShape(); post({ type: 'animDone', seq: modeSeq }); return; }
    if (!springRaf) { springT = performance.now(); springRaf = requestAnimationFrame(springStep); }
  }

  function springStep(now) {
    const dt = Math.min(0.032, (now - springT) / 1000);   // aba escondida e volta: não dá um salto
    springT = now;
    let moving = false;
    for (const k of KEYS) {
      const a = -SPRING_K * (cur[k] - goal[k]) - SPRING_C * vel[k];
      vel[k] += a * dt;
      cur[k] += vel[k] * dt;
      if (Math.abs(vel[k]) > 0.3 || Math.abs(cur[k] - goal[k]) > 0.3) moving = true;
    }
    if (!moving) {
      cur = { ...goal }; vel = { w: 0, h: 0, r: 0, e: 0 }; springRaf = 0;
      drawShape();
      post({ type: 'animDone', seq: modeSeq });   // o C# já pode deixar a janela do tamanho exato
      return;
    }
    drawShape();
    springRaf = requestAnimationFrame(springStep);
  }

  // Qualquer troca de estado, de altura da página aberta ou de formato reposiciona o alvo da mola
  new MutationObserver(springTo).observe(island, { attributes: true, attributeFilter: ['class', 'style'] });
  new MutationObserver(springTo).observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  springTo();

  /* ---------------- Mensagens do C# ---------------- */
  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m.type) {
      case 'settings':
        lang = V.I18N[m.language] ? m.language : 'pt';
        document.documentElement.lang = V.langTag(lang);
        document.documentElement.classList.toggle('lite', !!m.lite);
        document.documentElement.classList.toggle('floating', m.style === 'float');   // formato: pílula solta
        pomoOn = m.pomodoro !== false;
        V.applyI18n(lang);
        tickClock();
        showMedia();
        showWater();
        showPomodoro();
        break;
      case 'pomodoro': onPomodoro(m); break;
      case 'pet': if (m.on) rest(m.ms || 0); else wake(); break;   // mascote: PC parado ou passadinha
      case 'mode':    modeSeq = m.seq || 0; setMode(m.mode); break;
      case 'media': {
        const changed = media && m.media && media.title !== m.media.title;
        media = m.media; showMedia();
        if (changed && mode === 'open') { const t = document.querySelector('.track'); t.classList.remove('swap'); void t.offsetWidth; t.classList.add('swap'); }
        break;
      }
      case 'volume':  showVolume(m); break;
      case 'level':   onLevel(m.v); break;
      case 'privacy': showPrivacy(m); break;
      case 'agenda':  agendaOn = !!m.on; agenda = m.events || []; showAgenda(); showPages(); break;
      case 'weather':
        weatherOn = !!m.on; weather = m.weather;
        wxUnit = m.unit === 'f' ? 'f' : 'c'; wxShow = m.show || 'both';
        showWeather(); showMiniWeather(); showPages();
        break;
      case 'water':   water = m.water; showWater(); break;
      case 'battery': battery = m; showBattery(); break;
      case 'sound':   soundOn = m.on; showWave(); break;
      case 'notice':  showNotice(m); break;
    }
  });

  document.addEventListener('contextmenu', (e) => e.preventDefault());
  post({ type: 'ready' });
})();
