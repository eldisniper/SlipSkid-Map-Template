using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    public enum KitMoverMode
    {
        // There and back every `period` seconds, waiting `hold` at each end (drawbridge, piston, sliding wall).
        PingPong,

        // Turns all the time at `rotateBy` degrees per second (turntable, gears, a lighthouse beam).
        Spin,

        // Moves once, `startAfter` seconds into each race, over `duration` (a bridge that opens for the last cars).
        OnceAfter,

        // One way, again and again (a train, traffic): from rest to `moveBy` over the first (1 - hold) of each `period`,
        // waits at the far end (out of sight) for the rest, then starts again from rest. MapKitLoader 1.3.0+.
        Loop,
    }

    // Moves this object and everything under it (road tiles, walls, decoration) on the game's server clock, so every
    // player sees it in the same place at the same time without any network traffic. This object is the pivot.
    // MapKitLoader 1.3.0+.
    public class KitMover : MonoBehaviour
    {
        public KitMoverMode mode = KitMoverMode.PingPong;

        [Tooltip("PingPong / OnceAfter: offset reached at the far end, in metres along the parent's axes.")]
        public Vector3 moveBy;

        [Tooltip("PingPong / OnceAfter: rotation reached at the far end (degrees about this object's own axes). Spin: degrees per second.")]
        public Vector3 rotateBy;

        [Tooltip("PingPong: seconds for a whole cycle, there and back.")]
        public float period = 8f;

        [Tooltip("PingPong: share of each half cycle spent waiting at the end (0 to 0.9). Loop: share of the period spent waiting at the far end.")]
        public float hold = 0.4f;

        [Tooltip("PingPong / Spin / Loop: offset in the cycle (0 to 1), to stagger several movers.")]
        public float phase;

        [Tooltip("OnceAfter: seconds after the start of each race when it starts moving.")]
        public float startAfter = 40f;

        [Tooltip("OnceAfter: seconds the move takes.")]
        public float duration = 3f;

        [Tooltip("Keep traps off the space it sweeps (a no-trap box like the start's).")]
        public bool noTraps = true;
    }
}
