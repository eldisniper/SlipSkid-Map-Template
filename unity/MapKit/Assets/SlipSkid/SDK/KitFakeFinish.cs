using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // A decoy: the loader puts a copy of the game's finish arch here (like KitFinish: 1.5 m above the road,
    // forward = direction of travel) with nothing behind it – no trigger, no collision. MapKitLoader 1.3.0+.
    public class KitFakeFinish : MonoBehaviour
    {
    }
}
