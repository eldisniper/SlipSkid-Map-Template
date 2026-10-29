using System.Collections.Generic;
using UnityEngine;

// Slip & Skid Map Kit marker. Read by the SlipSkid.MapKit.Loader plugin when the map loads. Compiled by
// Unity (Assets/SlipSkid/SDK, assembly SlipSkid.MapKit.SDK) and by the loader build from the same files;
// Unity needs one MonoBehaviour per file, named after the class. Keep class names and fields stable.
namespace SlipSkid.MapKit.SDK
{
    // Starting grid, in order. Children are used when the list is empty.
    public class KitSpawns : MonoBehaviour
    {
        public List<Transform> points = new List<Transform>();

        public List<Transform> Resolve()
        {
            if (points != null && points.Count > 0) return points;
            var list = new List<Transform>();
            foreach (Transform child in transform) list.Add(child);
            return list;
        }
    }
}
