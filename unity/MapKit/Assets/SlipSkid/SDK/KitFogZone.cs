using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Box (BoxCollider, trigger) where the fog closes in around the car on screen: a sandstorm, a sea fog, smoke.
    // Local and visual only. MapKitLoader 1.3.0+.
    public class KitFogZone : MonoBehaviour
    {
        [Tooltip("Inside: distance where the fog starts (m).")]
        public float fogStart = 4f;

        [Tooltip("Inside: distance where nothing is visible any more (m).")]
        public float fogEnd = 45f;

        [Tooltip("Metres outside the box over which the fog closes in.")]
        public float blend = 30f;
    }
}
