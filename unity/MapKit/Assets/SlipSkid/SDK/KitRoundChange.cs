using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    public enum KitRoundChangeKind
    {
        // This object (and everything under it) appears from race `fromRace` on.
        Show,

        // This object disappears from race `fromRace` on.
        Hide,

        // The road tiles under this object take `surface` from race `fromRace` on (an eruption, a freeze).
        Surface,

        // The sea – and the death plane with it – rises `risePerRace` metres a race from race `fromRace` on.
        RaiseSea,
    }

    // A map that changes from race to race. Race 1 is the first race of a match (warm-up included); the change shows
    // from the draft before that race, so traps are placed on the map as it will be. Follows the game's networked
    // round counter: the same on every machine. MapKitLoader 1.3.0+.
    public class KitRoundChange : MonoBehaviour
    {
        public KitRoundChangeKind kind = KitRoundChangeKind.Show;

        [Tooltip("First race the change applies to (1 = the first race of a match).")]
        public int fromRace = 2;

        [Tooltip("Surface: the game surface the tiles under this object get (\"Magma\", \"Ice\", \"Mud\"...).")]
        public string surface = "Magma";

        [Tooltip("RaiseSea: metres the sea rises each race.")]
        public float risePerRace = 2f;

        [Tooltip("RaiseSea: the most it rises in total (metres).")]
        public float maxRise = 8f;
    }
}
