using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // A spot for scenery. In game, the loader copies a piece of the host level's current environment
    // (mountains, city blocks...) here, sized to fit, so decoration follows the surface's biome.
    public class KitDecor : MonoBehaviour
    {
        [Tooltip("Width of the piece, in metres. Its base sits on this marker.")]
        public float size = 40f;

        [Tooltip("Only use environment pieces whose name contains this (e.g. Mountain, City). Empty = any.")]
        public string prefer = "";
    }
}
