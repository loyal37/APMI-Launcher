# Third-party components

The launch workflow was informed by [EFMI Vulkan Launcher](https://github.com/loyal37/efmi-vulkan-launcher) and [XXMI Launcher](https://github.com/SpectrumQT/XXMI-Launcher). APMI Launcher source is licensed under GPL-3.0-or-later.

- [EFMI-Package](https://github.com/SpectrumQT/EFMI-Package) supplies the local EFMI 1.5.1 package. It is GPL-3.0 licensed. The build copies its README and source assets into `dist/APMI`; runtime files are supplied locally and are not tracked in this repository.
- [XXMI-Libs](https://github.com/SpectrumQT/XXMI-Libs) supplies the user's local D3D11 runtime 1.3.3. Source and release information are available through [XXMI-Libs-Package](https://github.com/SpectrumQT/XXMI-Libs-Package).
- `d3dcompiler_47.dll` is the Microsoft shader compiler supplied with the user's existing EFMI package.
- .NET and Windows Desktop Runtime are Microsoft products. Their license notices accompany a self-contained .NET publication.
- The Microsoft WebView2 SDK and Runtime provide the embedded browser. Their respective Microsoft license terms apply.
- `wallpapers/official-launcher-background.png` and `wallpapers/official-launcher-background.mp4` are original artwork and video from the Azur Promilia official launcher's local media cache. Copyright remains with Manjuu and the respective rights holders. These media assets are not covered by the source code's GPL license; this repository does not grant additional rights to them.
- `src/assets/launcher-icon.png` was supplied by the project owner. `launcher-icon.ico` contains the same image in Windows icon sizes. These artwork files retain their original rights and are not covered by the source code's GPL license.
