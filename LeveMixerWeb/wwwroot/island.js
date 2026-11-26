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
  // Bateria (só notebook): o raio no meio e a borda verde, que diminui conforme descarrega (vermelha com 20% ou menos)
  function batterySvg() {
    // Cor pela carga, passando aos poucos: verde (100%) → amarelo-laranja (~50%) → vermelho (20% ou menos)
    const hue = Math.round(Math.max(0, Math.min(1, (battery.percent - 20) / 80)) * 110);
    const c = 'hsl(' + hue + ', 88%, ' + (hue > 60 ? 52 : 56) + '%)';
    const cr = 2 * Math.PI * 10;
    const bolt = battery.charging ? c : '#3f3f3f';   // raio colorido só carregando; fora da tomada, cinza (não parece que está carregando)
    return '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="none" stroke="rgba(135,135,135,.4)" stroke-width="1.8"/>' +
      '<circle cx="12" cy="12" r="10" fill="none" stroke="' + c + '" stroke-width="1.8" stroke-linecap="round" ' +
      'stroke-dasharray="' + (cr * battery.percent / 100).toFixed(2) + ' ' + cr.toFixed(2) + '" transform="rotate(-90 12 12)"/>' +
      '<path d="M13.2 5.2 7.6 13.1h3.9l-1 5.7 5.7-8h-4z" fill="' + bolt + '" stroke="' + bolt + '" stroke-width=".6" stroke-linejoin="round"/></svg>';
  }

  function showBattery() {
    const svg = batterySvg();   // feito aqui só com números: nada de fora entra no HTML
    for (const el of [$('battery'), $('miniBattery')]) el.innerHTML = svg;
    $('battery').hidden = !battery.present;
    showMini();
  }

  // Fechada, à esquerda: o anel do Pomodoro (rodando), a capa (com música), o mascote (ilha ociosa) ou a bateria
  function showMini() {
    const ring = false;   // Pomodoro: bolha ao lado da ilha (pomoBubble), não mais o anel à esquerda
    const pet = !ring && idle;   // ociosa: só o mascote (a capa seria de uma música parada)
    const cover = !pet && !!(media && media.cover);   // com o Pomodoro rodando, aparecem os dois (anel e capa)
    $('miniRing').toggleAttribute('hidden', !ring);   // é um <svg>: a propriedade .hidden não vale nele
    $('miniCover').hidden = !cover;
    $('miniMascot').hidden = !pet;
    $('miniBattery').hidden = pet || !battery.present;
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
     Com som saindo, o C# manda o nível (~10 por segundo). Cada barrinha tem um alvo (nível × peso próprio, com um
     pouco de acaso) e anda até ele quadro a quadro, sempre suave: sobe rápido e desce devagar (como um medidor de
     verdade). Se o nível parar de chegar, as barras descem devagar até o repouso, sem pular para outra animação.
     O requestAnimationFrame só roda enquanto alguma barra está se mexendo. */
  const WAVE_SHAPE = [0.45, 0.8, 1, 0.7, 0.9, 0.5];
  const WAVE_REST = 0.3;                      // altura das barras parada (fração da altura cheia)
  const waveCur = WAVE_SHAPE.map(() => WAVE_REST), waveTgt = WAVE_SHAPE.map(() => WAVE_REST);
  let liveTimer = 0, waveRaf = 0, waveT = 0;
  function onLevel(v) {
    const lvl = Math.min(1, Math.sqrt(v) * 1.15);   // raiz: sons baixos ainda mexem a onda
    WAVE_SHAPE.forEach((w, i) => { waveTgt[i] = Math.min(1, WAVE_REST + lvl * w * (0.65 + Math.random() * 0.45)); });
    for (const w of [$('wave'), $('miniWave')]) w.classList.add('live');
    clearTimeout(liveTimer);
    liveTimer = setTimeout(() => waveTgt.fill(WAVE_REST), 450);   // o nível parou de chegar: volta ao repouso, devagar
    if (!waveRaf) { waveT = performance.now(); waveRaf = requestAnimationFrame(waveStep); }
  }
  function waveStep(now) {
    // nunca negativo: o 1º quadro pode trazer um carimbo de antes do performance.now() que ligou a onda,
    // e com intervalo negativo as barras andavam para longe do alvo (davam um pulo)
    const dt = Math.max(0, Math.min(0.05, (now - waveT) / 1000));
    waveT = now;
    let moving = false;
    for (let i = 0; i < waveCur.length; i++) {
      const d = waveTgt[i] - waveCur[i];
      const k = 1 - Math.exp(-(d > 0 ? 22 : 9) * dt);   // sobe rápido, desce devagar (independe da taxa de quadros)
      waveCur[i] += d * k;
      if (Math.abs(d) > 0.004) moving = true;
    }
    for (const w of [$('wave'), $('miniWave')]) {
      if (w.hidden) continue;
      [...w.children].forEach((bar, i) => { bar.style.transform = 'scaleY(' + waveCur[i].toFixed(3) + ')'; });
    }
    waveRaf = moving ? requestAnimationFrame(waveStep) : 0;
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
    const k = g === 'clear' ? (day ? 'sun' : 'moon') : g === 'mostly' || g === 'partly' ? (day ? 'sunCloud' : 'moonCloud') : g === 'cloudy' ? 'cloud' : g;
    return '<svg class="wx-svg" viewBox="0 0 24 24" aria-hidden="true">' + WX_PATHS[k] + '</svg>';
  };
  // Ícones cheios (estilo SF Symbols, como no Figma): ilha fechada, botão do player e ícone grande da página do clima.
  // As próximas horas continuam com os de traço (pequenos).
  const CLOUD = (y) => '<path transform="translate(0 ' + y + ')" d="M7 17.5h10.6a4.4 4.4 0 0 0 .6-8.76A6.4 6.4 0 0 0 5.8 10a3.8 3.8 0 0 0 1.2 7.5z" fill="currentColor"/>';
  const DROPS = (r) => '<path d="M8.6 18.6l-1 2.6M12.6 18.6l-1 2.6M16.6 18.6l-1 2.6" fill="none" stroke="currentColor" stroke-width="' + r + '" stroke-linecap="round"/>';
  const CLOUD_P = 'M9.4 21h8.4a3.6 3.6 0 0 0 .5-7.17A5.2 5.2 0 0 0 8.4 14.6 3.2 3.2 0 0 0 9.4 21z';
  // Sol ou lua com a nuvem na frente: a máscara tira do sol a nuvem e uma folguinha em volta dela
  const GAP = (id, back) => '<mask id="' + id + '" maskUnits="userSpaceOnUse" x="0" y="0" width="24" height="24"><rect width="24" height="24" fill="#fff"/>' +
    '<path d="' + CLOUD_P + '" fill="#000" stroke="#000" stroke-width="2.8"/></mask><g mask="url(#' + id + ')">' + back + '</g><path d="' + CLOUD_P + '" fill="currentColor"/>';
  const WX_FILL = {
    sun: '<circle cx="12" cy="12" r="4.6" fill="currentColor"/><path d="M12 2.2v2.2M12 19.6v2.2M4.9 4.9l1.6 1.6M17.5 17.5l1.6 1.6M2.2 12h2.2M19.6 12h2.2M4.9 19.1l1.6-1.6M17.5 6.5l1.6-1.6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>',
    moon: '<path d="M20 14.6A8.2 8.2 0 0 1 9.4 4a8.2 8.2 0 1 0 10.6 10.6z" fill="currentColor"/>',
    // A nuvem recorta um vão no sol/lua por máscara (antes era um contorno preto, que aparecia nos temas com fundo colorido)
    sunCloud: GAP('wxGapSun', '<circle cx="8.5" cy="8.5" r="3.4" fill="currentColor"/><path d="M8.5 1.8v1.4M2.6 8.5h1.4M3.8 3.8l1 1M13.2 3.8l-1 1" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/>'),
    moonCloud: GAP('wxGapMoon', '<path d="M12.6 3.6a5.6 5.6 0 0 1-7.4 7.4A5.6 5.6 0 1 0 12.6 3.6z" fill="currentColor"/>'),
    cloud: CLOUD(1.5),
    fog: CLOUD(-2.5) + '<path d="M5 18.5h14M7.5 21.5h9" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>',
    drizzle: CLOUD(-2.5) + '<path d="M9 19.4v.1M13 19.4v.1M11 21.8v.1M15 21.8v.1" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"/>',
    rain: CLOUD(-2.5) + DROPS(2),
    snow: CLOUD(-2.5) + '<path d="M8.5 19.2v.1M12 19.2v.1M15.5 19.2v.1M10.2 22v.1M13.8 22v.1" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>',
    storm: CLOUD(-2.5) + '<path d="M12.8 15.6 9.6 20.2h2.8l-1.2 3.4 3.6-4.8h-2.8z" fill="currentColor"/>',
  };
  const wxIconFill = (c, day) => {
    const g = wxGroup(c);
    const k = g === 'clear' ? (day ? 'sun' : 'moon') : g === 'mostly' || g === 'partly' ? (day ? 'sunCloud' : 'moonCloud') : g === 'cloudy' ? 'cloud' : g;
    return '<svg class="wx-svg wx-fill" viewBox="0 0 24 24" aria-hidden="true">' + WX_FILL[k] + '</svg>';
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
    $('miniWxIcon').innerHTML = wxIconFill(w.code, w.day);
    $('miniWxTemp').textContent = wxShow === 'both' ? deg(w.temp) + (wxUnit === 'f' ? 'F' : 'C') : '';
    el.title = w.place + ' · ' + t('wx_' + wxGroup(w.code)) + ' · ' + deg(w.temp) + (wxUnit === 'f' ? 'F' : 'C');
    el.classList.toggle('icon-only', wxShow !== 'both');
  }
  function showWeather() {
    const w = weather;
    if (!w) return;
    $('wxPlace').textContent = w.place;
    $('wxTemp').textContent = deg(w.temp);
    $('wxIcon').innerHTML = wxIconFill(w.code, w.day);
    $('wxCond').textContent = t('wx_' + wxGroup(w.code));
    $('wxRange').textContent = deg(w.max) + ' / ' + deg(w.min);   // máxima / mínima (curto: a condição do tempo cabe inteira)
    $('wxRange').title = t('wxMax') + ' ' + deg(w.max) + ' · ' + t('wxMin') + ' ' + deg(w.min);
    const hours = (w.hours || []).map((h) => {
      const li = document.createElement('li');
      const hr = document.createElement('span');
      hr.className = 'wx-hour';
      hr.textContent = lang === 'en' ? ((+h.hour % 12) || 12) + (+h.hour < 12 ? ' AM' : ' PM')
        : ['pt', 'es', 'fr', 'it'].includes(lang) ? h.hour + 'h'
        : new Intl.DateTimeFormat(V.langTag(lang), { hour: 'numeric' }).format(new Date(2000, 0, 1, +h.hour));
      const ic = document.createElement('span');
      ic.className = 'wx-hicon';
      ic.innerHTML = wxIconFill(h.code, h.day);
      const tp = document.createElement('span');
      tp.className = 'wx-htemp';
      tp.textContent = deg(h.temp);
      li.append(hr, ic, tp);
      return li;
    });
    $('wxHours').replaceChildren(...hours);
  }

  /* ---------------- Microfone e câmera em uso ---------------- */
  let lastPrivacy = null;   // para refazer as dicas ao trocar de idioma
  function showPrivacy(m) {
    lastPrivacy = m;
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
  // Bolinha líquida: estica no sentido em que é arrastada (quanto mais rápido, mais) e volta redonda com mola
  const gooLast = new WeakMap();
  function goo(el, e, viaVar) {
    const now = performance.now(), last = gooLast.get(el);
    gooLast.set(el, { x: e.clientX, t: now });
    if (!last || now - last.t > 120) return;
    const vx = (e.clientX - last.x) / Math.max(1, now - last.t);   // px por ms
    const s = Math.min(0.7, Math.abs(vx) * 0.45);
    const sx = (1 + s).toFixed(3), sy = (1 - s * 0.35).toFixed(3);
    if (viaVar) { el.style.setProperty('--ksx', sx); el.style.setProperty('--ksy', sy); } else el.style.scale = sx + ' ' + sy;
    clearTimeout(el._gooT);
    el._gooT = setTimeout(() => { if (viaVar) { el.style.removeProperty('--ksx'); el.style.removeProperty('--ksy'); } else el.style.scale = ''; }, 70);
  }

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
  seekBar.addEventListener('pointermove', (e) => { if (seeking) { seekShow(seekAt(e)); goo(seekBar, e, true); } });
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
  // Capa da música: traz para a frente o app que está tocando (Spotify, navegador...)
  document.querySelector('.cover').addEventListener('click', () => { if (media) post({ type: 'showSource' }); });
  $('next').addEventListener('click', () => post({ type: 'next' }));
  $('prev').addEventListener('click', () => post({ type: 'prev' }));
  // Alto-falante do player: abre o seletor de volume no lugar dos controles (abrir o mixer continua no "apertar e segurar")
  $('muteBtn').addEventListener('click', () => openVol());
  function showMute(muted) {
    $('muteBtn').classList.toggle('muted', !!muted);
    $('vpMute').classList.toggle('muted', !!muted);
    $('vpMute').title = t(muted ? 'islandUnmute' : 'islandMute');
    if (volOpen) vpShow(vpVal, !!muted);
  }

  /* ---------------- Seletor de volume (volume geral do Windows) ----------------
     A barra entra no lugar dos controles do player; arrastar ou clicar muda o volume (manda setVolume no máximo
     a cada 40 ms), o alto-falante dela silencia, o ✕ fecha. Fecha sozinha depois de 5 s sem mexer ou quando a ilha fecha. */
  let volOpen = false, vpVal = 0, vpMuted = false, vpDrag = false, vpSent = 0, vpTimer = 0, vpPending = null;
  const controlsEl = document.querySelector('.controls');
  function vpShow(v, muted) {
    vpVal = Math.min(1, Math.max(0, v)); vpMuted = !!muted;
    const pct = Math.round((vpMuted ? 0 : vpVal) * 100);
    $('vpFill').style.width = pct + '%';
    $('vpKnob').style.left = pct + '%';
    $('vpPct').textContent = pct + '%';
    $('vpTrack').setAttribute('aria-valuenow', pct);
    $('vpMute').classList.toggle('muted', vpMuted);
  }
  function vpIdle() { clearTimeout(vpTimer); vpTimer = setTimeout(() => { if (!vpDrag) closeVol(); }, 5000); }
  function openVol() {
    if (volOpen) { closeVol(); return; }
    volOpen = true;
    // a barra usa a mesma cor da barra do tempo da música (acompanha o tema)
    const bf = getComputedStyle($('barFill'));
    $('vpFill').style.background = bf.backgroundImage !== 'none' ? bf.backgroundImage : bf.backgroundColor;
    controlsEl.classList.add('vol-open');
    $('volPanel').setAttribute('aria-hidden', 'false');
    post({ type: 'getVolume' });
    vpIdle();
  }
  function closeVol() {
    if (!volOpen) return;
    volOpen = false; vpDrag = false;
    clearTimeout(vpTimer);
    controlsEl.classList.remove('vol-open');
    $('volPanel').setAttribute('aria-hidden', 'true');
  }
  function vpSend(v, final) {
    const now = Date.now();
    if (!final && now - vpSent < 40) { vpPending = v; return; }
    vpSent = now; vpPending = null;
    post({ type: 'setVolume', v: Math.round(v * 100) / 100 });
  }
  function vpFromX(x) {
    const r = $('vpTrack').getBoundingClientRect();
    return Math.min(1, Math.max(0, (x - r.left) / r.width));
  }
  $('vpTrack').addEventListener('pointerdown', (e) => {
    if (e.button !== 0) return;
    e.stopPropagation();
    vpDrag = true;
    $('vpTrack').setPointerCapture(e.pointerId);
    $('vpTrack').classList.add('dragging');
    const v = vpFromX(e.clientX); vpShow(v, false); vpSend(v);
    vpIdle();
  });
  $('vpTrack').addEventListener('pointermove', (e) => {
    if (!vpDrag) return;
    const v = vpFromX(e.clientX); vpShow(v, false); vpSend(v);
    goo($('vpKnob'), e);
  });
  const vpEnd = (e) => {
    if (!vpDrag) return;
    vpDrag = false;
    $('vpTrack').classList.remove('dragging');
    vpSend(vpPending ?? vpFromX(e.clientX), true);
    vpIdle();
  };
  $('vpTrack').addEventListener('pointerup', vpEnd);
  $('vpTrack').addEventListener('pointercancel', vpEnd);
  $('vpMute').addEventListener('click', () => { post({ type: 'toggleMute' }); vpIdle(); });
  $('vpClose').addEventListener('click', () => closeVol());
  $('volPanel').addEventListener('pointerdown', (e) => e.stopPropagation());   // não vira arrastar página / segurar a ilha

  /* ---------------- Água ---------------- */
  function showWater() {
    const done = water.todayMl >= water.goalMl;
    $('waterAmount').textContent = done
      ? t('waterDone').replace('{0}', V.fmtMl(water.todayMl, lang))
      : t('waterOf').replace('{0}', V.fmtMl(water.todayMl, lang)).replace('{1}', V.fmtMl(water.goalMl, lang));
    $('waterFill').style.width = Math.min(100, (water.todayMl / water.goalMl) * 100) + '%';
    $('waterPlus').textContent = '+ ' + V.fmtMl(water.cupMl, lang);
    $('waterMinus').disabled = water.todayMl <= 0;
    $('waterQuick').title = '+ ' + V.fmtMl(water.cupMl, lang);
    showQuick();
    showPages();
  }
  // Direita do player: o que está ativo agora. Pomodoro rodando = a bolha de fora, que entra na ilha e para aqui
  // quando ela abre no player; sem nada ativo, um espaço vazio do mesmo tamanho, para voltar, tocar/pausar e avançar
  // ficarem no centro (o +1 copo saiu daqui, pedido do Esdras; a água continua na página Água)
  function showQuick() {
    const pq = pomoActive();
    $('pomoQuick').classList.toggle('gone', !pq);
    $('waterQuick').classList.toggle('gone', pq);
    $('waterQuick').hidden = true;
    const list = pages();
    island.classList.toggle('quick-pomo', pq && list[Math.min(page, list.length - 1)]?.dataset.page === 'media');
    aimBubble();
  }
  $('pomoQuick').addEventListener('click', () => goToPage('focus'));

  // Ferramentas da ilha aberta (embaixo à esquerda): configurações (abre na página Ilha) e, com 2 monitores ou mais,
  // passar a ilha para o próximo monitor (o C# salva a escolha)
  let monitors = { count: 1, index: 1 };
  function showMonitors(m) {
    if (m) monitors = m;
    const b = $('monBtn');
    b.hidden = !(monitors.count > 1);
    b.title = t('islandNextMon').replace('{0}', monitors.index).replace('{1}', monitors.count);
    b.setAttribute('aria-label', b.title);
  }
  $('gearBtn').addEventListener('click', () => post({ type: 'openSettings' }));
  $('monBtn').addEventListener('click', () => post({ type: 'nextMonitor' }));

  // Centro de um elemento da ilha aberta em coordenadas do #island, pelo layout (sem as animações).
  // O .full fica centralizado com translateX(-50%); o resto não tem transformação parado.
  function centerIn(el) {
    const full = $('full');
    if (!el || !el.offsetWidth) return null;
    let x = el.offsetWidth / 2, y = el.offsetHeight / 2;
    for (let n = el; n && n !== full; n = n.offsetParent) { x += n.offsetLeft; y += n.offsetTop; }
    return { x: x + full.offsetLeft - full.offsetWidth / 2, y: y + full.offsetTop };
  }
  const aimBubble = () => blobTo();

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
    $('pomoChipTime').textContent = fmtClock(pomoLeft());
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
    setRing($('bubbleRingFg'), 14);
    setRing($('chipRingFg'), 8);
    setRing($('quickRingFg'), 14);
    for (const el of [$('pomoBubble'), $('pomoChip'), $('pomoQuick')]) el.classList.toggle('break', brk);
    // Bolha ao lado da ilha fechada: o C# alarga a janela para a direita enquanto ela está na tela
    const on = pomoActive();
    if (on !== island.classList.contains('pomo-on')) { island.classList.toggle('pomo-on', on); post({ type: 'bubble', on }); }
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
    showQuick();
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

  /* ---------------- Tarefas ----------------
     Lista curta: anotar, riscar e apagar. Os dados ficam no C# (todo.json); aqui só desenha e avisa.
     A ilha nunca pega o teclado (para não roubar o foco dos apps): ao clicar no campo, o JS manda todoFocus e o C#
     traz a janela da ilha para a frente só enquanto a pessoa digita; ela também não fecha nesse meio-tempo. */
  /* ---------------- Aparelhos sem fio (Devices.cs) ----------------
     Fone, mouse, teclado, caixinha e controle conectados, com a bateria que eles informam ao Windows.
     Ícones cheios (como as abas); o recorte usa --tab-cut (preto no fundo da ilha). */
  const CUT = 'var(--tab-cut, #000)';
  const DEV_ICONS = {
    headphones: '<svg viewBox="0 0 24 24"><path d="M4 15v-3a8 8 0 0 1 16 0v3" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><rect x="3" y="13" width="5" height="8" rx="2" fill="currentColor"/><rect x="16" y="13" width="5" height="8" rx="2" fill="currentColor"/></svg>',
    mouse: '<svg viewBox="0 0 24 24"><rect x="6" y="2.5" width="12" height="19" rx="6" fill="currentColor"/><path d="M12 6.2v3.6" stroke="' + CUT + '" stroke-width="2" stroke-linecap="round"/></svg>',
    keyboard: '<svg viewBox="0 0 24 24"><rect x="2" y="6" width="20" height="12" rx="3" fill="currentColor"/><path d="M6 10h.01M9.3 10h.01M12.6 10h.01M15.9 10h.01M18 10h.01M8 14h8" stroke="' + CUT + '" stroke-width="1.8" stroke-linecap="round"/></svg>',
    speaker: '<svg viewBox="0 0 24 24"><rect x="5.5" y="2.5" width="13" height="19" rx="3.2" fill="currentColor"/><circle cx="12" cy="14.6" r="3.4" fill="' + CUT + '"/><circle cx="12" cy="7" r="1.3" fill="' + CUT + '"/></svg>',
    gamepad: '<svg viewBox="0 0 24 24"><path d="M7.5 7h9a5 5 0 0 1 4.8 3.6l1 3.7a2.8 2.8 0 0 1-4.8 2.6L15.6 15H8.4l-1.9 1.9a2.8 2.8 0 0 1-4.8-2.6l1-3.7A5 5 0 0 1 7.5 7z" fill="currentColor"/><path d="M7.5 9.8v3.4M5.8 11.5h3.4" stroke="' + CUT + '" stroke-width="1.6" stroke-linecap="round"/><circle cx="16.4" cy="11.5" r="1.25" fill="' + CUT + '"/></svg>',
    device: '<svg viewBox="0 0 24 24"><path d="M7 7.5l10 9-5 4.5V3l5 4.5-10 9" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  };
  let devicesOn = true, devices = [];
  // Reconhecidos, mas sem bateria que o Volum saiba ler ainda: só listados na página (sem anel, sem aviso)
  let devUnsup = [];
  // Cor da bateria igual à do notebook: verde (100%) → amarelo-laranja → vermelho (20% ou menos)
  const battColor = (p) => { const hue = Math.round(Math.max(0, Math.min(1, (p - 20) / 80)) * 110); return 'hsl(' + hue + ', 88%, ' + (hue > 60 ? 52 : 56) + '%)'; };
  // Ilha fechada: até 2 aparelhos, cada um com o ícone dentro de um anel que mostra a carga (como a bateria do notebook)
  function miniDevicesSvg(d) {
    const p = Math.max(0, Math.min(100, Number(d.battery) || 0)), cr = 2 * Math.PI * 10;
    return '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="none" stroke="rgba(135,135,135,.4)" stroke-width="1.8"/>' +
      '<circle cx="12" cy="12" r="10" fill="none" stroke="' + battColor(p) + '" stroke-width="1.8" stroke-linecap="round" ' +
      'stroke-dasharray="' + (cr * p / 100).toFixed(2) + ' ' + cr.toFixed(2) + '" transform="rotate(-90 12 12)"/>' +
      '<g transform="translate(6.5 6.5) scale(.458)">' + (DEV_ICONS[d.kind] || DEV_ICONS.device).replace(/^<svg[^>]*>|<\/svg>$/g, '') + '</g></svg>';
  }
  let miniDevMax = 2;
  function showMiniDevices() {
    const box = $('miniDevs'), list = devicesOn ? devices.slice(0, miniDevMax) : [];
    if (box.hidden !== !list.length) box.hidden = !list.length;   // só quando muda (o observador do fit não entra em laço)
    box.replaceChildren(...list.map((d) => {
      const s = document.createElement('span');
      s.className = 'mini-dev';
      s.title = d.name + ' · ' + d.battery + '%';   // nome do aparelho só no título (texto)
      s.innerHTML = miniDevicesSvg(d);              // desenho feito aqui só com números e ícones fixos
      return s;
    }));
  }

  // Só os que cabem: a direita da ilha fechada também tem o clima e a onda. Confere de novo quando eles aparecem/somem.
  function fitMiniDevices() {
    // Cabe se nada encosta na hora: a esquerda (capa, bateria, tarefas) e a direita (aparelhos, clima, onda)
    // (comparado com a ilha sem aparelhos: o que já estava apertado à esquerda não é culpa deles)
    const leftGap = () => $('clock').getBoundingClientRect().left - document.querySelector('.mini-left').getBoundingClientRect().right;
    const fits = (base) => {
      const c = $('clock').getBoundingClientRect(), r = document.querySelector('.mini-right'), rr = r.getBoundingClientRect();
      return leftGap() >= base - 0.5 && rr.left >= c.right + 4 && r.scrollWidth <= r.clientWidth + 1;
    };
    miniDevMax = 0; showMiniDevices();
    const base = leftGap();
    for (let n = Math.min(2, devices.length); n > 0; n--) { miniDevMax = n; showMiniDevices(); if (fits(base)) return; }
    miniDevMax = 0; showMiniDevices();
  }
  // Refaz quando algo da ilha fechada aparece/some (capa, tarefas, clima, onda). As mudanças que o próprio fit faz
  // são descartadas (takeRecords), senão ele se chamaria sem parar.
  let fitRaf = 0;
  const fitObs = new MutationObserver((recs) => {
    // A onda pulsando (classe "live") não muda o espaço; ela aparecer/sumir ("playing") muda
    const same = (m) => m.target.closest && m.target.closest('.wave') && m.attributeName === 'class' &&
      /\bplaying\b/.test(m.oldValue || '') === m.target.classList.contains('playing');
    if (recs.every(same)) return;
    if (!fitRaf) fitRaf = requestAnimationFrame(() => { fitRaf = 0; fitMiniDevices(); fitObs.takeRecords(); }); });
  fitObs.observe(document.querySelector('.mini'), { attributes: true, subtree: true, attributeOldValue: true, attributeFilter: ['class', 'hidden'] });

  function showDevices() {
    fitMiniDevices();
    $('devsIcon').innerHTML = DEV_ICONS.headphones;
    const total = devices.length + devUnsup.length;   // conectados: com e sem bateria legível
    $('devCount').textContent = total === 1 ? t('devCount1') : t('devCountN').replace('{0}', total);
    $('devList').replaceChildren(...devices.map((d) => {
      const li = document.createElement('li');
      const ico = document.createElement('span');
      ico.className = 'dev-ico';
      ico.innerHTML = DEV_ICONS[d.kind] || DEV_ICONS.device;   // ícone daqui; o nome do aparelho entra só como texto
      const name = document.createElement('span');
      name.className = 'dev-name'; name.textContent = d.name; name.title = d.name;
      const bar = document.createElement('span');
      bar.className = 'dev-bar';
      const fill = document.createElement('i');
      const p = Math.max(0, Math.min(100, Number(d.battery) || 0));
      fill.style.width = p + '%'; fill.style.background = battColor(p);
      bar.append(fill);
      const pct = document.createElement('b');
      pct.className = 'dev-pct'; pct.textContent = p + '%';
      if (p <= 20) li.classList.add('low');
      li.append(ico, name, bar, pct);
      return li;
    }), ...devUnsup.map((d) => {   // depois dos que têm bateria (a lista mostra 3 linhas e rola)
      const li = document.createElement('li');
      li.className = 'unsup';
      const ico = document.createElement('span');
      ico.className = 'dev-ico';
      ico.innerHTML = DEV_ICONS[d.kind] || DEV_ICONS.device;
      const name = document.createElement('span');
      name.className = 'dev-name'; name.textContent = d.name; name.title = d.name;
      const note = document.createElement('span');
      note.className = 'dev-unsup'; note.textContent = t('devUnsupported'); note.title = note.textContent;
      li.append(ico, name, note);
      return li;
    }));
  }

  let todoOn = true, todos = [], todoSeen = new Set();
  const todo = $('todo'), todoInput = $('todoInput');
  const CHECK_SVG = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 8.5l3 3 6-7" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const DEL_SVG = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>';
  function showTodo() {
    const sorted = [...todos].sort((a, b) => (a.done - b.done) || (a.created - b.created));
    const done = todos.filter((x) => x.done).length;
    $('todoCount').textContent = todos.length ? t('todoCount').replace('{0}', done).replace('{1}', todos.length) : t('todoEmpty');
    $('todoClear').hidden = !done;
    const pending = todos.length - done;
    $('miniTodo').hidden = !(todoOn && pending > 0);
    $('miniTodoN').textContent = pending;
    $('miniTodo').title = t('todoPending').replace('{0}', pending);
    const first = todoSeen.size === 0;
    $('todoList').replaceChildren(...sorted.map((item) => {
      const li = document.createElement('li');
      li.classList.toggle('done', !!item.done);
      if (!first && !todoSeen.has(item.id)) li.classList.add('enter');   // tarefa nova: entra caindo, como gota
      const check = document.createElement('button');
      check.className = 'todo-check'; check.type = 'button'; check.innerHTML = CHECK_SVG;
      check.title = t('todoCheck'); check.setAttribute('aria-label', t('todoCheck'));
      check.setAttribute('aria-pressed', item.done ? 'true' : 'false');
      check.addEventListener('click', () => {   // marca na hora (sem mexer no objeto recebido) e avisa o C#
        todos = todos.map((x) => (x.id === item.id ? { ...x, done: !x.done } : x));
        showTodo();
        post({ type: 'todoToggle', id: item.id });
      });
      const txt = document.createElement('span');
      txt.className = 'todo-txt'; txt.textContent = item.text; txt.title = item.text;
      const del = document.createElement('button');
      del.className = 'todo-del'; del.type = 'button'; del.innerHTML = DEL_SVG;
      del.title = t('todoDelete'); del.setAttribute('aria-label', t('todoDelete'));
      del.addEventListener('click', () => { todos = todos.filter((x) => x.id !== item.id); showTodo(); post({ type: 'todoRemove', id: item.id }); });
      li.append(check, txt, del);
      return li;
    }));
    todoSeen = new Set(todos.map((x) => x.id));
  }
  function todoAdding(on) {
    todo.classList.toggle('adding', on);
    if (on) todoInput.focus(); else { todoInput.value = ''; if (document.activeElement === todoInput) todoInput.blur(); }
  }
  $('todoPlus').addEventListener('click', () => todoAdding(!todo.classList.contains('adding')));
  $('todoClear').addEventListener('click', () => { todos = todos.filter((x) => !x.done); showTodo(); post({ type: 'todoClear' }); });
  $('todoForm').addEventListener('submit', (e) => {
    e.preventDefault();
    const text = todoInput.value.trim();
    if (!text) { todoAdding(false); return; }
    post({ type: 'todoAdd', text });
    todoInput.value = '';   // continua no campo para a próxima
  });
  todoInput.addEventListener('keydown', (e) => { e.stopPropagation(); if (e.key === 'Escape') todoAdding(false); });
  todoInput.addEventListener('focus', () => post({ type: 'todoFocus', on: true }));
  // Perder o foco NÃO fecha o campo: ao pegar o teclado, o WebView às vezes tira e devolve o foco num instante, e o
  // campo fechava logo depois de abrir (parecia que o "+" não funcionava). Fecha só no ✕, no Esc ou com a ilha fechando.
  todoInput.addEventListener('blur', () => post({ type: 'todoFocus', on: false }));
  todo.addEventListener('pointerdown', (e) => e.stopPropagation());   // dentro da lista não vira arrastar página nem segurar a ilha

  /* ---------------- Páginas (setas ‹ › e bolinhas) ---------------- */
  let page = 0;
  const pages = () => [...document.querySelectorAll('.page')].filter((p) =>
    (p.dataset.page !== 'water' || water.enabled) && (p.dataset.page !== 'focus' || pomoOn) && (p.dataset.page !== 'agenda' || agendaOn) && (p.dataset.page !== 'weather' || weatherOn) && (p.dataset.page !== 'todo' || todoOn) && (p.dataset.page !== 'devices' || (devicesOn && devices.length + devUnsup.length > 0)));
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
    showTabs(list);
    reportHeight();
    showQuick();
    dotsTo(many ? list.length : 0, page);
    todoArm();
  }

  // Página Tarefas na tela com a ilha aberta: o C# deixa a ilha aceitar foco ao ser clicada (para digitar)
  let todoArmed = false;
  function todoArm() {
    const list = pages();
    const on = mode === 'open' && todoOn && list[Math.min(page, list.length - 1)]?.dataset.page === 'todo';
    if (on === todoArmed) return;
    todoArmed = on;
    post({ type: 'todoArm', on });
  }

  /* ---------------- Gota das páginas ----------------
     As bolinhas ficam paradas (apagadas) e a da página atual é uma gota desenhada por cima (SVG com filtro "goo").
     Trocando de página, a frente da gota corre (mola dura) e a parte de trás vem depois (mola mole): ela estica.
     Duas gotinhas vêm ainda mais atrás; longe, o filtro as mostra soltas; perto, elas se fundem de novo na gota.
     requestAnimationFrame só enquanto mexe; sem animação no modo leve. (Sem onda/anel piscando: pedido do Esdras.) */
  const dg = { svg: $('dotsGoo'), pill: $('dgPill'), d1: $('dgD1'), d2: $('dgD2'), rest: $('dotsGoo').querySelector('.dg-rest') };
  const DG_W = 14, DG_GAP = 14;   // gota parada: 14 px de largura; distância entre as bolinhas
  let dgCount = 0, dgP = null, dgV = null, dgGoal = 0, dgRaf = 0, dgT = 0;
  function dotsTo(count, index) {
    if (!count) { dg.svg.style.display = 'none'; dgCount = 0; return; }
    const dots = $('dots');
    dg.svg.style.display = '';
    const w = (count - 1) * DG_GAP + DG_W + 16;
    dg.svg.setAttribute('width', w); dg.svg.setAttribute('height', 22);
    dg.svg.setAttribute('viewBox', `0 0 ${w} 22`);
    dg.svg.style.left = (dots.offsetLeft + dots.offsetWidth / 2 - w / 2) + 'px';
    dg.svg.style.top = dots.offsetTop + 'px';
    const x0 = 8 + DG_W / 2;   // centro da primeira bolinha
    if (count !== dgCount) {
      dgCount = count;
      dg.rest.replaceChildren(...Array.from({ length: count }, (_, i) => {
        const c = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        c.setAttribute('cx', x0 + i * DG_GAP); c.setAttribute('cy', 11); c.setAttribute('r', 2.5);
        return c;
      }));
      dgP = null;
    }
    dgGoal = x0 + index * DG_GAP;
    const still = document.documentElement.classList.contains('lite') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!dgP || still) { dgP = { h: dgGoal, t: dgGoal, a: dgGoal, b: dgGoal }; dgV = { h: 0, t: 0, a: 0, b: 0 }; dgDraw(); return; }
    if (!dgRaf) { dgT = performance.now(); dgRaf = requestAnimationFrame(dgStep); }
  }
  // frente dura, trás mole, gotinhas mais moles ainda
  const DG_SPRINGS = { h: [520, 34], t: [200, 22], a: [130, 15], b: [90, 12] };
  function dgStep(now) {
    const sec = Math.max(0, Math.min(0.05, (now - dgT) / 1000));
    dgT = now;
    for (let left = sec; left > 1e-6; left -= 1 / 480) {
      const dt = Math.min(1 / 480, left);
      for (const k in DG_SPRINGS) {
        const [K, C] = DG_SPRINGS[k];
        dgV[k] += (-K * (dgP[k] - dgGoal) - C * dgV[k]) * dt;
        dgP[k] += dgV[k] * dt;
      }
    }
    dgDraw();
    const moving = Object.keys(dgP).some((k) => Math.abs(dgP[k] - dgGoal) > 0.2 || Math.abs(dgV[k]) > 2);
    if (moving) dgRaf = requestAnimationFrame(dgStep); else { dgRaf = 0; dgP = { h: dgGoal, t: dgGoal, a: dgGoal, b: dgGoal }; dgDraw(); }
  }
  function dgDraw() {
    const l = Math.min(dgP.h, dgP.t) - DG_W / 2, r = Math.max(dgP.h, dgP.t) + DG_W / 2;
    const stretch = (r - l) - DG_W;
    const hgt = Math.max(3.4, 5 - stretch * 0.04);   // esticada, fica mais fina
    dg.pill.setAttribute('x', l.toFixed(2)); dg.pill.setAttribute('width', (r - l).toFixed(2));
    dg.pill.setAttribute('y', (11 - hgt / 2).toFixed(2)); dg.pill.setAttribute('height', hgt.toFixed(2));
    dg.pill.setAttribute('rx', (hgt / 2).toFixed(2)); dg.pill.setAttribute('ry', (hgt / 2).toFixed(2));
    // gotinhas: só aparecem atrás da gota (paradas, ficam escondidas dentro dela)
    const dir = Math.sign(dgGoal - dgP.t) || 1;
    dg.d1.setAttribute('cx', (dgP.a - dir * 3).toFixed(2)); dg.d1.setAttribute('cy', 11);
    dg.d2.setAttribute('cx', (dgP.b - dir * 5).toFixed(2)); dg.d2.setAttribute('cy', 11);
  }

  /* ---------------- Abas (tema Vidro) ----------------
     No tema Vidro, as bolinhas viram uma barra de abas: ícones em círculos de vidro e a aba ativa numa pílula
     verde-limão. Ao trocar, a pílula ESTICA até a aba nova com mola: a ponta da frente vai primeiro (mais rígida) e a
     de trás vem depois (mais solta), como uma gota; na ponta de trás fica uma gotinha que some. O nome da aba entra
     com fade quando a pílula chega. Os espaços das abas são fixos (nada pula de lugar). rAF só enquanto mexe. */
  const TAB_ICONS = {
    media: '<svg viewBox="0 0 24 24"><path d="M9 17.5V6.2c0-.5.3-.9.8-1l8.4-2c.6-.1 1.2.3 1.2 1V15.5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><circle cx="6.5" cy="17.5" r="2.8" fill="currentColor"/><circle cx="16.9" cy="15.5" r="2.8" fill="currentColor"/></svg>',
    focus: '<svg viewBox="0 0 24 24"><circle cx="12" cy="13" r="8" fill="currentColor"/><path d="M12 9v4.2l2.6 1.6" fill="none" stroke="var(--tab-cut, #000)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><path d="M9.5 2.5h5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
    water: '<svg viewBox="0 0 24 24"><path d="M12 2.6c3.8 4.5 6.6 8 6.6 11.4a6.6 6.6 0 0 1-13.2 0C5.4 10.6 8.2 7.1 12 2.6z" fill="currentColor"/></svg>',
    agenda: '<svg viewBox="0 0 24 24"><path d="M7 2.5a1 1 0 0 1 1 1V5h8V3.5a1 1 0 1 1 2 0V5h.5A2.5 2.5 0 0 1 21 7.5v11a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 18.5v-11A2.5 2.5 0 0 1 5.5 5H6V3.5a1 1 0 0 1 1-1z" fill="currentColor"/><path d="M3 9.5h18" stroke="var(--tab-cut, #000)" stroke-width="1.6"/></svg>',
    weather: '<svg viewBox="0 0 24 24"><path d="M7 19h10.6a4.4 4.4 0 0 0 .6-8.76A6.4 6.4 0 0 0 5.8 11.5 3.8 3.8 0 0 0 7 19z" fill="currentColor"/></svg>',
    devices: '<svg viewBox="0 0 24 24"><path d="M4 15v-3a8 8 0 0 1 16 0v3" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><rect x="3" y="13" width="5" height="8" rx="2" fill="currentColor"/><rect x="16" y="13" width="5" height="8" rx="2" fill="currentColor"/></svg>',
  };
  TAB_ICONS.todo = '<svg viewBox="0 0 24 24" aria-hidden="true"><defs><mask id="tdTa"><rect width="24" height="24" fill="#fff"/><path d="M5.4 12l3.3 3.3L19.8 4.2" fill="none" stroke="#000" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"/></mask></defs><rect x="2.5" y="6.5" width="15" height="15" rx="4.5" fill="currentColor" mask="url(#tdTa)"/></svg>';
  const TAB_LABELS = { media: 'tabMedia', focus: 'tabFocus', water: 'tabWater', agenda: 'tabAgenda', weather: 'tabWeather', todo: 'tabTodo', devices: 'tabDevices' };
  let tabKey = '', tabPos = null, tabGoal = null, tabVel = { l: 0, r: 0 }, tabRaf = 0, tabT = 0, tabLead = 1;
  function showTabs(list) {
    const bar = $('tabbar');
    const key = lang + '|' + list.map((p) => p.dataset.page).join(',');
    if (key !== tabKey) {
      tabKey = key;
      bar.querySelectorAll('.tab').forEach((b) => b.remove());
      list.forEach((p, i) => {
        const b = document.createElement('button');
        b.className = 'tab';
        b.innerHTML = '<span class="tab-ico">' + TAB_ICONS[p.dataset.page] + '</span><span class="tab-label"></span>';
        b.querySelector('.tab-label').textContent = t(TAB_LABELS[p.dataset.page]);
        b.addEventListener('click', () => { const step = i - page; if (!step) return; page = i; showPages(); slideIn(step); });
        bar.appendChild(b);
      });
    }
    [...bar.querySelectorAll('.tab')].forEach((b, i) => b.classList.toggle('on', i === page));
    bar.hidden = list.length < 2;
    requestAnimationFrame(moveTabPill);
  }
  function moveTabPill() {
    const bar = $('tabbar'), on = bar.querySelector('.tab.on');
    if (!on || bar.hidden || !bar.offsetWidth) { tabPos = null; return; }   // fora do tema Vidro: nada a fazer
    // Posição de layout (sem a escala da animação de abrir nem zoom): a aba fica dentro da barra (position: relative)
    const goal = { l: on.offsetLeft, r: on.offsetLeft + on.offsetWidth };
    const still = document.documentElement.classList.contains('lite') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!tabPos || still) { tabPos = { ...goal }; tabGoal = goal; tabVel = { l: 0, r: 0 }; drawTabPill(); return; }
    if (tabGoal && Math.abs(goal.l - tabGoal.l) < 0.5 && Math.abs(goal.r - tabGoal.r) < 0.5) return;
    tabLead = goal.l > tabPos.l ? 1 : -1;   // indo para a direita: a ponta da direita vai na frente
    tabGoal = goal;
    const drop = $('tabDrop');               // gotinha que fica na ponta de trás
    drop.style.left = (tabLead > 0 ? tabPos.l + 12 : tabPos.r - 12) + 'px';
    drop.classList.remove('go'); void drop.offsetWidth; drop.classList.add('go');
    if (!tabRaf) { tabT = performance.now(); tabRaf = requestAnimationFrame(tabStep); }
  }
  function tabStep(now) {
    const dt = Math.max(0, Math.min(0.032, (now - tabT) / 1000));   // nunca negativo (ver waveStep)
    tabT = now;
    let moving = false;
    for (const k of ['l', 'r']) {
      const front = (k === 'r') === (tabLead > 0);
      const K = front ? 560 : 190, D = front ? 38 : 24;   // frente rígida, trás solta: a pílula estica e depois junta
      const a = -K * (tabPos[k] - tabGoal[k]) - D * tabVel[k];
      tabVel[k] += a * dt;
      tabPos[k] += tabVel[k] * dt;
      if (Math.abs(tabVel[k]) > 0.4 || Math.abs(tabPos[k] - tabGoal[k]) > 0.4) moving = true;
    }
    if (!moving) { tabPos = { ...tabGoal }; tabVel = { l: 0, r: 0 }; }
    drawTabPill();
    tabRaf = moving ? requestAnimationFrame(tabStep) : 0;
  }
  function drawTabPill() {
    const p = $('tabPill');
    p.style.transform = 'translateX(' + tabPos.l.toFixed(2) + 'px)';
    p.style.width = Math.max(24, tabPos.r - tabPos.l).toFixed(2) + 'px';
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
    const tl = e.target.closest && e.target.closest('.todo-list, .devs-list');
    if (tl && tl.scrollHeight > tl.clientHeight + 1) return;   // lista comprida (tarefas, aparelhos): a rodinha rola ela
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
    if (e.button !== 0 || mode !== 'open' || e.target.closest('button, .dots, .bar.seekable, .cover')) return;
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
    island.classList.toggle('pomo-alert', on && n.kind === 'pomo');   // fim do foco/pausa: aviso alto, com destaque
    if (n.icon === 'walk') requestAnimationFrame(playWalk);   // a cena começa do início (depois de o aviso aparecer)
    $('notice').hidden = !on;
    if (!on) return;
    $('notice').dataset.kind = n.kind || n.icon;   // água: botão azul; o resto, branco; greet: só a frase; osd: indicador rápido
    island.classList.toggle('osd', n.kind === 'osd');
    island.classList.toggle('act', n.kind === 'act');   // atividade: faixa um pouco mais larga
    const bar = $('osdBar');   // volume: barrinha com o nível
    bar.hidden = !((n.kind === 'osd' || n.kind === 'act') && n.level >= 0);
    if (!bar.hidden) bar.firstElementChild.style.width = Math.round(n.level * 100) + '%';
    $('noticeIcon').innerHTML = n.icon === 'greet' ? '' : NOTICE_ICONS[n.icon] || (String(n.icon).startsWith('dev-') ? '<span class="dev-notice-ico">' + (DEV_ICONS[n.icon.slice(4)] || DEV_ICONS.device) + '</span>' : NOTICE_ICONS.bell);   // ícones fixos daqui; o "bom dia" é só a frase
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
  // Tracinho (opção): recolhida em cima das abas ela fica travada; só abre com o mouse parado em cima por 3 s
  // (pedido do Esdras, 18/11). Clique e passadas rápidas não abrem. O traço vai crescendo até a borda enquanto espera.
  const TUCK_HOLD_MS = 3000;
  let tuckHold = 0;
  const tuckLine = () => mode === 'tucked' && document.documentElement.classList.contains('tuck-line');
  function tuckArm(on) {
    clearTimeout(tuckHold); tuckHold = 0;
    island.classList.toggle('arming', on);
    if (on) tuckHold = setTimeout(() => { island.classList.remove('arming'); if (tuckLine()) post({ type: 'tuckClick' }); }, TUCK_HOLD_MS);
  }
  island.addEventListener('mouseenter', () => { if (tuckLine()) tuckArm(true); });
  island.addEventListener('mouseleave', () => tuckArm(false));

  function setMode(m) {
    if (m !== 'tucked' && tuckHold) tuckArm(false);
    if (m !== 'open' && todo.classList.contains('adding')) todoAdding(false);   // fechou a ilha: larga o campo
    setTimeout(todoArm, 0);
    mode = m;
    island.classList.remove('tucked', 'closed', 'idle', 'alerting', 'open');
    island.classList.add(m === 'notice' ? 'alerting' : m);   // "notice" vira "alerting": a classe .notice já tem outro uso
    if (m === 'open') { showProgress(); showFocusTime(); aimBubble(); }
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
  // O conteúdo pode mudar de altura sozinho (ex.: a fonte terminou de carregar): mede de novo (só dispara quando muda)
  new ResizeObserver(() => reportHeight()).observe($('full'));

  /* ---------------- Forma: mola física + cantos contínuos ----------------
     Cada estado define no CSS o tamanho-alvo (--w, --h, --r, --ear-size). Quando muda (classe da ilha, altura da
     página, formato), a mola leva a forma até lá; o requestAnimationFrame só roda enquanto ela se mexe.
     Rígida e pouco elástica, medida no Alcove: chega em ~250 ms e passa só ~2% do tamanho. Interrompida no meio
     (o mouse saiu), ela muda de direção sem tranco, porque continua da velocidade em que estava. */
  /* ---------------- Bolha do Pomodoro: gota líquida ----------------
     O corpo da bolha é uma gota desenhada na mesma camada (e cor) da ilha. Quando as duas chegam perto, nasce um
     "pescoço" entre elas que estica e se rompe, como duas gotas se juntando (metaball). Uma mola mais mole que a da
     ilha leva a gota (x, y, raio) ao lugar do estado: fechada = ao lado; aberta = o botão do player (ou o chip);
     escondida = para dentro da ilha. O anel e o relógio andam junto; quando a gota chega, o botão aparece no lugar.
     O requestAnimationFrame só roda enquanto ela se mexe. */
  const BLOB_K = 170, BLOB_C = 14;   // mais mole que a ilha: a gota fica para trás e o pescoço estica
  const BLOB_C_REST = 24;             // voltando para o lado: quase sem passar do ponto (amortecimento ~0,9)
  // Escondendo (ilha recolhida em cima das abas, aviso, Pomodoro parado): mola dura e sem balanço, para a gota entrar
  // junto com a ilha. Mole, ela ficava para trás e parecia uma peça separada sumindo depois (Esdras, 18/11).
  const BLOB_K_HIDE = 900, BLOB_C_HIDE = 60;
  const blobNeck = $('blobNeck'), blobBody = $('blobBody'), bubbleEl = $('pomoBubble');
  let blob = null, blobGoal = null, blobVel = { x: 0, y: 0, r: 0 }, blobRaf = 0, blobT = 0;
  let blobLinked = false;   // ligada à ilha pelo pescoço (tensão superficial: liga encostando, solta só bem esticada)
  const shapeTop = (h) => (document.documentElement.classList.contains('at-bottom') ? island.offsetHeight - h : 0);
  const restOf = () => ({ x: bubbleEl.offsetLeft + bubbleEl.offsetWidth / 2, y: bubbleEl.offsetTop + bubbleEl.offsetHeight / 2 });

  function blobTarget() {
    const on = island.classList.contains('pomo-on');
    if (on && (mode === 'closed' || mode === 'idle')) return { ...restOf(), r: 25, show: true };
    if (on && mode === 'open') {
      const q = island.classList.contains('quick-pomo') && centerIn($('pomoQuick'));
      if (q) return { ...q, r: 25, show: true, land: true };   // mesmo tamanho do botão: a troca não pula
      const chip = $('pomoChip'), c = centerIn(chip);
      if (c) return { x: c.x - chip.offsetWidth / 2 + 10, y: c.y, r: 15, show: true, land: true };
    }
    const g = readGoal();   // escondida: entra pela borda da direita e some lá dentro
    // Escondida (recolhida em cima de um app, aviso, Pomodoro parado): a gota encolhe até sumir enquanto entra na ilha.
    // Antes ela ficava com 22 px de raio e, com a ilha recolhida (6 px de altura), ficava pendurada embaixo do risquinho.
    return { x: island.offsetWidth / 2 + g.w / 2 - 34, y: shapeTop(g.h) + Math.min(25, g.h / 2), r: 0, show: false };
  }

  function blobTo() {
    if (!bubbleEl || !cur) return;
    blobGoal = blobTarget();
    const still = document.documentElement.classList.contains('lite') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!blob || still) { blob = { x: blobGoal.x, y: blobGoal.y, r: blobGoal.r }; blobVel = { x: 0, y: 0, r: 0 }; blobPaint(); return; }
    if (!blobRaf) { blobT = performance.now(); blobRaf = requestAnimationFrame(blobStep); }
  }

  function blobStep(now) {
    const sec = Math.max(0, Math.min(0.05, (now - blobT) / 1000));
    blobT = now;
    if (springRaf) blobGoal = blobTarget();   // a ilha ainda está mudando de tamanho: o alvo anda junto
    // Voltando para o lado da ilha fechada (ou sumindo): mola bem amortecida, sem passar do ponto. Mole, ela passava
    // ~20 px do lugar, saía da área desenhável (borda da janela / topo da tela) e aparecia cortada.
    const hiding = !blobGoal.show;
    integrate(blob, blobVel, blobGoal, ['x', 'y', 'r'], hiding ? BLOB_K_HIDE : BLOB_K, hiding ? BLOB_C_HIDE : blobGoal.land ? BLOB_C : BLOB_C_REST, sec);
    let moving = false;
    for (const k of ['x', 'y', 'r']) if (Math.abs(blobVel[k]) > 0.3 || Math.abs(blob[k] - blobGoal[k]) > 0.3) moving = true;
    if (!moving && !springRaf) {
      blobLinked = blobLinked && blobGoal.show;
      blob = { x: blobGoal.x, y: blobGoal.y, r: blobGoal.r }; blobVel = { x: 0, y: 0, r: 0 }; blobRaf = 0;
      blobPaint();
      animDone();   // a gota chegou: se a ilha também já parou, a janela pode encolher
      return;
    }
    blobPaint();
    blobRaf = requestAnimationFrame(blobStep);
  }

  // Raio desenhado: nunca maior que o da bolha (25), senão passa da janela da ilha fechada e é cortado
  const blobR = () => Math.max(0, Math.min(25, blob.r));

  // A gota e o pescoço (coordenadas da forma)
  function drawBlob() {
    if (!blob || !cur) return;
    const { w, h } = cur;
    const ox = island.offsetWidth / 2 - w / 2, oy = shapeTop(h);
    const cx = blob.x - ox, cy = blob.y - oy, br = blobR();
    // ponto mais perto da ilha (retângulo de cantos redondos): distância do centro da gota e direção para fora
    const hx = w / 2, hy = h / 2, rr = Math.max(0, Math.min(cur.r, hx, hy));
    const px = cx - hx, py = cy - hy;
    const qx = Math.abs(px) - (hx - rr), qy = Math.abs(py) - (hy - rr);
    let dist, nx, ny;
    if (qx > 0 && qy > 0) { const l = Math.hypot(qx, qy); dist = l - rr; nx = Math.sign(px) * qx / l; ny = Math.sign(py) * qy / l; }
    else if (qx > qy) { dist = qx - rr; nx = Math.sign(px) || 1; ny = 0; }
    else { dist = qy - rr; nx = 0; ny = Math.sign(py) || 1; }
    const gap = dist - br;                 // espaço entre a gota e a ilha (negativo = já encostou)
    if (dist <= -br + 0.5 || br < 1) {     // inteira dentro da ilha (ou sem tamanho): nada a desenhar
      blobBody.setAttribute('d', ''); blobNeck.setAttribute('d', ''); return;
    }
    const f = (v) => v.toFixed(1);
    // Corpo: estica no sentido em que a gota anda (e achata do outro lado), como um pingo
    const speed = Math.hypot(blobVel.x, blobVel.y);
    const st = Math.min(0.32, speed * 0.0009);
    const rx = br * (1 + st), ry = br / Math.sqrt(1 + st);
    const ang = speed > 1 ? Math.atan2(blobVel.y, blobVel.x) : 0;
    const ex = Math.cos(ang) * rx, ey = Math.sin(ang) * rx, deg = ang * 180 / Math.PI;
    blobBody.setAttribute('d', `M${f(cx - ex)},${f(cy - ey)} A${f(rx)},${f(ry)} ${f(deg)} 1,0 ${f(cx + ex)},${f(cy + ey)} A${f(rx)},${f(ry)} ${f(deg)} 1,0 ${f(cx - ex)},${f(cy - ey)}Z`);
    // O pescoço aguenta mais esticado quando a gota está andando rápido (e se rompe quando ela freia)
    // Liga quando encosta; depois de ligada, o pescoço aguenta esticar (mais ainda com a gota andando rápido)
    // e só se rompe quando ela freia: aí a gota dá uma balançada
    const reach = 5 + Math.min(46, speed * 0.1);
    if (!blobLinked && gap < 3) blobLinked = true;
    else if (blobLinked && gap >= reach) {
      blobLinked = false;
      // Balançada ao romper: só no meio do caminho (ilha aberta). Voltando para o lado da ilha fechada ela inchava
      // ~5 px, passava dos 50 px da janela (cortada) e ficava tremendo no lugar (Esdras, 17/11).
      if (blobGoal && blobGoal.land) blobVel.r += 60;
      if (!blobRaf && blobGoal) { blobT = performance.now(); blobRaf = requestAnimationFrame(blobStep); }
    }
    if (!blobLinked) { blobNeck.setAttribute('d', ''); return; }
    let k = gap <= 0 ? 1 : Math.max(0, 1 - gap / Math.max(reach, 12));
    k = k * k * (3 - 2 * k);
    const tx = -ny, ty = nx;                        // ao longo da parede
    const g = Math.max(gap, 0);
    const Wx = cx - nx * dist, Wy = cy - ny * dist;  // ponto da parede
    const th = (0.28 + 0.58 * k) * Math.PI / 2;      // quanto da gota o pescoço abraça
    const wy = br * (0.22 + 0.95 * k);                // largura do pescoço na parede (encostando: um "menisco" largo)
    const sn = Math.sin(th), cs = Math.cos(th);
    const side = (s) => {
      const Ax = Wx - nx * 2 + tx * wy * s, Ay = Wy - ny * 2 + ty * wy * s;                  // na parede (um pouco para dentro)
      const Px = cx + br * (-nx * cs + tx * sn * s), Py = cy + br * (-ny * cs + ty * sn * s); // na gota
      const Tx = -(nx * sn + tx * cs * s), Ty = -(ny * sn + ty * cs * s);                     // tangente da gota, rumo à parede
      // saindo da parede: encostado, quase deitado nela (menisco); esticado, para fora e para dentro (cintura fina)
      const lean = 0.25 + 1.6 * (1 - k);
      const dl = Math.hypot(lean, 1);
      const h1 = g * 0.45 + wy * 0.55 + 1, h2 = g * 0.45 + br * 0.25 + 1;
      return {
        Ax, Ay, Px, Py,
        c1x: Ax + (nx * lean - tx * s) / dl * h1, c1y: Ay + (ny * lean - ty * s) / dl * h1,
        c2x: Px + Tx * h2, c2y: Py + Ty * h2,
      };
    };
    const a = side(1), b = side(-1);
    blobNeck.setAttribute('d', `M${f(a.Ax)},${f(a.Ay)} C${f(a.c1x)},${f(a.c1y)} ${f(a.c2x)},${f(a.c2y)} ${f(a.Px)},${f(a.Py)}` +
      ` L${f(cx)},${f(cy)} L${f(b.Px)},${f(b.Py)} C${f(b.c2x)},${f(b.c2y)} ${f(b.c1x)},${f(b.c1y)} ${f(b.Ax)},${f(b.Ay)}Z`);
  }

  // Gota + anel e relógio por cima; quando chega no botão, ele aparece no lugar
  function blobPaint() {
    drawBlob();
    const rest = restOf(), s = blobR() / 25;
    bubbleEl.style.transform = `translate(${(blob.x - rest.x).toFixed(1)}px, ${(blob.y - rest.y).toFixed(1)}px) scale(${s.toFixed(3)})`;
    const landed = !!blobGoal.land && Math.hypot(blob.x - blobGoal.x, blob.y - blobGoal.y) < 3;
    if (landed !== island.classList.contains('landed')) island.classList.toggle('landed', landed);
    bubbleEl.style.opacity = blobGoal.show && !landed ? '1' : '0';
  }

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
    // Líquido: enquanto a forma muda de tamanho, as bordas que andam embarrigam como uma gota (para fora crescendo,
    // para dentro encolhendo) e voltam retas quando ela para. Para fora, nunca passa do tamanho final (a janela não corta).
    const bottom = document.documentElement.classList.contains('at-bottom');
    // Mais contido (o Esdras viu calombos ao abrir/fechar do risquinho): no máx. 5/6 px, e nada indo para o risquinho
    // ou saindo dele (de 6 px para 160 px a velocidade é enorme e a forma ficava toda ondulada).
    const tinyEnd = h < 16 || (goal && goal.h < 16);
    let bx = tinyEnd ? 0 : Math.max(-5, Math.min(5, vel.w * 0.005));   // laterais
    let by = tinyEnd ? 0 : Math.max(-6, Math.min(6, vel.h * 0.008));   // borda que cresce na altura (embaixo da tela: a de cima)
    if (goal && bx > 0) bx = Math.min(bx, Math.max(0, (goal.w - w) / 2));
    if (goal && by > 0) by = Math.min(by, Math.max(0, goal.h - h) + 8);
    const bb = bottom ? 0 : by, tb = bottom ? -by : 0;
    const f = (v) => +v.toFixed(2);
    // reta de (x1,y1) a (x2,y2) que embarriga (ox, oy) no meio
    const edge = (x1, y1, x2, y2, ox, oy) => (Math.abs(ox) < 0.05 && Math.abs(oy) < 0.05) ? ` L${f(x2)},${f(y2)}`
      : ` C${f(x1 + (x2 - x1) / 3 + ox * 1.33)},${f(y1 + (y2 - y1) / 3 + oy * 1.33)} ${f(x1 + 2 * (x2 - x1) / 3 + ox * 1.33)},${f(y1 + 2 * (y2 - y1) / 3 + oy * 1.33)} ${f(x2)},${f(y2)}`;
    let d;
    if (floating) {   // pílula solta: quatro cantos contínuos
      const [p, c] = corner(r, Math.min(w, h) / 2);
      d = `M${f(L + p)},0` + edge(L + p, 0, R - p, 0, 0, tb) + ` C${f(R - c)},0 ${f(R)},${f(c)} ${f(R)},${f(p)}` +
          edge(R, p, R, h - p, bx, 0) + ` C${f(R)},${f(h - c)} ${f(R - c)},${f(h)} ${f(R - p)},${f(h)}` +
          edge(R - p, h, L + p, h, 0, bb) + ` C${f(L + c)},${f(h)} ${f(L)},${f(h - c)} ${f(L)},${f(h - p)}` +
          edge(L, h - p, L, p, -bx, 0) + ` C${f(L)},${f(c)} ${f(L + c)},0 ${f(L + p)},0 Z`;
    } else {          // grudada no topo: borda de cima reta, orelhas côncavas ligando à borda, cantos de baixo contínuos
      const [p, c] = corner(r, Math.min(w / 2, h));
      d = `M0,0 H${f(R + e)} Q${f(R)},0 ${f(R)},${f(e)}` + edge(R, e, R, h - p, bx, 0) +
          ` C${f(R)},${f(h - c)} ${f(R - c)},${f(h)} ${f(R - p)},${f(h)}` + edge(R - p, h, L + p, h, 0, bb) +
          ` C${f(L + c)},${f(h)} ${f(L)},${f(h - c)} ${f(L)},${f(h - p)}` + edge(L, h - p, L, e, -bx, 0) + ` Q${f(L)},0 0,0 Z`;
    }
    shapePath.setAttribute('d', d);
    drawBlob();   // o pescoço da gota acompanha a borda da ilha
  }

  // O C# só deixa a janela do tamanho exato quando a forma E a gota do Pomodoro pararam. Antes ia só pela forma:
  // a gota (mola mais mole) ainda estava voltando para o lado da ilha e a janela encolhia por cima dela, cortando
  // e fazendo piscar (vídeo do Esdras, 17/11). A gota começa no setTimeout do springTo: espera um quadro antes de decidir.
  function animDone() {
    if (springRaf || blobRaf) return;
    requestAnimationFrame(() => { if (!springRaf && !blobRaf) post({ type: 'animDone', seq: modeSeq }); });
  }

  function springTo() {
    goal = readGoal();
    setTimeout(blobTo, 0);   // a gota do Pomodoro vai para o lugar do estado novo (depois do layout)
    const still = document.documentElement.classList.contains('lite') || matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (!cur || still) { cur = { ...goal }; drawShape(); post({ type: 'animDone', seq: modeSeq }); return; }
    if (!springRaf) { springT = performance.now(); springRaf = requestAnimationFrame(springStep); }
  }

  // Passos fixos de 1/480 s: o movimento é o mesmo em 60, 144 ou 240 Hz (mais quadros = só mais liso),
  // e um quadro atrasado não deixa a animação em câmera lenta (anda o tempo que passou, até 50 ms)
  const PHYS_STEP = 1 / 480;

  // Quantos quadros a ilha consegue de verdade (o requestAnimationFrame segue o monitor). No fim de cada animação,
  // compara com a taxa do monitor (o C# manda "hz") e avisa o C#, que anota no log do --dev.
  let monitorHz = 60, frameTimes = [], lastFpsSent = 0;
  function frameStat(sec) { if (sec > 0 && frameTimes.length < 240) frameTimes.push(sec); }
  function frameReport() {
    const t = frameTimes; frameTimes = [];
    if (t.length < 8) return;
    t.sort((a, b) => a - b);
    const fps = Math.round(1 / t[t.length >> 1]);
    if (Math.abs(fps - lastFpsSent) < 4) return;
    lastFpsSent = fps;
    post({ type: 'fps', fps, hz: monitorHz });
  }
  function integrate(state, velo, target, keys, K, C, seconds) {
    for (let left = seconds; left > 1e-6; left -= PHYS_STEP) {
      const dt = Math.min(PHYS_STEP, left);
      for (const k of keys) {
        velo[k] += (-K * (state[k] - target[k]) - C * velo[k]) * dt;
        state[k] += velo[k] * dt;
      }
    }
  }

  function springStep(now) {
    const sec = Math.max(0, Math.min(0.05, (now - springT) / 1000));   // aba escondida e volta: não dá um salto
    springT = now;
    frameStat(sec);
    integrate(cur, vel, goal, KEYS, SPRING_K, SPRING_C, sec);
    let moving = false;
    for (const k of KEYS) if (Math.abs(vel[k]) > 0.3 || Math.abs(cur[k] - goal[k]) > 0.3) moving = true;
    if (!moving) {
      cur = { ...goal }; vel = { w: 0, h: 0, r: 0, e: 0 }; springRaf = 0;
      frameReport();
      drawShape();
      animDone();
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
        lang = V.I18N[m.language] ? m.language : 'en';
        document.documentElement.lang = V.langTag(lang);
        document.documentElement.classList.toggle('lite', !!m.lite);
        document.documentElement.classList.toggle('floating', m.style === 'float');   // formato: pílula solta
        document.documentElement.classList.toggle('at-bottom', !!m.bottom);
        document.documentElement.classList.toggle('tuck-line', !!m.tuckLine);   // em cima das abas: tracinho que abre com clique
        document.documentElement.dataset.skin = m.skin || 'default';
        if (V.useSkin) V.useSkin(m.skin);   // carrega só o arquivo desse tema
        tabPos = null; tabKey = ''; setTimeout(showPages, 0);                   // tema (Configurações → Temas)           // embaixo (acima da barra de tarefas): cresce para cima
        pomoOn = m.pomodoro !== false;
        V.applyI18n(lang);
        showMonitors();
        tickClock();
        showMedia();
        showWater();
        showPomodoro();
        showAgenda();          // textos montados pelo JS (Agora, Amanhã, Dia todo...) no idioma novo
        showWeather();
        showMiniWeather();
        showDevices();         // "1 conectado", nomes e títulos montados pelo JS
        showMiniDevices();
        showTodo();
        showBattery();
        showFocusTime();
        showMute($('muteBtn').classList.contains('muted'));
        if (lastPrivacy) showPrivacy(lastPrivacy);
        break;
      case 'pomodoro': onPomodoro(m); break;
      case 'pet': if (m.on) rest(m.ms || 0); else wake(); break;   // mascote: PC parado ou passadinha
      case 'mode':    modeSeq = m.seq || 0; setMode(m.mode); if (m.mode !== 'open') closeVol(); break;
      case 'media': {
        const changed = media && m.media && media.title !== m.media.title;
        media = m.media; showMedia();
        if (changed && mode === 'open') { const t = document.querySelector('.track'); t.classList.remove('swap'); void t.offsetWidth; t.classList.add('swap'); }
        break;
      }
      case 'volume':  if (volOpen) { vpShow(m.v, m.muted); vpIdle(); } else showVolume(m); showMute(m.muted); break;
      case 'volLevel': vpShow(m.v, m.muted); showMute(m.muted); break;
      case 'mute':    showMute(m.muted); break;
      case 'level':   onLevel(m.v); break;
      case 'privacy': showPrivacy(m); break;
      case 'agenda':  agendaOn = !!m.on; agenda = m.events || []; showAgenda(); showPages(); break;
      case 'weather':
        weatherOn = !!m.on; weather = m.weather; showQuick();
        wxUnit = m.unit === 'f' ? 'f' : 'c'; wxShow = m.show || 'both';
        showWeather(); showMiniWeather(); showPages();
        break;
      case 'water':   water = m.water; showWater(); break;
      case 'battery': battery = m; showBattery(); break;
      case 'sound':   soundOn = m.on; showWave(); break;
      case 'notice':  showNotice(m); break;
      case 'todo':    todoOn = m.on !== false; todos = m.items || []; showTodo(); showPages(); break;
      case 'devices': devicesOn = m.on !== false; devices = m.items || []; devUnsup = m.on !== false ? m.unsupported || [] : []; showDevices(); showPages(); break;
      case 'monitors': showMonitors(m); break;
      case 'hz':      monitorHz = m.hz || 60; lastFpsSent = 0; break;   // taxa do monitor onde a ilha está
    }
  });

  document.addEventListener('contextmenu', (e) => e.preventDefault());
  post({ type: 'ready' });
  // Escala em que o WebView desenha de verdade (monitor × "Tamanho do texto" do Windows): o C# ajusta o tamanho da janela.
  // Avisa ao abrir e sempre que mudar (trocou de monitor, mudou a escala ou o tamanho do texto).
  (function watchScale() {
    post({ type: 'dpr', v: window.devicePixelRatio });
    matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`).addEventListener('change', watchScale, { once: true });
  })();

})();
