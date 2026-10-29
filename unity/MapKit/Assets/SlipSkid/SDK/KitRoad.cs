using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    public enum KitRoadKind
    {
        // Driveable asphalt: a game road tile, repainted by surface cards.
        Road,

        // Concrete edge or wall: a game road tile in concrete, not repainted.
        Border,

        // Decoration in concrete, not a tile (pillars, props).
        Concrete,

        // Decoration in the road's dark asphalt, not a tile (buildings, props).
        Dark,
    }

    // Any mesh that is part of the track. Needs a MeshFilter + MeshRenderer (+ a collider to be solid).
    [DisallowMultipleComponent]
    public class KitRoad : MonoBehaviour
    {
        public KitRoadKind kind = KitRoadKind.Road;

        [Tooltip("Stable unique id; surface swaps are networked by it. Filled in by the kit.")]
        public string tileGuid = "";

        [Tooltip("Road tiles: game surface to use (e.g. \"Asphalt\", \"Mud\", \"Ice\"; matched by name). Empty = the host level's road surface.")]
        public string surface = "";

        [Tooltip("Decoration (Concrete / Dark): paint it with a game material by name instead, e.g. \"ToonGreen\", " +
                 "\"ToonWhite\", \"ToonWood\", \"ToonBeige\" (the game's toon props). Needs MapKitLoader 1.1.0+; older " +
                 "loaders paint it concrete / dark asphalt.")]
        public string paint = "";

        [Tooltip("With paint: colour applied to the material (_BaseColor). Alpha 0 = the material's own colour.")]
        public Color tint = new Color(1f, 1f, 1f, 0f);

        [Tooltip("Road tiles of a seasonal map (KitMap.seasonalBiomes): the season this tile belongs to, as the surface " +
                 "whose look it shows (\"Dirt\", \"Sand\", \"Mud\", \"Ice\", \"Magma\"). The look switches when a car reaches " +
                 "a tile of another season, whatever its surface is now (surface cards repaint tiles). Empty = the look " +
                 "follows the surface under the car. MapKitLoader 1.1.0+.")]
        public string season = "";

        [Tooltip("Road tiles: the surface this tile looks like, when it differs from how it drives (black ice: surface " +
                 "\"Ice\", look \"Asphalt\"). Empty = it looks like its surface. MapKitLoader 1.3.0+.")]
        public string look = "";
    }
}
