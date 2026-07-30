# HololensGo — Steamboat Willie Pokémon Go Implementation Plan

> **For Hermes:** Fork the SatelliteViewer codebase as the starting point, then strip out satellite logic and replace with Mickey Mouse + potato-throwing gameplay. Each task is a delegate_task subagent target.

**Goal:** A HoloLens 1 app where Steamboat Willie Mickey Mouse appears in the scanned room and the user throws a potato at him via pinch click (or clicker remote). Direct3D 11 / SharpDX, UWP, pure C# — no Unity.

**Architecture:** Fork `HololensSatelliteViewer` structure (same Direct3D pipeline, Common helpers, shaders, CI/CD workflows). Replace SatelliteRenderer with a `CharacterRenderer` that loads a wavefront OBJ model, place Mickey in a random room position after spatial mapping. Replace SpatialInputHandler with a press-and-release gesture detector that computes a physics throw vector from head gaze direction on release. Add a `potato` projectile that animates in a ballistic arc and detects collision with Mickey's bounding sphere.

**Tech Stack:** UWP C#, SharpDX 3.0.2, Direct3D 11, HLSL shaders, Windows 10 SDK 10.0.19041, HoloLens 1 (x86, Windows.Holographic device family)

---

### Task 1: Clone SatelliteViewer as base and scrub it down to the skeleton

**Objective:** Create the working project structure by copying the referenceapp, stripping satellite/TLecies, and rebranding to HololensGo.

**Files:**
- Everything under `HololensGo/`

**Step 1: Copy all source files from HololensSatelliteViewer**

Copy everything except `.git/`, `StoreListing/`, `satellite_position_bug.md`, `Windows-universal-samples-main.zip`, `*.csproj.bak`, `*.csproj.old`.

```bash
rsync -av --exclude='.git' --exclude='StoreListing' --exclude='*.zip' --exclude='*.bak' --exclude='*.old' \
  HololensSatelliteViewer/ HololensGo/
```

**Step 2: Rename csproj and sln**

```bash
cd HololensGo
mv HololensSatelliteViewer.csproj HololensGo.csproj
mv HololensSatelliteViewer.sln HololensGo.sln
mv HololensSatelliteViewer_TemporaryKey.pfx HololensGo_TemporaryKey.pfx
```

**Step 3: Update csproj**

In `HololensGo.csproj`:
- Replace all `HololensSatelliteViewer` → `HololensGo` (RootNamespace, AssemblyName)
- Generate a new GUID for `ProjectGuid` (use `uuidgen` or Visual Studio)
- Remove `<Compile Include>` entries for satellite code (Satellite.cs, TleRecord.cs, TleService.cs, Sgp4Service.cs, OrbitService.cs, GeolocationService.cs, SatelliteRenderer.cs, HolographicPositioning.cs)
- Remove old Asset entries that have satellite-themed imagery
- Update PackageReference: keep SharpDX, SharpDX.Direct2D1,D3D11, Microsoft.NETCore.UniversalWindowsPlatform 5.2.4
- Remove the ServiceModel and helper .cs references

**Step 4: Update Package.appxmanifest**

- Identity Name: `Turbolego.HololensGo`
- DisplayName: `HololensGo`
- Publisher: `CN=BB1A7F2A-A87C-44C8-8C14-84C6486E7E75` (same publisher)
- EntryPoint: `HololensGo.AppView`
- Description: `Steamboat Willie Pokémon Go for HoloLens`
- Capabilities: `webcam`, `microphone`, `internetClient` (location isn't needed here)

**Step 5: Update Program.cs, AppView.cs, AppViewSource.cs**

Update namespace from `HololensSatelliteViewer` → `HololensGo` in these three files.

**Step 6: Update BasicHologramMain.cs and rename to Main.cs**

- Rename `BasicHologramMain.cs` → `Main.cs`
- Rename class `HolographicTemplateAppMain` → `HololensGoMain`
- Change namespace: `HololensSatelliteViewer` → `HololensGo`
- Replace `satelliteRenderer` with `characterRenderer`
- Replace `SatelliteRenderer` with `CharacterRenderer` instantiation
- Keep SpatialInputHandler reference

**Step 7: Delete satellite-specific files**

```bash
rm -rf Models/Satellite.cs Models/TleRecord.cs
rm -rf Services/GeolocationService.cs Services/TleService.cs Services/Sgp4Service.cs Services/OrbitService.cs
rm -rf Content/SatelliteRenderer.cs
rm -rf Helpers/HolographicPositioning.cs
```

**Step 8: Update `Common/` to use HololensGo namespace**

All `Common/*.cs` files: update `namespace HololensSatelliteViewer.Common` → `namespace HololensGo.Common`.

**Step 9: Make placeholder assets**

Generate simple PNG logo assets at correct dimensions (the skill `uwp-store-pipeline` has the spec: Square150x150 (300x300), Wide310x150 (620x300), StoreLogo (50x50), SplashScreen (1240x600, transparent)).

**Step 10: Verification — MSBuild compile check**

On a Windows machine with VS 2022 + 10.0.19041 SDK:

```powershell
msbuild HololensGo.csproj /p:Configuration=Debug /p:Platform=x86 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false
```

Expected: Build succeeds with zero errors (may warn about missing content shaders).

---

### Task 2: Write the MickeyMouse renderer

**Files:**
- Create: `Content/MickeyRenderer.cs`
- Modify: `Content/ShaderStructures.cs` → add `VertexPositionNormalColor`

**Step 1: Create VertexPositionNormal struct**

Add to `Content/ShaderStructures.cs`:

```csharp
internal struct VertexPositionNormal
{
    public Vector3 pos;
    public Vector3 normal;
};
```

**Step 2: Create MickeyRenderer.cs**

```csharp
namespace HololensGo.Content
{
    public class MickeyRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;

        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;
        
        private ModelConstantBuffer modelConstantBuffer;
        private int indexCount = 0;
        private bool loaded = false;
        
        // Position / scale
        public Vector3 Position { get; set; } = Vector3.Zero;
        public float Scale { get; set; } = 0.15f;
        public float Yaw { get; set; } = 0f;
        
        // Animation values
        public bool IsHit { get; set; } = false;
        public float AnimTimer { get; set; } = 0f;
        
        // Procedural body parts — cylinder for head, box for body, etc.
        // Because OBJ loading on .NET Core UWP is painful, we'll procedurally
        // generate a stylized Willie silhouette from primitives.
        
        public MickeyRenderer(DeviceResources dr) { ... }
        public async void CreateDeviceDependentResourcesAsync() {
          // Load compiled .cso shaders
          // Build vertex/index buffers
          ...
        }
        
        // BuildBuffersForMickey: construct an into-ahead shape, a boat shape, a wheel shape
        private MeshData BuildBody() {
            // Head: sphere (uv sphere) centered at (0, 0.7, 0)
            // Ears: two small spheres at (+-0.24, 1.0, 0)
            // Body: rounded box at (0, 0.35, 0) scaled (0.45, 0.6, 0.3)
            // Arms: cylinders from shoulders to hands
            // Legs: cylinders from hips to feet
            // tail: cylinder from back
            // Steamboat wheel: torus around body
            return merge(meshes);
        }
        
        public void Render() {
            if (!loadingComplete) return;
            // Set input layout, shaders, constant buffer
            // Compute model matrix from Position, Scale, Yaw
            Matrix4x4 m = Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateTranslation(Position);
            modelConstantBuffer.model = Matrix4x4.Transpose(m);
            context.UpdateSubresource(ref modelConstantBuffer, modelConstantBuffer);
            context.DrawIndexedInstanced(indexCount, 2, 0, 0, 0);
        }
        
        public void ReleaseDeviceDependentResources() { ... }
    }
}
```

IMPORTANT: Since importing OBJ requires an FBX/OBJ reader, which UWP/SharpDX doesn't have built in, we procedurally generate Mickey's body parts from geometric primitives — spheres, cylinders, and toruses. This is MORE flexible (no external asset loading) and gives us native scale control. The generated model looks like a stylised silhouette of Steamboat Willie: two circular ears, round head, rectangular body, thin limbs, and the steamboat wheel as a torus shape.

**Step 3: Add MickeyRenderer to Main.cs**

In `HololensGoMain`:

```csharp
private MickeyRenderer mickeyRenderer;

// In SetHolographicSpace:
mickeyRenderer = new MickeyRenderer(deviceResources);
// Position Mickey at a random point in the room (will be updated in Task 6)

// In OnPointerPressed:
if (mickeyRenderer != null)
    mickeyRenderer.IsHit = true;
```

---

### Task 3: Adaptive spatial input handler (pinch click + clicker)

**Files:**
- Modify: `Common/SpatialInputHandler.cs`

**Step 1: Add air-tap detection**

The HoloLens 1 uses the "air-tap" gesture (index ring to thumb = pinch, release = tap). We need to detect:
1. SourcePressed → record start position and time
2. SourceUpdated → track movement
3. SourceReleased → compute thrown-direction from head-gaze and release-velocity

```csharp
public class SpatialInputHandler
{
    private SpatialInteractionManager _manager;
    
    public event Action<Vector3, Vector3> OnThrow; // origin, velocity
    
    private SpatialPointerPose _headPoseAtPress;
    private Vector3 _handPositionAtPress;
    private DateTime _pressTime;
    
    // In SourcePressed:
    _headPoseAtPress = SpatialPointerPose.TryGetAtTimestamp(...);
    _handPositionAtPress = args.State.Properties.TryGetLocation(...);
    _pressTime = DateTime.UtcNow;
    
    // In SourceReleased:
    float duration = (float)(DateTime.UtcNow - _pressTime).TotalSeconds;
    // Use head gaze direction as throw direction
    Vector3 gazeDir = _headPoseAtPress.Head.ForwardDirection;
    float speed = 3.0f; // m/s
    OnThrow?.Invoke(_headPoseAtPress.Head.Position, gazeDir * speed);
}
```

The releasevelocity doesn't exist for clicker so we use a fixed forward speed of 3 m/s from the head position at press time.

**Alternative for clicker remote: For SourcePressed from the little 2 arrow clicker**

The clicker walks as a SpatialInteractionSourceKind.Other. For Pressed events from it, we use a straight-ahead gaze direction from head tracking (since the clicker has no positional tracking).

**Step 2: Fallback for keyboard/mouse on Win32 (debugging)**

If `pointerPressed` is set (mouse click), Call the same throw logic from `OnPointerPressed` in `Main.cs`.

---

### Task 5: Create Potato Projectile

**Files:**
- Create: `Models/Potato.cs`
- Modify: `Content/PotatoRenderer.cs` (or integrate into a unified renderer)

**Step 1: Potato.cs**

```csharp
public class PotatoProjectile
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Lifetime = 0f;
    public bool HasCollided = false;
    
    public void Tick(float dt)
    {
        if (HasCollided) return;
        // Simple Euler integration with gravity
        Velocity.Y -= 9.81f * dt;
        Position += Velocity * dt;
        Lifetime += dt;
    }
}
```

**Step 2: PotatoRenderer.cs**

Renders a small ellipsoid (sphere-squished) with a brown color. DrawCall like the cube but more vertices — a uv sphere with ~30 vertices.

```csharp
public class PotatoRenderer : Disposer
{
    // Same shader setup as cube, but with potato-shaped vertex buffer
    // Vertex buffer built as a sphere with Y scaled to 0.7 (potato-ish)
    public void Draw(PotatoProjectile projectile)
    {
        Matrix4x4 m = ... translate to proj.Position, scale by 0.03;
        // Draw indexed instanced
    }
}
```

**Step 3: Integration**

In `Main.cs` update loop:

```csharp
List<PotatoProjectile> potatoes = new List<PotatoProjectile>();

void OnThrow(Vector3 origin, Vector3 velocity)
{
    potatoes.Add(new PotatoProjectile { Position = origin + velocity * 0.01f, Velocity = velocity, Gravity = 9.81f });
}
```

In `Update`:

```csharp
foreach (var p in potatoes) {
    p.Tick(timer.ElapsedSeconds);
    // Check if potato intersects with mickeyPosition bounding sphere
    if (!p.HasCollided && Vector3.Distance(p.Position, mickeyRenderer.Position) < 0.15f)
    {
        p.HasCollided = true;
        mickeyRenderer.IsHit = true;
        // Play audio sound-effect for hit (leave as empty)
    }
}
```

Render: Loop through potatoes and draw each one.

Clean-up: Remove drone that have expired (>5 seconds) or beyond some room distance (>10 meters).

---

### Task 6: Room placement + spawn Mickey

**Files:**
- Modify: `Main.cs`

**Step 1: SpatialMappingSurfaceObserver**

HoloLens 1 has a `SpatialSurfaceObserver` that provides spatial mapping meshes. In `HolographicGoMain.OnHolographicDisplayIsAvailableChanged` after creating the stationary reference frame:

```csharp
var surfaceObserver = new SpatialSurfaceObserver();
surfaceObserver.SetBoundingVolume(SpatialBoundingBox.FromPoints(
    new[] {new Vector3(-3f, -1.5f, -3f), new Vector3(3f, 3.0f, 3f)}
));

surfaceObserver.ObservedSurfacesChanged += (sender, args) => {
    foreach (var surface in observer.GetObservedSurfaces())
    {
        // Compute a random point on the floor
        if (surface.Type == SpatialSurfaceType.Floor)
        {
            // Get mesh bounding box
            // Place Mickey at center of bounding box with +0.5f Y
            mickeyRenderer.Position = center + new Vector3(0, 0.5f, 0);
            observer.Dispose();
        }
    }
};
```

Simplest fallback: just place Mickey at a face-forward position from the user's startup position (~2m away).

**Step 2: On spatial scanning reach, spawn Mickey**

After the HoloLens finishes initial room scan (3 see), we detect spatial anchoring via `SpatialAnchor.ExportAsync` round:

```csharp
// At startup:
mickeyRenderer.Position = worldCenter + new Vector3(0, 0, 1.5f);
```

And add it as a staticValue object.

---

### Task 7: Build scripts + CI workflows

**Files:**

- Create: `.github/workflows/dotnet.yml`
- Create: `.github/workflows/dotnet-desktop.yml`
- Create: `.github/workflows/store-submission.yml`
- Create: `scripts/generate_assets.ps1` / `scripts/make_assets.py`
- Create: `deploy.ps1`

Copy the workflows from HololensSatelliteViewer and update project + app name references to `HololensGo`. The formats are identical except for project identifier.

**KeyIndic changes from SatelliteViewer workflows:**

- `dotnet.yml`: Replace `HololensSatelliteViewer.csproj` → `HololensGo.csproj`, `HololensSatelliteViewer.sln` → `HololensGo.sln`
- `dotnet-desktop.yml`: Replace certificate names (`HololensSatelliteViewer_TemporaryKey` → `HololensGo_TemporaryKey`), artifact names
- `store-submission.yml`: Replace `Microsoft.HololensSatelliteViewer` → `Microsoft.HololensGo` in product ID references

**Verification:**

After pushing to GitHub, `dotnet.yml` action should pass (with `if: worked` flags as before for WACK).

---

### Task 8: Privacy policy HTML

**Files:**
- Create: `privacy/index.html`

Copy from SatelliteViewer repo and update app name references. Same capabilities mapping (webcam for spatial mapping, microphone for voice, internet for nothing).

---

### Task 9: Connect GitHub repo + push everything

**Steps:**

```bash
cd /path/to/HololensGo
git remote add origin https://github.com/turbolego/HololensGo.git
git add .
git commit -m "Initial commit: Hololens Go — Steamboat Willie object-throwing AR game"
git push origin master
```

**Set up GitHub Pages:**

Settings → Pages → Source → Deploy from a branch → branch = master / root (for `/privacy/`).

---

### Task 10: Generate asset icons (logos, splash)

Use Python/PIL to generate required PNG assets:

| File | Name | Size |
|------|------|------|
| Square150x150Logo.scale-200.png | 300x300 | Actually can be any size |
| Wide310x150Logo.scale-200.png | 620x300 | 2x scale ratio |
| From logo.png | 50x50 | Minimal |
| SplashScreen scale-200.png | 1240x600 | Semi-transparent |

Use a simple black/white silhouette of Mickey's head as icon.

---

## Tests / Validation

- [ ] `msbuild` succeeds locally with no errors in Debug x86
- [ ] `deploy.ps1` installs to HoloLens running Dev mode
- [ ] Th-like potato projectile spawns on click (visible as sphere moving through the room)
- [ ] Mickey appears after 3: second of startup at user's pivot spatial position
- [ ] All CI workflows in GitHub Actions pass — dotnet.yml, dotnet-desktop.yml
- [ ] Store package build fails WACK (ewp) but produces valid .appxupload
- [ ] Open source assets from Sketchfab (private object, for reference) and the procedural engine generate backup geometry as plan B

## Risks / Tradeoffs

- **OBJ loading on .NET Core 1.0**: Building a proper vertex tar OBJ FBX loader adds much complexity. Procedural body-part assembly is more robust and keeps the art stylized.
- **Surface detection on HL1**: Spatial mapping data can take 3-6 seconds to stabilize. If surface == floor is not found, fallback to stationary frame center (+2m forward).
- **Physics trajectory**: The HoloLens-1 spatial camera runs at 30 Hz — the potato may skip past Mickey in one frame if moving fast. Interpolated ray-march is needed.
- **No sound** for directional sound effects (not implemented).
- **No networking** — pure single-player POC.

---

## Open Questions

1. Should potato be a golden ogg-like structure or a simple contour?
2. Should space be followed by a retrieval animation (like thrown back)?
3. [ ] Should we support two hand input (one for potato, one for throw)?
4. [ ] Should the game track score (above Mickey: +1, miss=0)?

---

**Ready to execute. Dispatch per task using `delegate_task` with the full context above.**