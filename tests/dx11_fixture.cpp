#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <array>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>

namespace fs = std::filesystem;
using Microsoft::WRL::ComPtr;

// Opt-in compute skinning with native current/previous vertex buffers.
struct SkinningFixture
{
    ComPtr<ID3D11ComputeShader> shader;
    ComPtr<ID3D11Buffer> params;
    ComPtr<ID3D11Buffer> bones;
    std::array<ComPtr<ID3D11ShaderResourceView>, 3> input;
    std::array<ComPtr<ID3D11Buffer>, 2> output;
    std::array<ComPtr<ID3D11UnorderedAccessView>, 2> uav;

    bool initialize(ID3D11Device* device, const fs::path& path)
    {
        std::ifstream file(path, std::ios::binary);
        const std::string code(std::istreambuf_iterator<char>(file), {});
        if (code.empty() || FAILED(device->CreateComputeShader(code.data(), code.size(), nullptr, &shader)))
            return false;

        const float position[30] = {
            -0.5f,-0.5f,0, 0,0,1, 1,0,0,1,
             0.0f, 0.5f,0, 0,0,1, 1,0,0,1,
             0.5f,-0.5f,0, 0,0,1, 1,0,0,1,
        };
        const UINT blend[3] = {0, 0, 0};
        const UINT constants[4] = {3, 0, 0, 0};
        const float palette[32] = {
            1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1,
            1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1,
        };
        for (unsigned i = 0; i < input.size(); ++i) {
            D3D11_BUFFER_DESC desc{};
            desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            desc.ByteWidth = i == 0 ? sizeof(position) : i == 1 ? sizeof(blend) : sizeof(palette);
            desc.MiscFlags = i == 2 ? D3D11_RESOURCE_MISC_BUFFER_STRUCTURED : D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS;
            desc.StructureByteStride = i == 2 ? 64 : 0;
            D3D11_SUBRESOURCE_DATA data{i == 0 ? static_cast<const void*>(position) :
                                      i == 1 ? static_cast<const void*>(blend) : palette, 0, 0};
            ComPtr<ID3D11Buffer> buffer;
            if (FAILED(device->CreateBuffer(&desc, &data, &buffer))) return false;
            D3D11_SHADER_RESOURCE_VIEW_DESC srv{};
            srv.Format = i == 2 ? DXGI_FORMAT_UNKNOWN : DXGI_FORMAT_R32_TYPELESS;
            srv.ViewDimension = D3D11_SRV_DIMENSION_BUFFEREX;
            srv.BufferEx.NumElements = desc.ByteWidth / (i == 2 ? 64 : 4);
            srv.BufferEx.Flags = i == 2 ? 0 : D3D11_BUFFEREX_SRV_FLAG_RAW;
            if (FAILED(device->CreateShaderResourceView(buffer.Get(), &srv, &input[i]))) return false;
            if (i == 2) bones = buffer;
        }

        D3D11_BUFFER_DESC desc{};
        desc.ByteWidth = sizeof(constants);
        desc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        D3D11_SUBRESOURCE_DATA initial{constants, 0, 0};
        if (FAILED(device->CreateBuffer(&desc, &initial, &params))) return false;
        desc.ByteWidth = sizeof(position);
        desc.BindFlags = D3D11_BIND_VERTEX_BUFFER | D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
        desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_ALLOW_RAW_VIEWS;
        desc.StructureByteStride = 40;
        D3D11_UNORDERED_ACCESS_VIEW_DESC view{};
        view.Format = DXGI_FORMAT_R32_TYPELESS;
        view.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
        view.Buffer.NumElements = desc.ByteWidth / 4;
        view.Buffer.Flags = D3D11_BUFFER_UAV_FLAG_RAW;
        for (unsigned i = 0; i < output.size(); ++i) {
            if (FAILED(device->CreateBuffer(&desc, nullptr, &output[i])) ||
                FAILED(device->CreateUnorderedAccessView(output[i].Get(), &view, &uav[i]))) return false;
        }
        return true;
    }

    bool update(ID3D11DeviceContext* context, unsigned frame)
    {
        const UINT stride = 40, zero = 0;
        ID3D11Buffer* empty = nullptr;
        const unsigned current = frame % 2, previous = 1 - current;
        context->IASetVertexBuffers(0, 1, &empty, &stride, &zero);
        context->IASetVertexBuffers(3, 1, &empty, &stride, &zero);
        const float translation = current ? 0.25f : -0.25f;
        const float palette[32] = {
            1,0,0,0, 0,1,0,0, 0,0,1,0, translation,0,0,1,
            1,0,0,0, 0,1,0,0, 0,0,1,0, translation,0.5f,0,1,
        };
        context->UpdateSubresource(bones.Get(), 0, nullptr, palette, 0, 0);
        ID3D11ShaderResourceView* views[] = {input[0].Get(), input[1].Get(), input[2].Get()};
        ID3D11UnorderedAccessView* destination = uav[current].Get();
        ID3D11Buffer* constants = params.Get();
        context->CSSetShader(shader.Get(), nullptr, 0);
        context->CSSetShaderResources(0, 3, views);
        context->CSSetConstantBuffers(0, 1, &constants);
        context->CSSetUnorderedAccessViews(0, 1, &destination, nullptr);
        context->Dispatch(1, 1, 1);
        ComPtr<ID3D11ShaderResourceView> position, blend;
        ComPtr<ID3D11Buffer> cb;
        context->CSGetShaderResources(0, 1, &position);
        context->CSGetShaderResources(1, 1, &blend);
        context->CSGetConstantBuffers(0, 1, &cb);
        if (position.Get() != views[0] || blend.Get() != views[1] || cb.Get() != constants) return false;
        ID3D11ShaderResourceView* empty_views[3]{};
        context->CSSetShaderResources(0, 3, empty_views);
        destination = nullptr;
        context->CSSetUnorderedAccessViews(0, 1, &destination, nullptr);
        ID3D11Buffer* vertex = output[current].Get();
        ID3D11Buffer* history = output[previous].Get();
        context->IASetVertexBuffers(0, 1, &vertex, &stride, &zero);
        context->IASetVertexBuffers(3, 1, &history, &stride, &zero);
        return true;
    }
};

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
    const fs::path native_shader = exe.parent_path() / "native-skin.cso";
    const bool skinned = fs::is_regular_file(native_shader);
    SkinningFixture skinning;
    const char* vertex_source = skinned ?
        "struct V { float4 position:SV_Position; float2 uv:TEXCOORD; float4 color:COLOR; };"
        "V main(float3 pos:POSITION,float3 prev:TEXCOORD5,float2 uv:TEXCOORD,float4 color:COLOR) {"
        "V v; v.position=float4(pos + (pos-prev)*0.01,1); v.uv=uv; v.color=color; return v; }" :
        "cbuffer Config : register(b0) { float4 offset; };"
        "struct V { float4 position:SV_Position; float2 uv:TEXCOORD; float4 color:COLOR; };"
        "V main(float3 pos:POSITION,float2 uv:TEXCOORD,float4 color:COLOR) {"
        "V v; v.position=float4(pos,1)+offset; v.uv=uv; v.color=color; return v; }";
    const char* pixel_source =
        "Texture2D image : register(t0); SamplerState sampling : register(s0);"
        "float4 main(float4 pos:SV_Position,float2 uv:TEXCOORD,float4 color:COLOR):SV_Target { return image.Sample(sampling,uv)*color; }";
    ID3DBlob* vs_code = nullptr;
    ID3DBlob* ps_code = nullptr;
    if (FAILED(D3DCompile(vertex_source, strlen(vertex_source), nullptr, nullptr, nullptr, "main", "vs_5_0",
                         0, 0, &vs_code, nullptr))) return 15;
    if (FAILED(D3DCompile(pixel_source, strlen(pixel_source), nullptr, nullptr, nullptr, "main", "ps_5_0",
                         0, 0, &ps_code, nullptr))) return 16;
    ID3D11VertexShader* vs = nullptr;
    ID3D11PixelShader* ps = nullptr;
    if (FAILED(device->CreateVertexShader(vs_code->GetBufferPointer(), vs_code->GetBufferSize(), nullptr, &vs))) return 17;
    if (FAILED(device->CreatePixelShader(ps_code->GetBufferPointer(), ps_code->GetBufferSize(), nullptr, &ps))) return 18;
    D3D11_INPUT_ELEMENT_DESC elements[] = {
        { "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 12, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 1, 0, D3D11_INPUT_PER_VERTEX_DATA, 0 },
        { "TEXCOORD", 5, DXGI_FORMAT_R32G32B32_FLOAT, 3, 0, D3D11_INPUT_PER_VERTEX_DATA, 0 }
    };
    ID3D11InputLayout* layout = nullptr;
    if (FAILED(device->CreateInputLayout(elements, skinned ? 4 : 3, vs_code->GetBufferPointer(), vs_code->GetBufferSize(), &layout))) return 19;
    vs_code->Release();
    ps_code->Release();
    const float vertices[] = { -0.5f,-0.5f,0, 0,0, 0,0.5f,0, 0.5f,1, 0.5f,-0.5f,0, 1,0 };
    const unsigned short indices[] = { 0,1,2 };
    const float offset[] = { 0,0,0,0 };
    const float shared_color[] = { 1,0.5f,0.25f,1 };
    const float vertex_colors[] = { 1,0.5f,0.25f,1, 1,0.5f,0.25f,1, 1,0.5f,0.25f,1 };
    ID3D11Buffer* buffers[4]{};
    const UINT sizes[] = { sizeof(vertices), sizeof(indices), sizeof(offset), skinned ? sizeof(vertex_colors) : sizeof(shared_color) };
    const UINT binds[] = { D3D11_BIND_VERTEX_BUFFER, D3D11_BIND_INDEX_BUFFER, D3D11_BIND_CONSTANT_BUFFER, D3D11_BIND_VERTEX_BUFFER };
    const void* data[] = { vertices, indices, offset, skinned ? vertex_colors : shared_color };
    for (int i = 0; i < 4; ++i) {
        D3D11_BUFFER_DESC bd{};
        bd.ByteWidth = sizes[i];
        bd.Usage = D3D11_USAGE_DEFAULT;
        bd.BindFlags = binds[i];
        D3D11_SUBRESOURCE_DATA initial{ data[i], 0, 0 };
        if (FAILED(device->CreateBuffer(&bd, &initial, &buffers[i]))) return 20;
    }
    D3D11_TEXTURE2D_DESC td{};
    td.Width = 1; td.Height = 1; td.MipLevels = 1; td.ArraySize = 1;
    td.Format = DXGI_FORMAT_R8G8B8A8_UNORM; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    const unsigned pixel = 0xff3399ff;
    D3D11_SUBRESOURCE_DATA texture_data{ &pixel, 4, 4 };
    ID3D11Texture2D* texture = nullptr;
    ID3D11ShaderResourceView* texture_view = nullptr;
    if (FAILED(device->CreateTexture2D(&td, &texture_data, &texture))) return 21;
    if (FAILED(device->CreateShaderResourceView(texture, nullptr, &texture_view))) return 22;
    D3D11_SAMPLER_DESC sd{};
    sd.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.MaxLOD = D3D11_FLOAT32_MAX;
    ID3D11SamplerState* sampler = nullptr;
    if (FAILED(device->CreateSamplerState(&sd, &sampler))) return 23;
    const UINT stride = sizeof(float) * 5, zero = 0;
    context->IASetInputLayout(layout);
    context->IASetVertexBuffers(0, 1, &buffers[0], &stride, &zero);
    const UINT color_stride = skinned ? 16 : 0;
    context->IASetVertexBuffers(1, 1, &buffers[3], &color_stride, &zero);
    context->IASetIndexBuffer(buffers[1], DXGI_FORMAT_R16_UINT, 0);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(vs, nullptr, 0);
    context->PSSetShader(ps, nullptr, 0);
    context->VSSetConstantBuffers(0, 1, &buffers[2]);
    context->PSSetShaderResources(0, 1, &texture_view);
    context->PSSetSamplers(0, 1, &sampler);
    const D3D11_VIEWPORT viewport{ 0, 0, 320, 180, 0, 1 };
    context->RSSetViewports(1, &viewport);
    // XXMI loads includes on the first Present. Create the skinning buffers
    // afterwards, as when the game streams a character into the scene.
    if (skinned && (FAILED(swap->Present(0, 0)) || !skinning.initialize(device, native_shader))) return 24;
    std::ofstream(exe.parent_path() / "probe-loaded.txt") << (GetModuleHandleW(L"apmi_probe.dll") != nullptr);
    const unsigned frame_limit = GetPrivateProfileIntW(L"Fixture", L"frames", 90, ini.c_str());
    const bool instanced = skinned && GetPrivateProfileIntW(L"Fixture", L"instanced", 0, ini.c_str()) != 0;
    unsigned frames = 0;
    for (; frames < frame_limit; ++frames) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        const float color[] = { 0.06f, 0.24f, 0.32f, 1.0f };
        context->ClearRenderTargetView(view, color);
        if (skinned && !skinning.update(context, frames)) return 25;
        if (instanced)
            context->DrawIndexedInstanced(3, 2, 0, 0, 1);
        else
            context->DrawIndexed(3, 0, 0);
        if (skinned) {
            ComPtr<ID3D11Buffer> index, attribute;
            UINT bound_stride = 0, bind_offset = 0;
            DXGI_FORMAT format{};
            context->IAGetIndexBuffer(&index, &format, &bind_offset);
            context->IAGetVertexBuffers(1, 1, &attribute, &bound_stride, &bind_offset);
            if (index.Get() != buffers[1] || format != DXGI_FORMAT_R16_UINT ||
                attribute.Get() != buffers[3] || bound_stride != color_stride) return 26;
        }
        if (FAILED(swap->Present(0, 0))) break;
        Sleep(12);
    }
    wchar_t mapped[32768]{};
    GetModuleFileNameW(library, mapped, 32768);
    std::ofstream(exe.parent_path() / "frames.txt") << frames;
    std::ofstream(exe.parent_path() / "d3d11-path.txt", std::ios::binary)
        .write(reinterpret_cast<char*>(mapped), wcslen(mapped) * sizeof(wchar_t));
    Sleep(900);
    sampler->Release();
    texture_view->Release();
    texture->Release();
    for (auto buffer : buffers) buffer->Release();
    layout->Release();
    vs->Release();
    ps->Release();
    view->Release();
    backbuffer->Release();
    swap->Release();
    context->Release();
    device->Release();
    DestroyWindow(window);
    return 0;
}
