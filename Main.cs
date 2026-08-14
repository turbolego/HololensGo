//
// Comment out this preprocessor definition to disable all of the
// sample content.
//
// To remove the content after disabling it:
//     * Remove the unused code from this file.
//     * Delete the Content folder provided with this template.
//
#define DRAW_SAMPLE_CONTENT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Gaming.Input;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Holographic;
using Windows.Perception.Spatial;
using Windows.UI.Input.Spatial;

using HololensGo.Common;
using HololensGo.Content;
using HololensGo.Models;

namespace HololensGo
{
    /// <summary>
    /// HololensGo — Steamboat Willie catching game.
    /// Mickey appears in the room after spatial scan; throw potatoes via air-tap.
    /// </summary>
    internal class HololensGoMain : IDisposable
    {

#if DRAW_SAMPLE_CONTENT
        // Renders Steamboat Willie Mickey Mouse at a fixed world position
        private MickeyRenderer mickeyRenderer;

        // Spawns a procedural potato mesh to render active projectiles
        private PotatoRenderer potatoRenderer;

        // Renderer-independent rules, scoring, projectile simulation, and respawns.
        private readonly GameSession gameSession = new GameSession
        {
            TargetDriftRadiusMeters = 0.18f,
            TargetBobHeightMeters = 0.06f,
            TargetDriftRadiansPerSecond = 1.05f
        };
        private SpatialInputHandler spatialInputHandler;
        private SpatialPointerPose latestHeadPose;
#endif

        // Cached reference to device resources.
        private DeviceResources deviceResources;

        // Render loop timer.
        private StepTimer timer = new StepTimer();

        // Represents the holographic space around the user.
        HolographicSpace holographicSpace;

        // Stationary reference frame (origin at startup position).
        SpatialStationaryFrameOfReference stationaryReferenceFrame;
        private SpatialLocator spatialLocator;

        // For debugging: world-space coords of the user's start location
        private Vector3 worldOrigin = Vector3.Zero;
        private bool spatialTrackingActive = false;

        // Keep track of gamepads.
        private class GamepadWithButtonState
        {
            public Windows.Gaming.Input.Gamepad gamepad;
            public bool buttonAWasPressedLastFrame;
            public GamepadWithButtonState(
                Windows.Gaming.Input.Gamepad gamepad,
                bool buttonAWasPressedLastFrame)
            {
                this.gamepad = gamepad;
                this.buttonAWasPressedLastFrame = buttonAWasPressedLastFrame;
            }
        };
        List<GamepadWithButtonState> gamepads = new List<GamepadWithButtonState>();

        // Keep track of the basic input.
        bool pointerPressed = false;

        // Cache platform features
        bool canGetHolographicDisplayForCamera = false;
        bool canGetDefaultHolographicDisplay = false;
        bool canCommitDirect3D11DepthBuffer = false;

        // ── Mickey position (room-space) ──
        private Vector3 mickeyWorldPos = Vector3.Zero;
        private float mickeyFloorHeight = 0f;
        private bool mickeyPlaced = false;
        private float timeSinceStart = 0f;
        private const float SECONDS_BEFORE_SPAWN = 3.0f;

        public HololensGoMain(DeviceResources deviceResources)
        {
            this.deviceResources = deviceResources;

            // Register to be notified if the Direct3D device is lost.
            this.deviceResources.DeviceLost += this.OnDeviceLost;
            this.deviceResources.DeviceRestored += this.OnDeviceRestored;

            // If connected, a game controller can also be used for input.
            Gamepad.GamepadAdded += this.OnGamepadAdded;
            Gamepad.GamepadRemoved += this.OnGamepadRemoved;

            foreach (var gamepad in Gamepad.Gamepads)
            {
                OnGamepadAdded(null, gamepad);
            }

            canGetHolographicDisplayForCamera = Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Holographic.HolographicCamera", "Display");
            canGetDefaultHolographicDisplay = Windows.Foundation.Metadata.ApiInformation.IsMethodPresent(
                "Windows.Graphics.Holographic.HolographicDisplay", "GetDefault");
            canCommitDirect3D11DepthBuffer = Windows.Foundation.Metadata.ApiInformation.IsMethodPresent(
                "Windows.Graphics.Holographic.HolographicCameraRenderingParameters", "CommitDirect3D11DepthBuffer");
        }

        // ────────────── INITIALIZATION ──────────────

        public void SetHolographicSpace(HolographicSpace holographicSpace)
        {
            this.holographicSpace = holographicSpace;

#if DRAW_SAMPLE_CONTENT
            spatialInputHandler = new SpatialInputHandler();
            mickeyRenderer = new MickeyRenderer(deviceResources);
            // Do not show the target at the stationary-frame origin before the spawn delay.
            mickeyRenderer.IsVisible = false;
            potatoRenderer = new PotatoRenderer(deviceResources);
#endif

            if (canGetDefaultHolographicDisplay)
            {
                HolographicSpace.IsAvailableChanged += this.OnHolographicDisplayIsAvailableChanged;
            }

            OnHolographicDisplayIsAvailableChanged(null, null);

            holographicSpace.CameraAdded += this.OnCameraAdded;
            holographicSpace.CameraRemoved += this.OnCameraRemoved;
        }

        public void Dispose()
        {
            Gamepad.GamepadAdded -= this.OnGamepadAdded;
            Gamepad.GamepadRemoved -= this.OnGamepadRemoved;

            if (canGetDefaultHolographicDisplay)
            {
                HolographicSpace.IsAvailableChanged -= this.OnHolographicDisplayIsAvailableChanged;
            }

            if (holographicSpace != null)
            {
                holographicSpace.CameraAdded -= this.OnCameraAdded;
                holographicSpace.CameraRemoved -= this.OnCameraRemoved;
                holographicSpace = null;
            }

            if (spatialLocator != null)
            {
                spatialLocator.LocatabilityChanged -= this.OnLocatabilityChanged;
                spatialLocator = null;
            }

#if DRAW_SAMPLE_CONTENT
            if (spatialInputHandler != null)
            {
                spatialInputHandler.Dispose();
                spatialInputHandler = null;
            }

            if (mickeyRenderer != null)
            {
                mickeyRenderer.Dispose();
                mickeyRenderer = null;
            }
            if (potatoRenderer != null)
            {
                potatoRenderer.Dispose();
                potatoRenderer = null;
            }
#endif
        }

        // ────────────── UPDATE LOOP ──────────────

        public HolographicFrame Update()
        {
            // The HolographicFrame has information that the app needs in order
            // to update and render the current frame. The app begins each new
            // frame by calling CreateNextFrame.
            HolographicFrame holographicFrame = holographicSpace.CreateNextFrame();

            // Get a prediction of where holographic cameras will be when this frame
            // is presented.
            HolographicFramePrediction prediction = holographicFrame.CurrentPrediction;

            // Back buffers can change from frame to frame. Validate each buffer, and recreate
            // resource views and depth buffers as needed.
            deviceResources.EnsureCameraResources(holographicFrame, prediction);

            SpatialPointerPose headPose = null;

#if DRAW_SAMPLE_CONTENT
            if (stationaryReferenceFrame != null)
            {
                if (spatialInputHandler != null && spatialInputHandler.CheckForInput() != null)
                {
                    pointerPressed = true;
                }

                // Check gamepad (A = throw). Pointer and gesture input share the same
                // queued request so every input source follows identical game rules.
                for (int i = 0; i < gamepads.Count; ++i)
                {
                    bool aDown = (gamepads[i].gamepad.GetCurrentReading().Buttons & GamepadButtons.A) == GamepadButtons.A;
                    if (aDown && !gamepads[i].buttonAWasPressedLastFrame)
                    {
                        pointerPressed = true;
                    }
                    gamepads[i].buttonAWasPressedLastFrame = aDown;
                }

                // Obtain one predicted pose per update. DoThrow receives this pose instead
                // of creating a second holographic frame mid-update.
                headPose = SpatialPointerPose.TryGetAtTimestamp(
                    stationaryReferenceFrame.CoordinateSystem, prediction.Timestamp);
                latestHeadPose = headPose;

                if (pointerPressed)
                {
                    pointerPressed = false;
                    DoThrow(headPose);
                }
            }
#endif

            timer.Tick(() =>
            {
#if DRAW_SAMPLE_CONTENT
                // ── Game logic: time-based updates ──

                // Place Mickey after spatial tracking activates and delay elapses
                if (spatialTrackingActive && !mickeyPlaced)
                {
                    timeSinceStart += (float)timer.ElapsedSeconds;
                    if (timeSinceStart >= SECONDS_BEFORE_SPAWN && headPose != null)
                    {
                        PlaceMickey(headPose);
                    }
                }

                float dt = (float)timer.ElapsedSeconds;
                if (spatialTrackingActive)
                {
                    // GameSession uses fixed substeps, swept collisions, a projectile budget,
                    // floor bounces, score/combo tracking, and a short target respawn interval.
                    GameUpdateResult updateResult = gameSession.Update(dt, worldOrigin, mickeyFloorHeight);
                    if (gameSession.TargetVisible)
                    {
                        mickeyWorldPos = gameSession.TargetPosition;
                        if (mickeyRenderer != null)
                        {
                            mickeyRenderer.Position = mickeyWorldPos;
                        }
                    }

                    if (updateResult.TargetRespawned && latestHeadPose != null)
                    {
                        // Respawn against the player's latest gaze so the next throw begins
                        // as a fresh room-scale encounter instead of at a stale location.
                        PlaceMickey(latestHeadPose);
                    }

                    if (mickeyRenderer != null)
                    {
                        if (updateResult.TargetHit)
                        {
                            mickeyRenderer.TriggerHit();
                        }

                        mickeyRenderer.IsVisible = gameSession.TargetVisible;
                        mickeyRenderer.Update(dt);
                    }
                }
                else if (mickeyRenderer != null)
                {
                    // Do not render or simulate world-locked content without a valid pose.
                    mickeyRenderer.IsVisible = false;
                }
#endif
            });

            // Set focus point on platforms that don't support CommitDirect3D11DepthBuffer
            foreach (var cameraPose in prediction.CameraPoses)
            {
#if DRAW_SAMPLE_CONTENT
                HolographicCameraRenderingParameters renderingParameters = holographicFrame.GetRenderingParameters(cameraPose);
                if (stationaryReferenceFrame != null && mickeyPlaced)
                {
                    renderingParameters.SetFocusPoint(
                        stationaryReferenceFrame.CoordinateSystem,
                        mickeyWorldPos);
                }
#endif
            }

            // The holographic frame will be used to get up-to-date view and projection matrices and
            // to present the swap chain.
            return holographicFrame;
        }

        /// <summary>
        /// Spawns a potato projectile in the direction of the predicted gaze pose.
        /// </summary>
        void DoThrow(SpatialPointerPose headPose)
        {
            if (headPose == null)
            {
                return;
            }

            Vector3 hpos = headPose.Head.Position;
            Vector3 hdir = headPose.Head.ForwardDirection;
            if (hdir.LengthSquared() < 0.0001f)
            {
                return;
            }

            hdir = Vector3.Normalize(hdir);
            Vector3 velocity = hdir * 3.5f;
            bool wasAccepted = gameSession.TryThrow(new Potato
            {
                Position = hpos + hdir * 0.1f,
                Velocity = velocity,
                Lifetime = 0f
            });

            if (!wasAccepted)
            {
                Debug.WriteLine("Potato throw ignored because the active projectile budget is full.");
            }
        }

        /// <summary>
        /// Places Mickey 1.5 meters in front of the user's gaze direction, on the floor plane.
        /// </summary>
        void PlaceMickey(SpatialPointerPose headPose)
        {
            if (headPose == null) return;

            Vector3 gazeFlat = headPose.Head.ForwardDirection;
            gazeFlat.Y = 0;
            if (gazeFlat.Length() < 0.01f)
                gazeFlat = new Vector3(0, 0, -1);
            gazeFlat = Vector3.Normalize(gazeFlat);

            mickeyWorldPos = headPose.Head.Position
                + gazeFlat * 1.5f
                + new Vector3(0, -0.3f, 0);

            mickeyFloorHeight = mickeyWorldPos.Y;
            gameSession.SpawnTarget(mickeyWorldPos);
            mickeyRenderer.Position = gameSession.TargetPosition;
            mickeyRenderer.IsVisible = true;
            mickeyPlaced = true;
        }

        // ────────────── RENDER ──────────────

        /// <summary>
        /// Renders the current frame to each holographic display, according to the
        /// current application and spatial positioning state. Returns true if the
        /// frame was rendered to at least one display.
        /// </summary>
        public bool Render(HolographicFrame holographicFrame)
        {
            // Don't try to render anything before the first Update.
            if (timer.FrameCount == 0)
            {
                return false;
            }

            // Up-to-date frame predictions enhance the effectiveness of image stabilization and
            // allow more accurate positioning of holograms.
            holographicFrame.UpdateCurrentPrediction();
            HolographicFramePrediction prediction = holographicFrame.CurrentPrediction;

            // Lock the set of holographic camera resources, then draw to each camera
            // in this frame.
            return deviceResources.UseHolographicCameraResources(
                (Dictionary<uint, CameraResources> cameraResourceDictionary) =>
                {
                    bool atLeastOneCameraRendered = false;

                    foreach (var cameraPose in prediction.CameraPoses)
                    {
                        // This represents the device-based resources for a HolographicCamera.
                        CameraResources cameraResources = cameraResourceDictionary[cameraPose.HolographicCamera.Id];

                        // Get the device context.
                        var context = deviceResources.D3DDeviceContext;
                        var renderTargetView = cameraResources.BackBufferRenderTargetView;
                        var depthStencilView = cameraResources.DepthStencilView;

                        // Set render targets to the current holographic camera.
                        context.OutputMerger.SetRenderTargets(depthStencilView, renderTargetView);

                        // Clear the back buffer and depth stencil view.
                        if (canGetHolographicDisplayForCamera &&
                            cameraPose.HolographicCamera.Display.IsOpaque)
                        {
                            SharpDX.Mathematics.Interop.RawColor4 cornflowerBlue =
                                new SharpDX.Mathematics.Interop.RawColor4(0.392156899f, 0.58431375f, 0.929411829f, 1.0f);
                            context.ClearRenderTargetView(renderTargetView, cornflowerBlue);
                        }
                        else
                        {
                            SharpDX.Mathematics.Interop.RawColor4 transparent =
                                new SharpDX.Mathematics.Interop.RawColor4(0.0f, 0.0f, 0.0f, 0.0f);
                            context.ClearRenderTargetView(renderTargetView, transparent);
                        }

                        context.ClearDepthStencilView(
                            depthStencilView,
                            SharpDX.Direct3D11.DepthStencilClearFlags.Depth | SharpDX.Direct3D11.DepthStencilClearFlags.Stencil,
                            1.0f,
                            0);

                        // The view and projection matrices for each holographic camera will change
                        // every frame. This function refreshes the data in the constant buffer for
                        // the holographic camera indicated by cameraPose.
                        if (stationaryReferenceFrame != null)
                        {
                            cameraResources.UpdateViewProjectionBuffer(deviceResources, cameraPose, stationaryReferenceFrame.CoordinateSystem);
                        }

                        // Attach the view/projection constant buffer for this camera to the graphics pipeline.
                        bool cameraActive = cameraResources.AttachViewProjectionBuffer(deviceResources);

#if DRAW_SAMPLE_CONTENT
                        // Only render world-locked content when positional tracking is active.
                        if (cameraActive)
                        {
                            // Draw the Mickey hologram (Steamboat Willie)
                            if (mickeyRenderer != null)
                            {
                                mickeyRenderer.Render();
                            }

                            // Render each active potato projectile owned by the game session.
                            for (int i = 0; i < gameSession.Potatoes.Count; i++)
                            {
                                Potato projectile = gameSession.Potatoes[i];
                                if (!projectile.HasCollided && potatoRenderer != null)
                                {
                                    potatoRenderer.Draw(projectile);
                                }
                            }
                        }
#endif
                        atLeastOneCameraRendered = true;
                    }

                    return atLeastOneCameraRendered;
                });
        }

        // ────────────── LIFE CYCLE ──────────────

        public void SaveAppState() { }
        public void LoadAppState() { }

        public void OnPointerPressed()
        {
            this.pointerPressed = true;
        }

        void OnDeviceLost(Object sender, EventArgs e)
        {
#if DRAW_SAMPLE_CONTENT
            mickeyRenderer?.ReleaseDeviceDependentResources();
            potatoRenderer?.ReleaseDeviceDependentResources();
#endif
        }

        void OnDeviceRestored(Object sender, EventArgs e)
        {
#if DRAW_SAMPLE_CONTENT
            mickeyRenderer?.CreateDeviceDependentResourcesAsync();
            potatoRenderer?.CreateDeviceDependentResourcesAsync();
#endif
        }

        void OnLocatabilityChanged(SpatialLocator sender, object args)
        {
            switch (sender.Locatability)
            {
                case SpatialLocatability.Unavailable:
                case SpatialLocatability.PositionalTrackingActivating:
                case SpatialLocatability.OrientationOnly:
                case SpatialLocatability.PositionalTrackingInhibited:
                    spatialTrackingActive = false;
                    pointerPressed = false;
                    latestHeadPose = null;
                    Debug.WriteLine("Positional tracking is " + sender.Locatability + "; gameplay is paused.");
                    break;

                case SpatialLocatability.PositionalTrackingActive:
                    spatialTrackingActive = true;
                    break;
            }
        }

        void OnCameraAdded(HolographicSpace sender, HolographicSpaceCameraAddedEventArgs args)
        {
            Deferral deferral = args.GetDeferral();
            HolographicCamera holographicCamera = args.Camera;

            Task.Run(() =>
            {
                deviceResources.AddHolographicCamera(holographicCamera);
                deferral.Complete();
            });
        }

        void OnCameraRemoved(HolographicSpace sender, HolographicSpaceCameraRemovedEventArgs args)
        {
            deviceResources.RemoveHolographicCamera(args.Camera);
        }

        void OnGamepadAdded(object sender, Windows.Gaming.Input.Gamepad gamepad)
        {
            if (gamepads.Exists(x => x.gamepad == gamepad)) return;
            gamepads.Add(new GamepadWithButtonState(gamepad, false));
        }

        void OnGamepadRemoved(object sender, Windows.Gaming.Input.Gamepad gamepad)
        {
            gamepads.RemoveAll(x => x.gamepad == gamepad);
        }

        void OnHolographicDisplayIsAvailableChanged(object sender, object args)
        {
            // Get the spatial locator for the default HolographicDisplay, if one is available.
            SpatialLocator nextSpatialLocator = null;
            if (canGetDefaultHolographicDisplay)
            {
                HolographicDisplay defaultHolographicDisplay = HolographicDisplay.GetDefault();
                if (defaultHolographicDisplay != null)
                {
                    nextSpatialLocator = defaultHolographicDisplay.SpatialLocator;
                }
            }
            else
            {
                nextSpatialLocator = SpatialLocator.GetDefault();
            }

            if (spatialLocator != nextSpatialLocator)
            {
                if (spatialLocator != null)
                {
                    spatialLocator.LocatabilityChanged -= this.OnLocatabilityChanged;
                }

                spatialLocator = nextSpatialLocator;
                if (spatialLocator != null)
                {
                    spatialLocator.LocatabilityChanged += this.OnLocatabilityChanged;
                }
            }

            if (spatialLocator != null)
            {
                // The simplest way to render world-locked holograms is to create
                // a "world" coordinate system with the origin placed at the device's position
                // as the app is launched.
                stationaryReferenceFrame = spatialLocator.CreateStationaryFrameOfReferenceAtCurrentLocation();
            }
            else
            {
                stationaryReferenceFrame = null;
            }
        }
    }
}
