using System.Globalization;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using static Oculus.Interaction.TransformerUtils;

public class GISMappingControllerV2 : MonoBehaviour
{
    [Header("References")]
    public Transform germanyRoot;           // "germany_texture" Parent
    public TextAsset csv;                   // kw_locations.csv (UTF-8)
    public GameObject markerPrefab;
    public ModelFactory modelFactory;

    [Header("Geo bounds (Germany)")]
    public Vector2 latRange = new Vector2(47.54935f, 54.80345f);
    public Vector2 lonRange = new Vector2(6.114f, 14.65504f);

    [Header("Placement")]
    public bool useRaycastToSurface = true;
    public float yOffset = 0.02f;
    public bool invertZ = false;

    [Header("Box Collider Handling")]
    public bool useMeshColliderOnly = true;  // Only use mesh colliders, ignore box colliders
    public LayerMask meshColliderLayerMask = -1; // Layer mask for mesh colliders only
    public bool validateWithMeshBounds = true;   // Use mesh bounds for validation
    public float boundaryMargin = 0.95f;         // Use 95% of mesh bounds

    [Header("Debug")]
    public bool showDebugMarkers = true;
    public bool logPlacementDetails = false;

    private Bounds worldBounds;
    private List<Collider> meshColliders = new List<Collider>();
    private List<Collider> boxColliders = new List<Collider>();

    void Awake()
    {
        // Calculate Germany model bounds
        CalculateWorldBounds();
        
        // Analyze colliders to handle box collider issue
        AnalyzeColliders();
        
        // Configure layer mask for mesh colliders only
        ConfigureMeshColliderLayerMask();
        
        Place();
    }

    void CalculateWorldBounds()
    {
        var rends = germanyRoot.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            Debug.LogError("Kein Renderer unter germanyRoot gefunden.");
            return;
        }
        
        worldBounds = rends[0].bounds;
        foreach (var r in rends) worldBounds.Encapsulate(r.bounds);
        
        Debug.Log($"Germany model bounds: min=({worldBounds.min.x:F2}, {worldBounds.min.z:F2}), max=({worldBounds.max.x:F2}, {worldBounds.max.z:F2})");
        Debug.Log($"Germany model size: ({worldBounds.size.x:F2}, {worldBounds.size.z:F2})");
    }

    void AnalyzeColliders()
    {
        var allColliders = germanyRoot.GetComponentsInChildren<Collider>();
        
        meshColliders.Clear();
        boxColliders.Clear();
        
        foreach (var col in allColliders)
        {
            if (col is MeshCollider)
            {
                meshColliders.Add(col);
            }
            else if (col is BoxCollider)
            {
                boxColliders.Add(col);
            }
        }
        
        Debug.Log($"Found {meshColliders.Count} mesh colliders and {boxColliders.Count} box colliders");
        
        if (boxColliders.Count > 0)
        {
            Debug.LogWarning("Box colliders detected! This can cause objects to be placed outside the actual Germany mesh.");
            Debug.Log("Solution: Using mesh colliders only for raycast validation.");
        }
    }

    void ConfigureMeshColliderLayerMask()
    {
        if (meshColliders.Count == 0)
        {
            Debug.LogWarning("No mesh colliders found! Raycast validation may not work properly.");
            return;
        }

        // Create layer mask that includes only mesh collider layers
        int layerMask = 0;
        foreach (var meshCol in meshColliders)
        {
            layerMask |= (1 << meshCol.gameObject.layer);
        }
        
        meshColliderLayerMask = layerMask;
        Debug.Log($"Configured mesh collider layer mask: {meshColliderLayerMask.value}");
        
        // Log which layers are included
        for (int i = 0; i < 32; i++)
        {
            if ((meshColliderLayerMask.value & (1 << i)) != 0)
            {
                Debug.Log($"  Layer {i} ({LayerMask.LayerToName(i)}): INCLUDED (mesh collider)");
            }
        }
    }

    public void Place()
    {
        if (csv == null || germanyRoot == null)
        {
            Debug.LogError("Bitte References setzen (germanyRoot, csv).");
            return;
        }

        var lines = csv.text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            Debug.LogWarning("CSV leer oder nur Headerzeile.");
            return;
        }

        // Parse header
        char delim = DetectDelimiter(lines[0]);
        string[] header = lines[0].Split(delim);

        // Find column indices
        int LatIdx = FindColumnIndex(header, "lat", "BREITENGRAD_DEG_N", "LAT", "LATITUDE");
        int LonIdx = FindColumnIndex(header, "lon", "LAENGENGRAD_DEG_E", "LON", "LONGITUDE");
        int NameIdx = FindColumnIndex(header, "name", "KRAFTWERKSNAME", "NAME", "STATION");
        int FuelIdx = FindColumnIndex(header, "tech", "ENERGIETRAEGER", "FUEL", "FUELTYPE");
        int Pidx = FindColumnIndex(header, "power_mw", "P_INST_MW", "POWER_MW", "CAPACITY_MW");

        if (LatIdx < 0 || LonIdx < 0)
        {
            Debug.LogError("CSV: Spalten für Breiten-/Längengrad nicht gefunden.");
            return;
        }

        // Cleanup old markers
        CleanupOldMarkers();

        // Create parent for new markers
        var parent = new GameObject("KW_Markers").transform;
        parent.SetParent(germanyRoot, true);

        // Create fuel type groups
        var fuelParents = new Dictionary<string, Transform>(StringComparer.InvariantCultureIgnoreCase);
        var placedKeys = new HashSet<string>();
        int created = 0;
        int skipped = 0;

        // Process CSV data
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(delim);
            if (parts.Length <= Math.Max(LatIdx, LonIdx)) continue;

            if (!TryParseFloat(parts[LatIdx], out float lat)) continue;
            if (!TryParseFloat(parts[LonIdx], out float lon)) continue;

            // Filter by geographic bounds
            if (lat < latRange.x || lat > latRange.y || lon < lonRange.x || lon > lonRange.y) continue;

            // Deduplicate
            string key = lat.ToString("F5", CultureInfo.InvariantCulture) + "_" +
                         lon.ToString("F5", CultureInfo.InvariantCulture);
            if (!placedKeys.Add(key)) continue;

            // Calculate world position
            var worldPos = GeoToWorld(lat, lon);

            // Validate position using mesh bounds (handles box collider issue)
            if (!IsValidPosition(worldPos, lat, lon))
            {
                skipped++;
                continue;
            }

            // Place object on surface
            if (!PlaceOnSurface(ref worldPos))
            {
                skipped++;
                continue;
            }

            // Create marker
            CreateMarker(worldPos, parts, LatIdx, LonIdx, NameIdx, FuelIdx, Pidx, fuelParents, ref created);
        }

        Debug.Log($"Placed {created} markers, skipped {skipped} invalid positions in {fuelParents.Count} fuel groups.");
        
        if (showDebugMarkers)
        {
            CreateDebugMarkers(parent);
        }
    }

    bool IsValidPosition(Vector3 worldPos, float lat, float lon)
    {
        if (!validateWithMeshBounds) return true;

        // Create margin bounds to stay well within Germany
        var marginBounds = new Bounds(worldBounds.center, worldBounds.size * boundaryMargin);
        
        bool isValid = marginBounds.Contains(new Vector3(worldPos.x, worldBounds.center.y, worldPos.z));
        
        if (!isValid && logPlacementDetails)
        {
            Debug.LogWarning($"Position outside Germany mesh bounds: lat={lat:F6}, lon={lon:F6} -> world=({worldPos.x:F2}, {worldPos.z:F2})");
        }
        
        return isValid;
    }

    bool PlaceOnSurface(ref Vector3 worldPos)
    {
        if (!useRaycastToSurface)
        {
            worldPos.y = worldBounds.center.y + yOffset;
            return true;
        }

        // Use mesh colliders only to avoid box collider interference
        if (useMeshColliderOnly && meshColliders.Count > 0)
        {
            if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out var hit, 100f, meshColliderLayerMask))
            {
                worldPos = hit.point + Vector3.up * yOffset;
                return true;
            }
            else
            {
                if (logPlacementDetails)
                {
                    Debug.LogWarning($"No mesh collider hit at position: ({worldPos.x:F2}, {worldPos.z:F2})");
                }
                return false; // Reject if no mesh collider hit
            }
        }
        else
        {
            // Fallback to all colliders
            if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out var hit, 100f))
            {
                worldPos = hit.point + Vector3.up * yOffset;
                return true;
            }
            else
            {
                worldPos.y = worldBounds.center.y + yOffset;
                return true;
            }
        }
    }

    void CreateMarker(Vector3 worldPos, string[] parts, int LatIdx, int LonIdx, int NameIdx, int FuelIdx, int Pidx, 
                     Dictionary<string, Transform> fuelParents, ref int created)
    {
        // Read data
        string name = (NameIdx >= 0 && NameIdx < parts.Length) ? SanitizeName(parts[NameIdx]) : null;
        string fuel = (FuelIdx >= 0 && FuelIdx < parts.Length) ? (parts[FuelIdx]?.Trim() ?? "") : "";
        float power = 0f;
        if (Pidx >= 0 && Pidx < parts.Length) TryParseFloat(parts[Pidx], out power);

        // Create container
        var containerName = !string.IsNullOrEmpty(name) ? name : $"marker_{created}";
        var container = new GameObject(containerName);
        container.transform.position = worldPos;

        // Add to fuel group
        var fuelParent = GetFuelParent(fuel, fuelParents);
        container.transform.SetParent(fuelParent, true);

        // Create icon
        if (modelFactory != null)
        {
            var icon = modelFactory.BuildIcon(fuel, power);
            if (icon != null)
            {
                icon.transform.SetParent(container.transform, false);
                icon.name = "icon";
                
                // Scale icon appropriately
                float powerScale = CalculatePowerScale(power);
                FitIconToMap(icon, worldBounds, 0.01f, powerScale);
                
                // Disable physics
                DisablePhysics(icon);
                
                // Set layer to avoid raycast interference
                SetLayerRecursively(icon.transform, LayerMask.NameToLayer("Ignore Raycast"));
            }
        }
        else
        {
            // Fallback cube
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(fallback.GetComponent<Collider>());
            fallback.transform.SetParent(container.transform, false);
            fallback.transform.localScale = Vector3.one * 0.8f;
            fallback.name = "icon_fallback";
        }

        created++;
    }

    void CreateDebugMarkers(Transform parent)
    {
        var debugParent = new GameObject("Debug_Markers").transform;
        debugParent.SetParent(parent, true);

        // Test positions
        var testPositions = new[]
        {
            new { name = "Center", lat = (latRange.x + latRange.y) * 0.5f, lon = (lonRange.x + lonRange.y) * 0.5f },
            new { name = "SW_Corner", lat = latRange.x, lon = lonRange.x },
            new { name = "NE_Corner", lat = latRange.y, lon = lonRange.y },
            new { name = "Outside_SW", lat = latRange.x - 0.5f, lon = lonRange.x - 0.5f },
            new { name = "Outside_NE", lat = latRange.y + 0.5f, lon = lonRange.y + 0.5f }
        };

        foreach (var pos in testPositions)
        {
            var worldPos = GeoToWorld(pos.lat, pos.lon);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Debug_{pos.name}";
            marker.transform.position = worldPos;
            marker.transform.SetParent(debugParent, true);
            marker.transform.localScale = Vector3.one * 0.05f;

            // Color based on validation
            var renderer = marker.GetComponent<Renderer>();
            bool isValid = IsValidPosition(worldPos, pos.lat, pos.lon);
            renderer.material.color = isValid ? Color.green : Color.red;
            
            Destroy(marker.GetComponent<Collider>());
            
            if (logPlacementDetails)
            {
                Debug.Log($"Debug marker '{pos.name}': lat={pos.lat:F6}, lon={pos.lon:F6} -> world=({worldPos.x:F2}, {worldPos.z:F2}) - {(isValid ? "VALID" : "INVALID")}");
            }
        }

        // Show mesh bounds
        CreateBoundsMarkers(debugParent);
    }

    void CreateBoundsMarkers(Transform parent)
    {
        var boundsParent = new GameObject("Bounds_Markers").transform;
        boundsParent.SetParent(parent, true);

        var corners = new[]
        {
            new Vector3(worldBounds.min.x, worldBounds.center.y, worldBounds.min.z), // SW
            new Vector3(worldBounds.max.x, worldBounds.center.y, worldBounds.min.z), // SE
            new Vector3(worldBounds.min.x, worldBounds.center.y, worldBounds.max.z), // NW
            new Vector3(worldBounds.max.x, worldBounds.center.y, worldBounds.max.z), // NE
        };

        var cornerNames = new[] { "SW", "SE", "NW", "NE" };

        for (int i = 0; i < corners.Length; i++)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"Bounds_{cornerNames[i]}";
            marker.transform.position = corners[i];
            marker.transform.SetParent(boundsParent, true);
            marker.transform.localScale = Vector3.one * 0.02f;

            var renderer = marker.GetComponent<Renderer>();
            renderer.material.color = Color.yellow;
            Destroy(marker.GetComponent<Collider>());
        }

        // Show margin bounds
        if (validateWithMeshBounds)
        {
            var marginBounds = new Bounds(worldBounds.center, worldBounds.size * boundaryMargin);
            var marginCorners = new[]
            {
                new Vector3(marginBounds.min.x, marginBounds.center.y, marginBounds.min.z),
                new Vector3(marginBounds.max.x, marginBounds.center.y, marginBounds.min.z),
                new Vector3(marginBounds.min.x, marginBounds.center.y, marginBounds.max.z),
                new Vector3(marginBounds.max.x, marginBounds.center.y, marginBounds.max.z),
            };

            for (int i = 0; i < marginCorners.Length; i++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"Margin_{cornerNames[i]}";
                marker.transform.position = marginCorners[i];
                marker.transform.SetParent(boundsParent, true);
                marker.transform.localScale = Vector3.one * 0.01f;

                var renderer = marker.GetComponent<Renderer>();
                renderer.material.color = Color.cyan;
                Destroy(marker.GetComponent<Collider>());
            }
        }
    }

    // Helper methods
    char DetectDelimiter(string headerLine)
    {
        if (headerLine.Contains(";")) return ';';
        if (headerLine.Contains("\t")) return '\t';
        return ',';
    }

    bool TryParseFloat(string s, out float v)
    {
        s = (s ?? "").Replace("\"", "").Trim();
        s = s.Replace(",", ".");
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    int FindColumnIndex(string[] header, params string[] candidates)
    {
        for (int i = 0; i < header.Length; i++)
        {
            var h = header[i].Trim().ToUpperInvariant();
            foreach (var c in candidates)
            {
                if (h.Equals(c.Trim().ToUpperInvariant()))
                    return i;
            }
        }
        return -1;
    }

    string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '_');
        return raw;
    }

    Transform GetFuelParent(string fuelName, Dictionary<string, Transform> fuelParents)
    {
        fuelName = string.IsNullOrWhiteSpace(fuelName) ? "_Unbekannt" : fuelName.Trim();
        if (!fuelParents.TryGetValue(fuelName, out var t))
        {
            var go = new GameObject(fuelName);
            go.transform.SetParent(fuelParents.Values.FirstOrDefault()?.parent ?? transform, true);
            t = go.transform;
            fuelParents[fuelName] = t;
        }
        return t;
    }

    float CalculatePowerScale(float power)
    {
        return (power <= 0f) ? 0.8f : Mathf.Clamp(0.6f + Mathf.Sqrt(power) * 0.06f, 0.6f, 2.5f);
    }

    void FitIconToMap(GameObject icon, Bounds mapBounds, float targetFracXZ, float powerScale = 1f)
    {
        if (icon == null) return;

        var rends = icon.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) return;

        var iconBounds = rends[0].bounds;
        foreach (var r in rends) iconBounds.Encapsulate(r.bounds);

        float iconHeight = Mathf.Max(0.0001f, iconBounds.size.y);
        Vector2 xz = new Vector2(mapBounds.size.x, mapBounds.size.z);
        float mapDiagXZ = xz.magnitude;
        float targetHeight = mapDiagXZ * targetFracXZ * powerScale;
        float factor = Mathf.Clamp(targetHeight / iconHeight, 0.05f, 50f);

        icon.transform.localScale *= factor;
    }

    void DisablePhysics(GameObject obj)
    {
        foreach (var rb in obj.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        foreach (var col in obj.GetComponentsInChildren<Collider>(true))
        {
            col.isTrigger = true;
        }
    }

    void SetLayerRecursively(Transform t, int layer)
    {
        if (layer >= 0)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }
    }

    void CleanupOldMarkers()
    {
        var existingParent = germanyRoot.Find("KW_Markers");
        if (existingParent != null)
        {
#if UNITY_EDITOR
            GameObject.DestroyImmediate(existingParent.gameObject);
#else
            GameObject.Destroy(existingParent.gameObject);
#endif
        }
    }

    Vector3 GeoToWorld(float lat, float lon)
    {
        // Direct linear mapping from lat/lon to world coordinates
        float nx = Mathf.InverseLerp(lonRange.x, lonRange.y, lon);
        float nz = Mathf.InverseLerp(latRange.x, latRange.y, lat);

        // Map to Unity world bounds
        float x = Mathf.Lerp(worldBounds.min.x, worldBounds.max.x, nx);
        float z = Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, invertZ ? 1f - nz : nz);

        return new Vector3(x, worldBounds.max.y, z);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Place();
        }
    }
}
