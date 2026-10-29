using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Trigger box (BoxCollider): a car driving into it comes out at `exit` – on the road there, heading along its
    // forward – with its speed. Each player's game moves its own car (cars are driven by their owner): nothing is
    // networked. A portal may be a shortcut or a trap that sends you back. MapKitLoader 1.3.0+.
    public class KitPortal : MonoBehaviour
    {
        [Tooltip("Where cars come out: position on the road, forward = direction of travel, up = the road's up.")]
        public Transform exit;

        [Tooltip("Speed along exit's forward when coming out (m/s); 0 = the speed the car had.")]
        public float exitSpeed;

        [Tooltip("Slowest a car comes out (m/s), so a car crawling in still clears the exit.")]
        public float minSpeed = 15f;
    }
}
