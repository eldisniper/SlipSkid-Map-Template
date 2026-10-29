using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Trigger box (BoxCollider) that pushes cars along this object's forward: a crosswind (forward = sideways), a
    // geyser or launch pad (forward = up, launchSpeed), a current. Gusts follow the game's server clock (the same for
    // everyone). MapKitLoader 1.3.0+.
    public class KitForceZone : MonoBehaviour
    {
        [Tooltip("Push while a car is inside, m/s² along forward (the game's gravity is 40).")]
        public float acceleration = 25f;

        [Tooltip("On entering, cars slower than this along forward are set to it (m/s); 0 = none. Geysers: 25-35.")]
        public float launchSpeed;

        [Tooltip("Seconds of a gust cycle; 0 = blows all the time.")]
        public float gustPeriod;

        [Tooltip("Share of the gust cycle it blows (0 to 1).")]
        public float gustOn = 0.5f;

        [Tooltip("Offset in the gust cycle (0 to 1), to stagger zones.")]
        public float phase;

        [Tooltip("Shown only while it blows (flags, dust, a geyser's plume); optional.")]
        public GameObject showWhenOn;
    }
}
