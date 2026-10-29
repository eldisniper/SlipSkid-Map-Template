using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Pose for the game's start gate. Same convention as stock levels: at road height, forward
    // pointing up, up pointing to the cars' left, behind the grid.
    public class KitStart : MonoBehaviour
    {
    }
}
