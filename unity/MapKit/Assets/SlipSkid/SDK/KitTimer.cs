using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Switches objects on and off on the game's shared clock (the same for every player): traffic lights, level-
    // crossing lights, warning signs – in step with the KitMovers they announce (same period and phase maths).
    // MapKitLoader 1.3.0+.
    public class KitTimer : MonoBehaviour
    {
        [Tooltip("Seconds of a whole cycle.")]
        public float period = 10f;

        [Tooltip("Offset in the cycle (0 to 1).")]
        public float phase;

        [Tooltip("Share of the cycle (from its start) when it is 'on' (0 to 1).")]
        public float onShare = 0.5f;

        [Tooltip("Shown while on, hidden while off.")]
        public GameObject[] showWhenOn = new GameObject[0];

        [Tooltip("Shown while off, hidden while on.")]
        public GameObject[] showWhenOff = new GameObject[0];
    }
}
