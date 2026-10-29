using UnityEngine;

namespace SlipSkid.MapKit.SDK
{
    // One of the game's own road pieces (R###B###, named as in the stock levels: see docs/MAP_PROMPT.md "Stock
    // pieces"), placed at this object's pose. Nothing of the piece ships in the map: the loader (MapKitLoader 1.2.0+)
    // clones the real piece from the host level, or from `fromLevel`, with its borders, collisions and surfaces.
    // Pose: this object's position and rotation are the piece's own frame as the game places it, except that the
    // piece's FBX turn (x 270°) and scale (100) are applied by the loader – scale 1 = the stock size, 2 = the x2 pieces
    // some stock tracks use. The editor shows a preview (Slip & Skid > Stock pieces), never shipped.
    [AddComponentMenu("Slip & Skid/Kit Stock Piece")]
    public class KitStockPiece : MonoBehaviour
    {
        [Tooltip("Stock piece name, e.g. R018B101 (a 20 m quarter turn with both borders).")]
        public string piece = "";

        [Tooltip("Stock level to take the piece from (e.g. 'Danibugne'); empty = the host level. Each other level is loaded once per session, behind the loading screen.")]
        public string fromLevel = "";

        [Tooltip("Surface of the road part (Asphalt, Concrete, Dirt, Sand, Mud, Ice, Magma); empty = as the piece comes.")]
        public string surface = "";

        [Tooltip("Surface of its border parts; empty = as the piece comes.")]
        public string borderSurface = "";

        [Tooltip("Comma-separated parts to leave out (e.g. Border001B101I001), like the bare copies some stock tracks use.")]
        public string withoutParts = "";

        [Tooltip("Seasonal maps: the season this piece's road shows (like KitRoad.season).")]
        public string season = "";

        [HideInInspector] public string tileGuid = "";
    }
}
