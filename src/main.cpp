// VetTV Universal Entry — One codebase for 4 stores
// Compile with: VETTV_MICROSOFTSTORE | VETTV_STEAM | VETTV_EPIC | VETTV_WEBUI2

#include "core/dx12_renderer.h"
#include "core/game_loop.h"
#include "webui/webui_bridge.h"
#include <iostream>

#if defined(VETTV_MICROSOFTSTORE)
    extern "C" __declspec(dllexport) void VetTV_InitDX12() {
        VetTV::DX12Renderer::Get().Initialize();
    }
#elif defined(VETTV_WEBUI2)
    #include "webui.h"
    int main() {
        webui::window win;
        win.bind("buyLemons", [](webui::window::event* e){
            int count = e->get_int(0);
            VetTV::WebUIBridge::BindBuyLemons(count);
        });
        win.bind("setWeather", [](webui::window::event* e){
            float i = e->get_float(0); float w = e->get_float(1);
            VetTV::WebUIBridge::BindSetWeather(i,w);
        });
        win.show("src/webui/index.html");
        webui::wait();
        return 0;
    }
#else
    int main() {
        std::cout << "VetTV Standalone - Steam/Epic Build\n";
        VetTV::GameLoop loop;
        loop.Run();
        return 0;
    }
#endif
