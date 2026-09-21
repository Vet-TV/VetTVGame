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

function updateUI(moneyVal) {
  if (moneyVal !== undefined) money = moneyVal;
  document.getElementById('money').innerText = money;
  document.getElementById('lemons').innerText = lemons;
}

function onFrameComplete(data) {
  if (data && data.money !== undefined) updateUI(data.money);
}
window.onFrameComplete = onFrameComplete;
window.buyLemons = buyLemons;
window.setWeatherNative = setWeatherNative;
