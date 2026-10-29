using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Root of a map. Everything else lives under it; it is saved as the prefab in the bundle.
    [DisallowMultipleComponent]
    public class KitMap : MonoBehaviour
    {
        public string mapName = "My Map";

        public string author = "";

        [Range(1, 5)]
        public int difficulty = 1;

        public bool isReversible;

        [Tooltip("Stock level whose scenery, lighting and music this map uses. Empty = the first stock level.")]
        public string hostLevel = "";

        [Tooltip("How far below the lowest road the sea (the level's fog plane) sits, in metres; the game kills cars 3.5 m " +
                 "under it. 0 = the host level's own depth. Before MapKitLoader 1.3.0 a value smaller than the host's " +
                 "own depth (about 6.5 m on Snake) was raised to it.")]
        public float deathDepth = 30f;

        [Tooltip("Seasons: each player's sky, fog, lighting, scenery, weather and ambiance follow the surface of the road " +
                 "under their own car (Dirt = spring, Sand = summer, Mud = autumn, Ice = winter, Magma). Asphalt keeps " +
                 "the current look. Needs MapKitLoader 1.1.0+ (older loaders ignore it).")]
        public bool seasonalBiomes;
    }
}
