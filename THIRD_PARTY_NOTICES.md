# Third-party components

The live D3D11 probe and Python capture backend under vendor/efmi-mcp/ come from the project owner's EFMI MCP project. UPSTREAM.json records the source commit and file checksums. APMI adds process and package selection, its MCP tool entry points, and launcher integration. [EFMI MCP source](https://github.com/loyal37/EFMI-MCP)

The launch workflow was informed by [EFMI Vulkan Launcher](https://github.com/loyal37/efmi-vulkan-launcher) and [XXMI Launcher](https://github.com/SpectrumQT/XXMI-Launcher). APMI Launcher source is licensed under GPL-3.0-or-later.

- [APMI Package](https://github.com/loyal37/APMI-Package) includes APMI Core and configuration based on [EFMI-Package](https://github.com/SpectrumQT/EFMI-Package), licensed under GPL-3.0. The bundled package includes the Core sources, license and provenance.
- [XXMI-Libs](https://github.com/SpectrumQT/XXMI-Libs) supplies the user's local D3D11 runtime 1.3.3. Source and release information are available through [XXMI-Libs-Package](https://github.com/SpectrumQT/XXMI-Libs-Package).
- `d3dcompiler_47.dll` is the Microsoft shader compiler supplied with the user's existing EFMI package.
- .NET and Windows Desktop Runtime are Microsoft products. Their license notices accompany a self-contained .NET publication.
- The Microsoft WebView2 SDK and Runtime provide the embedded browser. Their respective Microsoft license terms apply.
- `wallpapers/official-launcher-background.png` and `wallpapers/official-launcher-background.mp4` are original artwork and video from the Azur Promilia official launcher's local media cache. Copyright remains with Manjuu and the respective rights holders. These media assets are not covered by the source code's GPL license; this repository does not grant additional rights to them.
- `src/assets/launcher-icon.png` was supplied by the project owner. `launcher-icon.ico` contains the same image in Windows icon sizes. These artwork files retain their original rights and are not covered by the source code's GPL license.
