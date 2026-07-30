# Blsqui SDK for Unity Engine

The official Unity SDK for **Blsqui** — enabling seamless Flow blockchain local loopback authentication and in-game transaction dialogs.

## Features
- **Local Loopback Auth:** Secure, browser-based Flow wallet authentication via embedded HTTP loopback listener.
- **Cloudflare Worker Backbone:** High-throughput, edge-accelerated transaction status polling via Cloudflare Workers.
- **Pre-built UI Prefabs:** Ready-to-use Canvas dialogs for payment confirmation and live transaction loading state.
- **Async & C# Events:** Modern C# event delegates for clean game loop and UI integration.

## 📦 Quick Start
1. Clone or download this repository.
2. Copy `Packages/com.blsqui.sdk/` into your Unity project's `Packages/` directory (or install via UPM).
3. Drag the `BlsquiManager` prefab into your initial scene.
4. Call `BlsquiManager.Instance.RequestTransaction(...)` from your game script!

## Requirements
- Unity **2021.3 LTS** or higher
- Target Support: Standalone PC/Mac, WebGL (via popup bridge)

## License
MIT License - see [LICENSE](LICENSE) for details.