using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // A collider (made a trigger by the loader) that destroys any car touching it: a passing train, cross traffic, a
    // wrecking ball. The game's own death (lives, respawn, spectating), with no trap owner to score it. Put it under a
    // KitMover so it moves. MapKitLoader 1.3.0+.
    public class KitHazard : MonoBehaviour
    {
    }
}
