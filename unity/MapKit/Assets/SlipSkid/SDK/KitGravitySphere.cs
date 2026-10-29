using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Spherical gravity. Negative gravity pushes outwards: put one at the centre of a loop.
    public class KitGravitySphere : MonoBehaviour
    {
        public float gravity = -90f;

        public float innerRadius;

        public float outerRadius = 20f;
    }
}
