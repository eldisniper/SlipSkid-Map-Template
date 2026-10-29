Map scenes are generated here (`<Map>/<Map>.unity`, its prefab, `TrackMeshes.asset`, preview materials,
`mapsettings.json`) by `python tools/build_map.py maps/<Map>/map.json` or the **Slip & Skid** menu. They are rebuilt
from the JSON file every time and not committed. A map you edit by hand in Unity must be un-ignored in `.gitignore`
(`!unity/MapKit/Assets/Maps/<Map>/`) – and is no longer rebuilt from its JSON.
