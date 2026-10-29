using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Trigger box (BoxCollider, isTrigger) that speeds cars up.
    public class KitBoostPad : MonoBehaviour
    {
        [Tooltip("Speed added along the car's forward when it enters.")]
        public float boostSpeed = 20f;

        [Tooltip("Off: boostSpeed is always added. On: the game's own behaviour - cars slower than boostSpeed get boostSpeed added, and continuousAcceleration pushes while inside.")]
        public bool capAtBoostSpeed;

        [Tooltip("Push along this object's forward while a car is inside (only with capAtBoostSpeed).")]
        public float continuousAcceleration;
    }
}
