// TilePlan 的瀏覽器端互動：只處理 .NET 不擅長的高頻事件（平移縮放、拖曳），計算一律回到 .NET。

export function storageGet(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}

export function storageSet(key, value) {
  localStorage.setItem(key, value);
}

export function storageRemove(key) {
  try { localStorage.removeItem(key); } catch { /* 無痕模式可能不允許 */ }
}

export function downloadText(fileName, text, mime) {
  const blob = new Blob([text], { type: mime || "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function printPage() {
  window.print();
}

/** 等瀏覽器真的把畫面畫出來（兩個 animation frame）後回傳時間戳，用於量測重繪時間。 */
export function afterPaint() {
  return new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(performance.now()))));
}

export function now() {
  return performance.now();
}

const canvases = new WeakMap();

/**
 * 啟用畫布互動。svg 的內容放在 <g data-world> 中（已用 scale(1,-1) 轉成 y 軸向上），
 * 回報給 .NET 的座標都是世界座標（cm）。
 * - 滾輪：以游標為中心縮放
 * - 在空白處拖曳：平移
 * - 拖曳 [data-handle] 元素：回報 OnHandleDrag / OnHandleDrop
 * - 點擊（沒有拖曳）：回報 OnCanvasClick
 */
export function initCanvas(svg, dotnet) {
  const state = { svg, dotnet, drag: null, frame: 0 };
  canvases.set(svg, state);

  const world = () => svg.querySelector("[data-world]");
  const toWorld = (evt) => {
    const g = world();
    const m = g.getScreenCTM().inverse();
    const p = new DOMPoint(evt.clientX, evt.clientY).matrixTransform(m);
    return { x: p.x, y: p.y };
  };

  svg.addEventListener("wheel", (evt) => {
    evt.preventDefault();
    const vb = svg.viewBox.baseVal;
    const rect = svg.getBoundingClientRect();
    const fx = (evt.clientX - rect.left) / rect.width;
    const fy = (evt.clientY - rect.top) / rect.height;
    const scale = Math.exp(evt.deltaY * 0.0015);
    const w = vb.width * scale, h = vb.height * scale;
    svg.setAttribute("viewBox", `${vb.x + (vb.width - w) * fx} ${vb.y + (vb.height - h) * fy} ${w} ${h}`);
  }, { passive: false });

  svg.addEventListener("pointerdown", (evt) => {
    if (evt.button !== 0) return;
    const handle = evt.target.closest("[data-handle]");
    const vb = svg.viewBox.baseVal;
    state.drag = {
      handle: handle ? handle.getAttribute("data-handle") : null,
      startX: evt.clientX, startY: evt.clientY,
      vb: { x: vb.x, y: vb.y, w: vb.width, h: vb.height },
      moved: false,
    };
    svg.setPointerCapture(evt.pointerId);
  });

  svg.addEventListener("pointermove", (evt) => {
    const d = state.drag;
    if (!d) return;
    const dx = evt.clientX - d.startX, dy = evt.clientY - d.startY;
    if (!d.moved && Math.hypot(dx, dy) < 4) return;
    d.moved = true;
    if (d.handle) {
      const p = toWorld(evt);
      cancelAnimationFrame(state.frame);
      state.frame = requestAnimationFrame(() => dotnet.invokeMethodAsync("OnHandleDrag", d.handle, p.x, p.y));
    } else {
      const rect = svg.getBoundingClientRect();
      const k = Math.max(d.vb.w / rect.width, d.vb.h / rect.height);
      svg.setAttribute("viewBox", `${d.vb.x - dx * k} ${d.vb.y - dy * k} ${d.vb.w} ${d.vb.h}`);
    }
  });

  svg.addEventListener("pointerup", (evt) => {
    const d = state.drag;
    state.drag = null;
    if (!d) return;
    const p = toWorld(evt);
    if (d.handle && d.moved) {
      cancelAnimationFrame(state.frame);
      dotnet.invokeMethodAsync("OnHandleDrop", d.handle, p.x, p.y);
    } else if (!d.moved) {
      dotnet.invokeMethodAsync("OnCanvasClick", p.x, p.y, d.handle);
    }
  });
}

/** 讓世界座標範圍 [minX, minY]–[maxX, maxY] 填滿畫布（y 軸向上，所以 viewBox 的 y 取負值）。 */
export function fitView(svg, minX, minY, maxX, maxY, margin) {
  const m = margin ?? 20;
  svg.setAttribute("viewBox", `${minX - m} ${-maxY - m} ${maxX - minX + 2 * m} ${maxY - minY + 2 * m}`);
}

export function disposeCanvas(svg) {
  canvases.delete(svg);
}
