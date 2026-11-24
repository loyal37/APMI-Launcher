#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <filesystem>
#include <fstream>
#include <string>

namespace fs = std::filesystem;

static LRESULT CALLBACK window_proc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp)
{
    return DefWindowProcW(hwnd, message, wp, lp);
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    wchar_t self[32768]{};
    GetModuleFileNameW(nullptr, self, 32768);
    const fs::path exe(self);
    const fs::path ini = exe.wstring() + L".ini";
    if (exe.filename() == L"official.exe") {
        wchar_t target[32768]{};
        GetPrivateProfileStringW(L"Fixture", L"target", L"", target, 32768, ini.c_str());
        if (!*target) return 0;
        std::wstring command = L"\"" + std::wstring(target) + L"\"";
        STARTUPINFOW startup{ sizeof(startup) };
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(target, command.data(), nullptr, nullptr, FALSE, 0, nullptr,
                            fs::path(target).parent_path().c_str(), &startup, &process)) return 10;
        std::ofstream(exe.parent_path() / "target.pid") << process.dwProcessId;
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 0;
    }
    const int delay = GetPrivateProfileIntW(L"Fixture", L"delay", 1600, ini.c_str());
    Sleep(delay);
    const int exit_code = GetPrivateProfileIntW(L"Fixture", L"exit", 0, ini.c_str());
    if (exit_code) return exit_code;
    if (GetPrivateProfileIntW(L"Fixture", L"idle", 0, ini.c_str())) { Sleep(5000); return 0; }

    HMODULE library = LoadLibraryW(L"d3d11.dll");
    if (!library) return 11;
    auto create_device = reinterpret_cast<decltype(&D3D11CreateDeviceAndSwapChain)>(
        GetProcAddress(library, "D3D11CreateDeviceAndSwapChain"));
    if (!create_device) return 12;
    WNDCLASSW wc{};
    wc.lpfnWndProc = window_proc;
    wc.hInstance = instance;
    wc.lpszClassName = L"APMIDx11Test";
    RegisterClassW(&wc);
    HWND window = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, wc.lpszClassName,
        L"APMI DX11 test", WS_POPUP, -1000, -1000, 320, 180, nullptr, nullptr, instance, nullptr);
    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferDesc.Width = 320;
    desc.BufferDesc.Height = 180;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 1;
    desc.OutputWindow = window;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    IDXGISwapChain* swap = nullptr;
    D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
    HRESULT hr = create_device(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, &level, 1,
        D3D11_SDK_VERSION, &desc, &swap, &device, nullptr, &context);
    if (FAILED(hr)) return 13;
    ID3D11Texture2D* backbuffer = nullptr;
    ID3D11RenderTargetView* view = nullptr;
    hr = swap->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&backbuffer));
    if (SUCCEEDED(hr)) hr = device->CreateRenderTargetView(backbuffer, nullptr, &view);
    if (FAILED(hr)) return 14;
    context->OMSetRenderTargets(1, &view, nullptr);
    unsigned frames = 0;
    for (; frames < 90; ++frames) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        const float color[] = { 0.06f, 0.24f, 0.32f, 1.0f };
        context->ClearRenderTargetView(view, color);
        if (FAILED(swap->Present(0, 0))) break;
        Sleep(12);
    }
    wchar_t mapped[32768]{};
    GetModuleFileNameW(library, mapped, 32768);
    std::ofstream(exe.parent_path() / "frames.txt") << frames;
    std::ofstream(exe.parent_path() / "d3d11-path.txt", std::ios::binary)
        .write(reinterpret_cast<char*>(mapped), wcslen(mapped) * sizeof(wchar_t));
    Sleep(900);
    view->Release();
    backbuffer->Release();
    swap->Release();
    context->Release();
    device->Release();
    DestroyWindow(window);
    return 0;
}
