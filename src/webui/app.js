// VetTV WebUI 2.0 Bridge - JS <-> C++ / C# via WebUI & WebView2
let money = 100;
let lemons = 0;

function buyLemons(count) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage({ action: 'buyLemons', count: count });
  } else if (window.vetNative) {
    window.vetNative.buyLemons(count);
  }
  lemons += count;
  money -= count * 2;
  updateUI();
  console.log(`[WebUI] buyLemons(${count}) -> DX12 SpawnLemonPile`);
}

function setWeatherNative(intensity, wind) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage({ action: 'setWeather', intensity, wind });
  } else if (window.vetNative) {
    window.vetNative.setWeather(intensity, wind);
  }
  document.getElementById('weather').innerText = intensity > 0.5 ? 'Storm' : 'Sunny';
}

function toggleRaytracing() {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage({ action: 'checkRaytracing' });
  }
}

function updateUI(moneyVal, lemonsVal) {
  if (moneyVal !== undefined) money = moneyVal;
  if (lemonsVal !== undefined) lemons = lemonsVal;
  document.getElementById('money').innerText = money;
  document.getElementById('lemons').innerText = lemons;
}

function onFrameComplete(data) {
  if (!data) return;
  if (data.money !== undefined) updateUI(data.money);
  if (data.lemons !== undefined) updateUI(undefined, data.lemons);
  if (data.weather) {
    document.getElementById('weather').innerText = data.weather;
  }
}

function applyHostMessage(data) {
  if (!data || typeof data !== 'object') return;

  if (data.action === 'frameState' || data.action === 'hostReady') {
    updateUI(data.money, data.lemons);
    if (data.weather) {
      document.getElementById('weather').innerText = data.weather;
    }
    console.log('[WebUI] host message', data.action, data);
    // Compat: older callers / sinks expect onFrameComplete({ money, ... }).
    onFrameComplete(data);
    return;
  }

  if (data.action === 'actionResult') {
    console.log('[WebUI] actionResult', data.forAction, data.ok, data.detail, data);
    if (data.money !== undefined || data.lemons !== undefined) {
      updateUI(data.money, data.lemons);
    }
    if (data.weather) {
      document.getElementById('weather').innerText = data.weather;
    }
  }
}

function onHostWebMessage(event) {
  applyHostMessage(event && event.data);
}

if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', onHostWebMessage);
}

window.onFrameComplete = onFrameComplete;
window.buyLemons = buyLemons;
window.setWeatherNative = setWeatherNative;
window.applyHostMessage = applyHostMessage;