using Windows.UI.Input.Spatial;

namespace HololensGo.Common
{
    // Sample gesture handler.
    // Hooks up events to recognize a tap gesture, and keeps track of input using a boolean value.
    public class SpatialInputHandler : System.IDisposable
    {
        // API objects used to process gesture input, and generate gesture events.
        private SpatialInteractionManager interactionManager;

        // Used to indicate that a Pressed input event was received this frame.
        private SpatialInteractionSourceState sourceState;

        // Creates and initializes a GestureRecognizer that listens to a Person.
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
