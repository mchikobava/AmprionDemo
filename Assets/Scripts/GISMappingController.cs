using System.Globalization;
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using static Oculus.Interaction.TransformerUtils;


public class GISMappingController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("References")]
    public Transform germanyRoot;           // "germany_texture" Parent
    public TextAsset csv;                   // kw_locations.csv (UTF-8)
    public GameObject markerPrefab;

    [Header("Geo bounds (Germany)")]
    public Vector2 latRange = new Vector2(47.54935f, 54.80345f);
    public Vector2 lonRange = new Vector2(6.114f, 14.65504f);

    [Header("Placement")]
    public bool useRaycastToSurface = true; // MeshCollider nötig
    public float yOffset = 0.02f;           // etwas über der Oberfläche
    public bool invertZ = false;            // falls Nord/Süd gespiegelt wirkt -> anmachen
    public ModelFactory modelFactory;

    Bounds worldBounds;

    void Awake()
    {
        // Gesamt-Bounds des Deutschland-Modells ermitteln
        var rends = germanyRoot.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            Debug.LogError("Kein Renderer unter germanyRoot gefunden.");
            return;
        }
        worldBounds = rends[0].bounds;
        foreach (var r in rends) worldBounds.Encapsulate(r.bounds);
        Place();
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

    // --- Helpers ----------------------------------------------------------
    char DetectDelim(string headerLine)
    {
        if (headerLine.Contains(";")) return ';';
        if (headerLine.Contains("\t")) return '\t';
        return ',';
    }

    bool TryParseFloat(string s, out float v)
    {
        s = (s ?? "").Replace("\"", "").Trim();
        s = s.Replace(",", "."); // deutsch -> invariant
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    int FindIndex(string[] headerArr, params string[] candidates)
    {
        for (int i = 0; i < headerArr.Length; i++)
        {
            var h = headerArr[i].Trim().ToUpperInvariant();
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

    // --- Parse header -----------------------------------------------------
    char delim = DetectDelim(lines[0]);
    string[] header = lines[0].Split(delim);

    // Spalten-Indizes (mit Alternativen)
    int LatIdx = FindIndex(header, "BREITENGRAD_DEG_N", "LAT", "LATITUDE", "BREITENGRAD", "Y");
    int LonIdx = FindIndex(header, "LAENGENGRAD_DEG_E", "LON", "LONGITUDE", "LÄNGENGRAD", "X");
    int NameIdx = FindIndex(header, "KRAFTWERKSNAME", "NAME", "STATION", "PLANTNAME");
    int FuelIdx = FindIndex(header, "ENERGIETRAEGER", "FUEL", "FUELTYPE", "ENERGIETRÄGER");
    int Pidx = FindIndex(header, "P_INST_MW", "P_INSTALLED_MW", "POWER_MW", "CAPACITY_MW");

    if (LatIdx < 0 || LonIdx < 0)
    {
        Debug.LogError("CSV: Spalten für Breiten-/Längengrad nicht gefunden.");
        return;
    }

    // --- Cleanup: alte Marker entfernen ----------------------------------
    var existingParent = germanyRoot.Find("KW_Markers");
    if (existingParent != null)
    {
#if UNITY_EDITOR
        GameObject.DestroyImmediate(existingParent.gameObject);
#else
        GameObject.Destroy(existingParent.gameObject);
#endif
    }

    // Haupt-Parent
    var parent = new GameObject("KW_Markers").transform;
    parent.SetParent(germanyRoot, true);

    // Unterordner pro Energieträger (on-demand erzeugen)
    var fuelParents = new Dictionary<string, Transform>(StringComparer.InvariantCultureIgnoreCase);
    Transform GetFuelParent(string fuelName)
    {
        fuelName = string.IsNullOrWhiteSpace(fuelName) ? "_Unbekannt" : fuelName.Trim();
        if (!fuelParents.TryGetValue(fuelName, out var t))
        {
            var go = new GameObject(fuelName);
            go.transform.SetParent(parent, true);
            t = go.transform;
            fuelParents[fuelName] = t;
        }
        return t;
    }

    // Deduplizierungsschlüssel
    var placedKeys = new HashSet<string>();
    int created = 0;

    // --- Zeilen verarbeiten ----------------------------------------------
    for (int i = 1; i < lines.Length; i++)
    {
        var parts = lines[i].Split(delim);
        if (parts.Length <= Math.Max(LatIdx, LonIdx)) continue;

        if (!TryParseFloat(parts[LatIdx], out float lat)) continue;
        if (!TryParseFloat(parts[LonIdx], out float lon)) continue;

        // Filter auf Bounding-Box
        if (lat < latRange.x || lat > latRange.y || lon < lonRange.x || lon > lonRange.y) continue;

        // Deduplizieren (gerundete Koordinaten, damit winzige CSV-Rundungsdifferenzen zusammenfallen)
        string key = lat.ToString("F5", CultureInfo.InvariantCulture) + "_" +
                     lon.ToString("F5", CultureInfo.InvariantCulture);
        if (!placedKeys.Add(key)) continue;

        // Weltposition
        var worldPos = GeoToWorld(lat, lon);

        if (useRaycastToSurface && Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out var hit, 100f))
            worldPos = hit.point + Vector3.up * yOffset;
        else
            worldPos.y = worldBounds.center.y + yOffset;

        // Felder lesen
        string name = (NameIdx >= 0 && NameIdx < parts.Length) ? SanitizeName(parts[NameIdx]) : null;
        string fuel = (FuelIdx >= 0 && FuelIdx < parts.Length) ? (parts[FuelIdx]?.Trim() ?? "") : "";
        float power = 0f;
        if (Pidx >= 0 && Pidx < parts.Length) TryParseFloat(parts[Pidx], out power);

        // Container-GameObject
        var containerName = !string.IsNullOrEmpty(name) ? name : $"marker_{created}";
        var container = new GameObject(containerName);
        container.transform.position = worldPos;

        // In Fuel-Unterordner hängen
        var slotParent = GetFuelParent(fuel);
        container.transform.SetParent(slotParent, true);

        // Icon erstellen über ScriptableObject-Factory
        if (modelFactory != null)
        {
            var icon = modelFactory.BuildIcon(fuel, power);
            if (icon != null)
            {
                icon.transform.SetParent(container.transform, false);
                icon.name = "icon";

                    // Physik neutralisieren, damit nichts „hochfliegt“
                    foreach (var rb in icon.GetComponentsInChildren<Rigidbody>(true))
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                        rb.useGravity = false;
                    }

                    // Entweder Collider entfernen ODER als Trigger setzen:
                    foreach (var col in icon.GetComponentsInChildren<Collider>(true))
                    {
                        // Destroy(col); // harte Variante
                        col.isTrigger = true; // sanfte Variante: keine physische Reaktion
                    }

                    // Icon nicht vom eigenen Raycast treffen lassen
                    int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
                    if (ignoreRaycast >= 0)
                    {
                        SetLayerRecursively(icon.transform, ignoreRaycast);
                    }

                    // Hilfsfunktion (innerhalb der Klasse)
                    void SetLayerRecursively(Transform t, int layer)
                    {
                        t.gameObject.layer = layer;
                        for (int i = 0; i < t.childCount; i++)
                            SetLayerRecursively(t.GetChild(i), layer);
                    }
                }
        }
        else
        {
            // Fallback: kleiner Cube (falls Factory nicht gesetzt)
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(fallback.GetComponent<Collider>());
            fallback.transform.SetParent(container.transform, false);
            fallback.transform.localScale = Vector3.one * 0.8f;
            fallback.name = "icon_fallback";
        }

        created++;
    }

    Debug.Log($"Placed {created} unique markers in {fuelParents.Count} fuel groups.");
}




Vector3 GeoToWorld(float lat, float lon)
    {
        float nx = Mathf.InverseLerp(lonRange.x, lonRange.y, lon);
        float nz = Mathf.InverseLerp(latRange.x, latRange.y, lat);

        // Mapping auf die Welt-Bounds des Meshes
        float x = Mathf.Lerp(worldBounds.min.x, worldBounds.max.x, nx);
        float z = Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, invertZ ? 1f - nz : nz);

        return new Vector3(x, worldBounds.max.y, z);
    }

    void Update()
    {
        // wenn Leertaste gedrückt → Place() aufrufen
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Place();
        }
    }
}
