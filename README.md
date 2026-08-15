# HololensGo

[![UWP Build](https://github.com/turbolego/HololensGo/actions/workflows/dotnet.yml/badge.svg)](https://github.com/turbolego/HololensGo/actions/workflows/dotnet.yml)
[![UWP Package](https://github.com/turbolego/HololensGo/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/turbolego/HololensGo/actions/workflows/dotnet-desktop.yml)
![Platform x86](https://img.shields.io/badge/platform-x86-blue)
![SDK 10.0.19041](https://img.shields.io/badge/Windows%20SDK-10.0.19041-blue)
![HoloLens 1](https://img.shields.io/badge/HoloLens-1st%20gen-blueviolet)

**Pokémon Go inspired game for Microsoft HoloLens 1** — catch Steamboat Willie Mickey Mouse by throwing potatoes at him!

Built on the UWP platform with a custom Direct3D 11 / SharpDX rendering pipeline — no Unity, no external engines.

---

## What You Will See

Put on the HoloLens and launch the app:

1. **Room scan** — The HoloLens scans your environment using spatial mapping
2. **Mickey appears** — After ~3 seconds, Steamboat Willie Mickey Mouse spawns ~1.5m in front of you, floating at floor level
3. **Throw potatoes!** — Perform an air-tap (pinch gesture), click your clicker remote, or use a connected controller to launch a potato in the direction you're looking
4. **Hit Mickey** — Mickey drifts and bobs within the encounter area. A successful hit triggers a shake-and-pulse reaction, advances your combo, and starts a short respawn interval
5. **Recover from misses** — Potatoes follow ballistic physics, bounce off the encounter floor with damping, and expire after five seconds. A miss resets the active combo

Both Mickey and the potatoes are procedurally generated 3D models — no external OBJ/FBX assets required.

---

## How It Works

| Step | Detail |
|---|---|
| **Spatial tracking** | HoloLens creates a stationary reference frame at the user's startup position |
| **Mickey placement** | After 3 seconds, Mickey appears 1.5m in front of the user on the floor plane |
| **Gesture input** | `SpatialInteractionManager` detects air-taps (hand gestures) and clicker remote presses |
| **Projectile launch** | Each throw begins 0.1m ahead of the gaze origin at 3.5m/s, with a ten-potato active-projectile budget |
| **Game session** | A renderer-independent `GameSession` owns score, combo, projectile budget, target respawn, and deterministic fixed-step simulation |
| **Collision detection** | Swept segment-versus-sphere check (0.15m target radius) prevents fast potatoes from skipping through Mickey |
| **Projectile physics** | Semi-implicit gravity, floor bounce/restitution, lateral friction, damping, lifetime/range cleanup, and trajectory-driven spin |
| **Target behavior** | Mickey drifts and bobs during each encounter; a hit triggers shake-and-pulse feedback before a gaze-relative respawn |
| **Rendering** | Direct3D 11 holographic pipeline draws Mickey (procedural geometry) and each active, spinning potato |

### Mickey's Model

Built entirely from code — no 3D assets. ~15 body parts constructed from primitives:

- **Head** — sphere + 2 ear spheres
- **Face** — skin-coloured disc, white eyes, black pupils, nose, red mouth
- **Body** — red rounded box with 2 white buttons
- **Limbs** — black cylinders for arms, red cylinders for legs, skin-coloured hand spheres
- **Shoes** — brown boxes
- **Tail** — black cylinder
- **Hat** — blue captain's hat with white brim
- **Steamboat wheel** — grey ring around his waist

---

## Project Structure

```
HololensGo/
├── .github/workflows/
│   ├── dotnet.yml              # CI compile check (Debug + Release)
│   ├── dotnet-desktop.yml      # Signed .appxupload artifact on push
│   └── store-submission.yml    # Full Store pipeline: build, package, submit
├── Assets/                     # PNG logos/splash at required sizes
├── Common/
│   └── DeviceResources.cs      # Direct3D device management
├── Content/
│   ├── MickeyRenderer.cs       # Procedural Steamboat Willie rendering
│   ├── PotatoRenderer.cs       # Brown ellipsoid potato rendering
│   ├── SpatialInputHandler.cs  # Gesture/clicker input
│   ├── ShaderStructures.cs     # Vertex/constant buffer structs
│   └── Shaders/                # HLSL shaders
├── Models/
│   ├── GameSession.cs          # Deterministic gameplay rules, scoring, collisions, and respawns
│   └── Potato.cs               # Potato projectile with physics and spin state
├── tests/                      # Portable unit tests for projectile and session rules
├── ITERATIONS.md               # 100-cycle gameplay iteration register
├── privacy/
│   └── index.html              # Privacy policy (served via GitHub Pages)
├── properties/
│   └── AssemblyInfo.cs
├── Main.cs                     # Game loop — update, render, collision
├── HololensGo.csproj           # UWP project — SharpDX 3.0.2, x86
├── HololensGo_TemporaryKey.pfx # Dev signing cert
├── Package.appxmanifest        # Identity, capabilities, logos
└── deploy.ps1                  # One-shot deploy to HoloLens over USB
```

---

## Prerequisites

| Requirement | Version / Notes |
|---|---|
| Windows | 10 or 11 (64-bit) |
| Visual Studio 2022 | Community (free) or higher — **UWP workload** required |
| Windows 10 SDK | **10.0.19041.0** (included in UWP workload) |
| HoloLens 1 | Developer Mode enabled |
| Cable | Micro-USB to USB-A |

---

## Build

Use MSBuild from Visual Studio 2022. The cross-platform `dotnet build` CLI
cannot resolve Windows XAML targets.

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
```

### Debug build — compile check only

```powershell
& $msbuild HololensGo.csproj `
    /p:Configuration=Debug `
    /p:Platform=x86 `
    /p:AppxPackageSigningEnabled=false `
    /p:GenerateAppxPackageOnBuild=false `
    /v:minimal
```

### Release build — signed .appxupload (Store-ready)

```powershell
& $msbuild HololensGo.csproj `
    /t:Publish `
    /p:Configuration=Release `
    /p:Platform=x86 `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxPackageDir=AppPackages\ `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateKeyFile=HololensGo_TemporaryKey.pfx `
    /p:PackageCertificatePassword=ci `
    /v:minimal
```

Output lands in `AppPackages\HololensGo_1.0.0.0_x86_Test\`.

---

## Deploy to HoloLens

### 1. Enable Developer Mode on HoloLens

1. **Start menu → Settings → Update & Security → For developers**
2. Toggle **Use developer features → On**
3. Toggle **Enable Device Portal → On**

### 2. Connect via USB

Connect the HoloLens with a **Micro-USB to USB-A** cable. Windows installs a
**Remote NDIS (RNDIS)** driver — the device becomes reachable at `127.0.0.1`.

### 3. Install with WinAppDeployCmd

```powershell
# Locate the tool
$wadc = (Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin"
    -Recurse -Filter "WinAppDeployCmd.exe" |
    Sort-Object FullName | Select-Object -Last 1).FullName

# Install the .appx + dependencies
$pkg = "AppPackages\HololensGo_1.0.0.0_x86_Test"
& $wadc install `
    -f  "$pkg\HololensGo_1.0.0.0_x86.appx" `
    -ip 127.0.0.1 `
    -d  "$pkg\Dependencies\x86\Microsoft.NET.Native.Framework.1.3.appx" `
    -d  "$pkg\Dependencies\x86\Microsoft.NET.Native.Runtime.1.4.appx" `
    -d  "$pkg\Dependencies\x86\Microsoft.VCLibs.x86.14.00.appx"
```

First-time pairing: the HoloLens shows a 6-digit PIN — add `-pin 123456`.

### Quick re-deploy

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy.ps1
```

---

## Gameplay Iteration Record

The current gameplay pass is documented in [`ITERATIONS.md`](ITERATIONS.md). It records one hundred focused design, implementation, and deterministic simulation cycles, with the portable rules covered by unit tests and a 100-update stability scenario.

## CI / CD

Three GitHub Actions workflows run on `windows-2022` runners.

| Workflow | Trigger | Produces |
|---|---|---|
| `dotnet.yml` | Push / PR to `main` | Compile check (Debug + Release) |
| `dotnet-desktop.yml` | Push to `main` | Signed `.appxupload` artifact |
| `store-submission.yml` | Tag `v*.*.*` | `.appxupload` + WACK + optional Store publish |

### `dotnet.yml` — compile check

Builds Debug and Release with signing disabled. Catches build regressions.

### `dotnet-desktop.yml` — signed artifact

Generates a fresh self-signed cert per run, builds a full signed Release
`.appxupload` via the `Publish` target with `UapAppxPackageBuildMode=StoreUpload`,
produces a downloadable artifact, then removes the cert.

---

## GitHub Pages

The privacy policy is served at:
**https://turbolego.github.io/HololensGo/privacy/**

The `.nojekyll` file in the repo root ensures GitHub Pages serves the static
HTML directly without Jekyll processing.

---

## Microsoft Store

The `.appxupload` from the CI artifact can be uploaded directly to
[Partner Center](https://partner.microsoft.com/dashboard).

Package identity: `Turbolego.HololensGo`
Publisher: `CN=BB1A7F2A-A87C-44C8-8C14-84C6486E7E75`

---

## Privacy Policy

This app does not collect or transmit personal information. The webcam and
microphone capabilities are used exclusively for HoloLens platform operation
(spatial mapping, head tracking) — no data leaves the device.

Full policy: https://turbolego.github.io/HololensGo/privacy/
