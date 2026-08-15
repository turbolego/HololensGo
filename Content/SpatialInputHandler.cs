using Windows.UI.Input.Spatial;

namespace HololensGo.Common
{
    // Gesture handler that queues the latest spatial press for consumption by the game loop.
    public class SpatialInputHandler : System.IDisposable
    {
        // API objects used to process gesture input, and generate gesture events.
        private SpatialInteractionManager interactionManager;

        // Holds the latest press received since the game loop last consumed input.
        private SpatialInteractionSourceState sourceState;

        // Creates and initializes the spatial interaction listener for the current view.
        public SpatialInputHandler()
        {
            // The interaction manager provides an event that informs the app when
            // spatial interactions are detected.
            interactionManager = SpatialInteractionManager.GetForCurrentView();

            // Bind a handler to the SourcePressed event.
            interactionManager.SourcePressed += this.OnSourcePressed;

            //
            // TODO: Expand this class to use other gesture-based input events as applicable to
            //       your app.
            //
        }

        // Checks if the user performed an input gesture since the last call to this method.
        // Allows the main update loop to check for asynchronous changes to the user
        // input state.
        public SpatialInteractionSourceState CheckForInput()
        {
            SpatialInteractionSourceState sourceState = this.sourceState;
            this.sourceState = null;
            return sourceState;
        }

        public void OnSourcePressed(SpatialInteractionManager sender, SpatialInteractionSourceEventArgs args)
        {
            // Keep only the latest press; the main loop consumes it once per update.
            sourceState = args.State;
        }

        public void Dispose()
        {
            if (interactionManager != null)
            {
                interactionManager.SourcePressed -= this.OnSourcePressed;
                interactionManager = null;
            }

            sourceState = null;
        }
    }
}
