using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class PowerPlantManager : MonoBehaviour
{
    [Header("References")]
    public Transform germanyRoot;           // "germany_texture" Parent
    public TextAsset csv;                   // Power plant data CSV
    public ModelFactory modelFactory;       // Model factory for creating power plant models

    [Header("Geo bounds (Germany)")]
    public Vector2 latRange = new Vector2(47.54935f, 54.80345f);
    public Vector2 lonRange = new Vector2(6.114f, 14.65504f);

    [Header("Placement Settings")]
    public bool useRaycastToSurface = true;
    public float yOffset = 0.02f;
    public bool invertZ = false;

    [Header("Color Settings")]
    public bool useColorCoding = true;
    public Color defaultColor = Color.white;
    public bool colorByUENB = false;      // Color by grid operator (UENB) instead of energy type

    [Header("Visibility Settings")]
    public bool showOnlySpecificType = false;
    public string visibleType = "";

    // Private fields
    private Bounds worldBounds;
    private Transform powerPlantParent;
    private Dictionary<string, List<GameObject>> powerPlantsByType = new Dictionary<string, List<GameObject>>();
    private Dictionary<string, List<GameObject>> powerPlantsByProvider = new Dictionary<string, List<GameObject>>();

    // Energy provider color mapping
    private Dictionary<string, Color> energyColors = new Dictionary<string, Color>
    {
        { "Erdgas", new Color(0.2f, 0.8f, 1.0f) },           // Light blue
        { "Steinkohle", new Color(0.3f, 0.3f, 0.3f) },       // Dark gray
        { "Braunkohle", new Color(0.6f, 0.4f, 0.2f) },       // Brown
        { "Kernenergie", new Color(1.0f, 0.8f, 0.0f) },      // Yellow
        { "Windenergie", new Color(0.8f, 1.0f, 0.8f) },      // Light green
        { "Solarenergie", new Color(1.0f, 0.6f, 0.0f) },     // Orange
        { "Wasserkraft", new Color(0.0f, 0.6f, 1.0f) },      // Blue
        { "Biogas", new Color(0.4f, 0.8f, 0.4f) },           // Green
        { "Mineraloelprodukte", new Color(0.8f, 0.4f, 0.0f) }, // Dark orange
        { "Kuppelgas", new Color(0.6f, 0.6f, 0.8f) },        // Light purple
        { "Abfall", new Color(0.5f, 0.5f, 0.5f) },           // Gray
        { "Sonstige", new Color(0.8f, 0.8f, 0.8f) }          // Light gray
    };

    // UENB (Grid Operator) color mapping
    private Dictionary<string, Color> uenbColors = new Dictionary<string, Color>
    {
        { "TenneT", new Color(1.0f, 0.2f, 0.2f) },          // Red
        { "50Hertz", new Color(0.2f, 0.8f, 0.2f) },         // Green
        { "Amprion", new Color(0.2f, 0.2f, 1.0f) },         // Blue
        { "TransnetBW", new Color(1.0f, 0.8f, 0.2f) },      // Yellow
        { "Unknown", new Color(0.6f, 0.6f, 0.6f) }          // Gray
    };

    void Awake()
    {
        Debug.Log("PowerPlantManager: Starting automatic initialization...");

        // Check references first
        if (germanyRoot == null)
        {
            Debug.LogError("PowerPlantManager: germanyRoot is not assigned!");
            return;
        }

        if (csv == null)
        {
            Debug.LogError("PowerPlantManager: csv is not assigned!");
            return;
        }

        if (modelFactory == null)
        {
            Debug.LogWarning("PowerPlantManager: modelFactory is not assigned - will use fallback cubes");
        }

        Debug.Log("PowerPlantManager: All references are set correctly");

        // Calculate world bounds
        CalculateWorldBounds();

        // Initialize power plant parent
        InitializePowerPlantParent();

        // Load all power plants automatically
        LoadAllPowerPlants();

        // Hide all power plants initially
        HideAllPowerPlants();

        // Debug: Show what was created
        Debug.Log($"PowerPlantManager: Created {powerPlantParent.childCount} power plant containers");
        Debug.Log($"PowerPlantManager: Tracking {powerPlantsByType.Count} energy types: {string.Join(", ", powerPlantsByType.Keys)}");

        Debug.Log("PowerPlantManager: Automatic initialization completed. Use individual keys to show specific power plant types.");
    }

    #region Public Methods

    /// <summary>
    /// Visualizes existing power plant objects that have already been calculated by GIS mapping
    /// </summary>
    public void VisualizeExistingPowerPlants()
    {
        Debug.Log("PowerPlantManager: Starting VisualizeExistingPowerPlants()");

        if (germanyRoot == null)
        {
            Debug.LogError("PowerPlantManager: Please set germanyRoot reference.");
            return;
        }

        Debug.Log($"PowerPlantManager: germanyRoot found: {germanyRoot.name}");

        // Find existing KW_Markers parent (created by GIS mapping)
        var existingMarkersParent = germanyRoot.Find("KW_Markers");
        if (existingMarkersParent == null)
        {
            Debug.LogWarning("PowerPlantManager: No existing KW_Markers found. Please run GIS mapping first.");
            Debug.Log("PowerPlantManager: Available children under germanyRoot:");
            for (int i = 0; i < germanyRoot.childCount; i++)
            {
                Debug.Log($"  - {germanyRoot.GetChild(i).name}");
            }
            return;
        }

        Debug.Log($"PowerPlantManager: Found KW_Markers with {existingMarkersParent.childCount} fuel groups");

        // Log all fuel groups found
        for (int i = 0; i < existingMarkersParent.childCount; i++)
        {
            var fuelGroup = existingMarkersParent.GetChild(i);
            Debug.Log($"PowerPlantManager: Fuel group {i}: '{fuelGroup.name}' with {fuelGroup.childCount} power plants");
        }

        // Initialize dictionaries (cleanup already done in Update function)
        powerPlantsByType.Clear();
        powerPlantsByProvider.Clear();

        int processed = 0;

        // Process all existing markers
        ProcessExistingMarkers(existingMarkersParent, ref processed);

        Debug.Log($"PowerPlantManager: Visualized {processed} existing power plants from GIS mapping.");
    }

    /// <summary>
    /// Loads all power plant models from CSV data and places them on the map
    /// </summary>
    public void LoadAllPowerPlants()
    {
        if (csv == null || germanyRoot == null)
        {
            Debug.LogError("PowerPlantManager: Please set references (germanyRoot, csv).");
            return;
        }

        var lines = csv.text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            Debug.LogWarning("PowerPlantManager: CSV is empty or contains only header row.");
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
        int UENBIdx = FindColumnIndex(header, "UENB", "GRID_OPERATOR", "TSO");

        if (LatIdx < 0 || LonIdx < 0)
        {
            Debug.LogError("PowerPlantManager: CSV columns for latitude/longitude not found.");
            return;
        }

        // Initialize dictionaries
        powerPlantsByType.Clear();
        powerPlantsByProvider.Clear();

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

            // Validate position
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

            // Create power plant
            CreatePowerPlant(worldPos, parts, LatIdx, LonIdx, NameIdx, FuelIdx, Pidx, UENBIdx, ref created);
        }

        Debug.Log($"PowerPlantManager: Loaded {created} power plants, skipped {skipped} invalid positions.");
    }

    /// <summary>
    /// Colors all power plants of a specific type
    /// </summary>
    public void ColorPowerPlantsByType(string energyType, Color color)
    {
        if (string.IsNullOrEmpty(energyType)) return;

        string normalizedType = energyType.Trim();
        if (powerPlantsByType.TryGetValue(normalizedType, out var plants))
        {
            foreach (var plant in plants)
            {
                ApplyColorToModel(plant, color);
            }
            Debug.Log($"PowerPlantManager: Colored {plants.Count} {normalizedType} power plants.");
        }
        else
        {
            Debug.LogWarning($"PowerPlantManager: No power plants found for type '{normalizedType}'.");
        }
    }

    /// <summary>
    /// Colors all power plants by energy provider (UENB)
    /// </summary>
    public void ColorPowerPlantsByProvider(string provider, Color color)
    {
        if (string.IsNullOrEmpty(provider)) return;

        string normalizedProvider = provider.Trim();
        if (powerPlantsByProvider.TryGetValue(normalizedProvider, out var plants))
        {
            foreach (var plant in plants)
            {
                ApplyColorToModel(plant, color);
            }
            Debug.Log($"PowerPlantManager: Colored {plants.Count} power plants for provider '{normalizedProvider}'.");
        }
        else
        {
            Debug.LogWarning($"PowerPlantManager: No power plants found for provider '{normalizedProvider}'.");
        }
    }

    /// <summary>
    /// Colors Erdgas power plants
    /// </summary>
    public void ColorErdgasPowerPlants(Color color)
    {
        ColorPowerPlantsByType("Erdgas", color);
    }

    /// <summary>
    /// Colors Steinkohle power plants
    /// </summary>
    public void ColorSteinkohlePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Steinkohle", color);
    }

    /// <summary>
    /// Colors Braunkohle power plants
    /// </summary>
    public void ColorBraunkohlePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Braunkohle", color);
    }

    /// <summary>
    /// Colors Kernenergie power plants
    /// </summary>
    public void ColorKernenergiePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Kernenergie", color);
    }

    /// <summary>
    /// Colors Windenergie power plants
    /// </summary>
    public void ColorWindenergiePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Windenergie", color);
    }

    /// <summary>
    /// Colors Solarenergie power plants
    /// </summary>
    public void ColorSolarenergiePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Solarenergie", color);
    }

    /// <summary>
    /// Colors Wasserkraft power plants
    /// </summary>
    public void ColorWasserkraftPowerPlants(Color color)
    {
        ColorPowerPlantsByType("Wasserkraft", color);
    }

    /// <summary>
    /// Colors Biogas power plants
    /// </summary>
    public void ColorBiogasPowerPlants(Color color)
    {
        ColorPowerPlantsByType("Biogas", color);
    }

    /// <summary>
    /// Colors Mineraloelprodukte power plants
    /// </summary>
    public void ColorMineraloelproduktePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Mineraloelprodukte", color);
    }

    /// <summary>
    /// Colors Kuppelgas power plants
    /// </summary>
    public void ColorKuppelgasPowerPlants(Color color)
    {
        ColorPowerPlantsByType("Kuppelgas", color);
    }

    /// <summary>
    /// Colors Abfall power plants
    /// </summary>
    public void ColorAbfallPowerPlants(Color color)
    {
        ColorPowerPlantsByType("Abfall", color);
    }

    /// <summary>
    /// Colors Sonstige power plants
    /// </summary>
    public void ColorSonstigePowerPlants(Color color)
    {
        ColorPowerPlantsByType("Sonstige", color);
    }

    /// <summary>
    /// Shows only Erdgas power plants
    /// </summary>
    public void ShowOnlyErdgasPowerPlants()
    {
        ShowOnlyPowerPlantType("Erdgas");
    }

    /// <summary>
    /// Shows only Kernenergie power plants
    /// </summary>
    public void ShowOnlyKernenergiePowerPlants()
    {
        ShowOnlyPowerPlantType("Kernenergie");
    }

    /// <summary>
    /// Shows only Abfall power plants
    /// </summary>
    public void ShowOnlyAbfallPowerPlants()
    {
        ShowOnlyPowerPlantType("Abfall");
    }

    /// <summary>
    /// Shows only Steinkohle power plants
    /// </summary>
    public void ShowOnlySteinkohlePowerPlants()
    {
        ShowOnlyPowerPlantType("Steinkohle");
    }

    /// <summary>
    /// Shows only Braunkohle power plants
    /// </summary>
    public void ShowOnlyBraunkohlePowerPlants()
    {
        ShowOnlyPowerPlantType("Braunkohle");
    }

    /// <summary>
    /// Shows only Windenergie power plants
    /// </summary>
    public void ShowOnlyWindenergiePowerPlants()
    {
        ShowOnlyPowerPlantType("Windenergie");
    }

    /// <summary>
    /// Shows only Solarenergie power plants
    /// </summary>
    public void ShowOnlySolarenergiePowerPlants()
    {
        ShowOnlyPowerPlantType("Solarenergie");
    }

    /// <summary>
    /// Shows only Wasserkraft power plants
    /// </summary>
    public void ShowOnlyWasserkraftPowerPlants()
    {
        ShowOnlyPowerPlantType("Wasserkraft");
    }

    /// <summary>
    /// Shows only Biogas power plants
    /// </summary>
    public void ShowOnlyBiogasPowerPlants()
    {
        ShowOnlyPowerPlantType("Biogas");
    }

    /// <summary>
    /// Shows only Mineraloelprodukte power plants
    /// </summary>
    public void ShowOnlyMineraloelproduktePowerPlants()
    {
        ShowOnlyPowerPlantType("Mineraloelprodukte");
    }

    /// <summary>
    /// Shows only Kuppelgas power plants
    /// </summary>
    public void ShowOnlyKuppelgasPowerPlants()
    {
        ShowOnlyPowerPlantType("Kuppelgas");
    }

    /// <summary>
    /// Shows only Sonstige power plants
    /// </summary>
    public void ShowOnlySonstigePowerPlants()
    {
        ShowOnlyPowerPlantType("Sonstige");
    }

    /// <summary>
    /// Shows only power plants of a specific type
    /// </summary>
    public void ShowOnlyPowerPlantType(string energyType)
    {
        if (string.IsNullOrEmpty(energyType))
        {
            ShowAllPowerPlants();
            return;
        }

        showOnlySpecificType = true;
        visibleType = energyType.Trim();

        Debug.Log($"PowerPlantManager: Attempting to show {energyType} power plants...");

        // Hide all power plants first
        HideAllPowerPlants();

        // Show only the specified type
        if (powerPlantsByType.TryGetValue(visibleType, out var plants))
        {
            Debug.Log($"PowerPlantManager: Found {plants.Count} {visibleType} power plants in tracking dictionary");

            int shownCount = 0;
            foreach (var plant in plants)
            {
                if (plant != null)
                {
                    SetPowerPlantVisibility(plant, true);
                    shownCount++;
                    Debug.Log($"PowerPlantManager: Made visible: {plant.name}");
                }
                else
                {
                    Debug.LogWarning("PowerPlantManager: Found null plant in tracking dictionary");
                }
            }
            Debug.Log($"PowerPlantManager: Successfully showed {shownCount} {visibleType} power plants.");
        }
        else
        {
            Debug.LogWarning($"PowerPlantManager: No power plants found for type '{visibleType}'.");
            Debug.Log($"PowerPlantManager: Available types: {string.Join(", ", powerPlantsByType.Keys)}");
        }
    }

    /// <summary>
    /// Shows all power plants
    /// </summary>
    public void ShowAllPowerPlants()
    {
        showOnlySpecificType = false;
        visibleType = "";

        if (powerPlantParent == null)
        {
            Debug.LogWarning("PowerPlantManager: No powerPlantParent found. Cannot show power plants.");
            return;
        }

        Debug.Log($"PowerPlantManager: Showing all {powerPlantParent.childCount} power plant containers");

        // Show all power plant containers
        for (int i = 0; i < powerPlantParent.childCount; i++)
        {
            var container = powerPlantParent.GetChild(i);
            SetPowerPlantVisibility(container.gameObject, true);
        }

        Debug.Log($"PowerPlantManager: Showing all {powerPlantParent.childCount} power plants.");
    }

    /// <summary>
    /// Hides all power plants
    /// </summary>
    public void HideAllPowerPlants()
    {
        if (powerPlantParent == null) return;

        var allModels = powerPlantParent.GetComponentsInChildren<Renderer>();
        foreach (var renderer in allModels)
        {
            SetPowerPlantVisibility(renderer.gameObject, false);
        }
    }

    /// <summary>
    /// Colors all power plants using predefined energy type colors
    /// </summary>
    public void ApplyDefaultEnergyTypeColors()
    {
        Debug.Log($"PowerPlantManager: Applying default energy type colors to {powerPlantsByType.Count} energy types");

        foreach (var kvp in energyColors)
        {
            ColorPowerPlantsByType(kvp.Key, kvp.Value);
        }
        Debug.Log("PowerPlantManager: Applied default energy type colors to all power plants.");
    }

    /// <summary>
    /// Colors all power plants using predefined UENB provider colors
    /// </summary>
    public void ApplyDefaultProviderColors()
    {
        foreach (var kvp in uenbColors)
        {
            ColorPowerPlantsByProvider(kvp.Key, kvp.Value);
        }
        Debug.Log("PowerPlantManager: Applied default provider colors to all power plants.");
    }

    /// <summary>
    /// Removes all power plant models from the scene
    /// </summary>
    public void CleanupPowerPlants()
    {
        Debug.Log("PowerPlantManager: Starting cleanup of all power plants");

        if (powerPlantParent != null)
        {
            int childCount = powerPlantParent.childCount;
            Debug.Log($"PowerPlantManager: Destroying {childCount} power plant containers");

            // Disable all colliders first to prevent Oculus Interaction issues
            var allColliders = powerPlantParent.GetComponentsInChildren<Collider>();
            foreach (var collider in allColliders)
            {
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }

            // Disable all renderers
            var allRenderers = powerPlantParent.GetComponentsInChildren<Renderer>();
            foreach (var renderer in allRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            // Wait a frame before destroying to let Oculus Interaction system clean up
            StartCoroutine(DestroyAfterFrame());
        }
        else
        {
            Debug.Log("PowerPlantManager: No powerPlantParent found to cleanup");
            // Clear tracking dictionaries even if no parent exists
            ClearTrackingDictionaries();
            InitializePowerPlantParent();
        }
    }

    System.Collections.IEnumerator DestroyAfterFrame()
    {
        yield return null; // Wait one frame

        if (powerPlantParent != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(powerPlantParent.gameObject);
#else
            Destroy(powerPlantParent.gameObject);
#endif
        }

        // Clear tracking dictionaries
        ClearTrackingDictionaries();

        // Reinitialize
        InitializePowerPlantParent();
        Debug.Log("PowerPlantManager: Cleanup completed and new parent initialized.");
    }

    void ClearTrackingDictionaries()
    {
        int typeCount = powerPlantsByType.Count;
        int providerCount = powerPlantsByProvider.Count;
        powerPlantsByType.Clear();
        powerPlantsByProvider.Clear();
        Debug.Log($"PowerPlantManager: Cleared {typeCount} energy types and {providerCount} providers from tracking");
    }

    /// <summary>
    /// Gets the count of power plants by type
    /// </summary>
    public int GetPowerPlantCountByType(string energyType)
    {
        if (string.IsNullOrEmpty(energyType)) return 0;
        return powerPlantsByType.TryGetValue(energyType.Trim(), out var plants) ? plants.Count : 0;
    }

    /// <summary>
    /// Gets the count of power plants by provider
    /// </summary>
    public int GetPowerPlantCountByProvider(string provider)
    {
        if (string.IsNullOrEmpty(provider)) return 0;
        return powerPlantsByProvider.TryGetValue(provider.Trim(), out var plants) ? plants.Count : 0;
    }

    /// <summary>
    /// Gets all available energy types
    /// </summary>
    public string[] GetAvailableEnergyTypes()
    {
        return powerPlantsByType.Keys.ToArray();
    }

    /// <summary>
    /// Gets all available energy providers
    /// </summary>
    public string[] GetAvailableProviders()
    {
        return powerPlantsByProvider.Keys.ToArray();
    }

    #endregion

    #region Private Methods

    // Removed CalculateWorldBounds - not needed since we work with existing GIS mapping objects

    void InitializePowerPlantParent()
    {
        powerPlantParent = new GameObject("PowerPlants").transform;
        powerPlantParent.SetParent(germanyRoot, true);
    }

    // Removed CreatePowerPlant method - functionality moved to ProcessExistingPowerPlant

    void ProcessExistingMarkers(Transform markersParent, ref int processed)
    {
        Debug.Log($"PowerPlantManager: Processing {markersParent.childCount} fuel groups");

        // Process each fuel type group
        for (int i = 0; i < markersParent.childCount; i++)
        {
            var fuelGroup = markersParent.GetChild(i);
            string fuelType = fuelGroup.name;

            Debug.Log($"PowerPlantManager: Processing fuel group '{fuelType}' with {fuelGroup.childCount} power plants");

            // Process each power plant in this fuel group
            for (int j = 0; j < fuelGroup.childCount; j++)
            {
                var powerPlantContainer = fuelGroup.GetChild(j);
                Debug.Log($"PowerPlantManager: Processing power plant {j}: '{powerPlantContainer.name}'");
                ProcessExistingPowerPlant(powerPlantContainer, fuelType, ref processed);
            }
        }

        Debug.Log($"PowerPlantManager: Finished processing markers. Total processed: {processed}");
    }

    void ProcessExistingPowerPlant(Transform container, string fuelType, ref int processed)
    {
        Debug.Log($"PowerPlantManager: Processing container '{container.name}' with {container.childCount} children");

        // Log all children to see what's available
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            Debug.Log($"PowerPlantManager: Child {i}: '{child.name}'");
        }

        // Find the icon/model child
        Transform iconTransform = null;
        for (int i = 0; i < container.childCount; i++)
        {
            var child = container.GetChild(i);
            if (child.name == "icon" || child.name == "icon_fallback")
            {
                iconTransform = child;
                Debug.Log($"PowerPlantManager: Found icon: '{child.name}'");
                break;
            }
        }

        if (iconTransform == null)
        {
            Debug.LogWarning($"PowerPlantManager: No icon found in container '{container.name}'. Available children:");
            for (int i = 0; i < container.childCount; i++)
            {
                Debug.LogWarning($"  - {container.GetChild(i).name}");
            }
            return;
        }

        // Create a copy for our visualization
        var visualizationContainer = new GameObject($"PowerPlant_{processed}");
        visualizationContainer.transform.position = container.position;
        visualizationContainer.transform.SetParent(powerPlantParent, true);

        // Copy the icon/model
        var modelCopy = Instantiate(iconTransform.gameObject);
        modelCopy.transform.SetParent(visualizationContainer.transform, false);
        modelCopy.name = "model";

        // Disable colliders to prevent Oculus Interaction issues
        var colliders = modelCopy.GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        // Remove any Oculus Interaction components that might cause issues
        var interactionComponents = modelCopy.GetComponentsInChildren<MonoBehaviour>();
        foreach (var component in interactionComponents)
        {
            if (component != null && (component.GetType().Name.Contains("Interactable") ||
                                     component.GetType().Name.Contains("Grabbable") ||
                                     component.GetType().Name.Contains("Grab")))
            {
                DestroyImmediate(component);
            }
        }

        // Extract UENB information from container name or parent hierarchy
        string uenb = ExtractUENBFromContainer(container);

        // Add to tracking dictionaries
        AddToTrackingDictionaries(modelCopy, fuelType, uenb);

        Debug.Log($"PowerPlantManager: Successfully processed power plant {processed}: '{container.name}' -> '{fuelType}'");
        processed++;
    }

    string ExtractUENBFromContainer(Transform container)
    {
        // Try to extract UENB information from the container hierarchy
        // This is a simplified approach - you might need to adjust based on your actual data structure

        // Check if container name contains UENB info
        string containerName = container.name.ToUpperInvariant();
        if (containerName.Contains("TENNET")) return "TenneT";
        if (containerName.Contains("50HERTZ")) return "50Hertz";
        if (containerName.Contains("AMPRION")) return "Amprion";
        if (containerName.Contains("TRANSNETBW")) return "TransnetBW";

        // Check parent fuel group for additional context
        if (container.parent != null)
        {
            string parentName = container.parent.name.ToUpperInvariant();
            if (parentName.Contains("TENNET")) return "TenneT";
            if (parentName.Contains("50HERTZ")) return "50Hertz";
            if (parentName.Contains("AMPRION")) return "Amprion";
            if (parentName.Contains("TRANSNETBW")) return "TransnetBW";
        }

        return "Unknown";
    }

    void AddToTrackingDictionaries(GameObject model, string fuel, string uenb)
    {
        // Add to energy type tracking
        if (!string.IsNullOrEmpty(fuel))
        {
            string normalizedFuel = fuel.Trim();
            if (!powerPlantsByType.ContainsKey(normalizedFuel))
            {
                powerPlantsByType[normalizedFuel] = new List<GameObject>();
            }
            powerPlantsByType[normalizedFuel].Add(model);
        }

        // Add to provider tracking
        if (!string.IsNullOrEmpty(uenb))
        {
            string normalizedProvider = uenb.Trim();
            if (!powerPlantsByProvider.ContainsKey(normalizedProvider))
            {
                powerPlantsByProvider[normalizedProvider] = new List<GameObject>();
            }
            powerPlantsByProvider[normalizedProvider].Add(model);
        }
    }

    void ApplyColorCoding(GameObject obj, string fuelType, string uenb)
    {
        if (!useColorCoding) return;

        Color color;
        if (colorByUENB)
        {
            color = GetUENBColor(uenb);
        }
        else
        {
            color = GetEnergyProviderColor(fuelType);
        }

        ApplyColorToModel(obj, color);
    }

    void ApplyColorToModel(GameObject obj, Color color)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            var material = new Material(renderer.material);
            material.color = color;
            renderer.material = material;
        }
    }

    Color GetEnergyProviderColor(string fuelType)
    {
        if (!useColorCoding || string.IsNullOrEmpty(fuelType))
            return defaultColor;

        if (energyColors.TryGetValue(fuelType, out Color color))
            return color;

        var key = energyColors.Keys.FirstOrDefault(k =>
            string.Equals(k, fuelType, StringComparison.OrdinalIgnoreCase));

        return key != null ? energyColors[key] : defaultColor;
    }

    Color GetUENBColor(string uenb)
    {
        if (!useColorCoding || string.IsNullOrEmpty(uenb))
            return defaultColor;

        if (uenbColors.TryGetValue(uenb, out Color color))
            return color;

        var key = uenbColors.Keys.FirstOrDefault(k =>
            string.Equals(k, uenb, StringComparison.OrdinalIgnoreCase));

        return key != null ? uenbColors[key] : defaultColor;
    }

    void CalculateWorldBounds()
    {
        var rends = germanyRoot.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0)
        {
            Debug.LogError("PowerPlantManager: No Renderer found under germanyRoot.");
            return;
        }

        worldBounds = rends[0].bounds;
        foreach (var r in rends) worldBounds.Encapsulate(r.bounds);

        Debug.Log($"PowerPlantManager: Germany model bounds: min=({worldBounds.min.x:F2}, {worldBounds.min.z:F2}), max=({worldBounds.max.x:F2}, {worldBounds.max.z:F2})");
    }

    void CreatePowerPlant(Vector3 worldPos, string[] parts, int LatIdx, int LonIdx, int NameIdx, int FuelIdx, int Pidx, int UENBIdx, ref int created)
    {
        // Read data
        string name = (NameIdx >= 0 && NameIdx < parts.Length) ? SanitizeName(parts[NameIdx]) : null;
        string fuel = (FuelIdx >= 0 && FuelIdx < parts.Length) ? (parts[FuelIdx]?.Trim() ?? "") : "";
        string uenb = (UENBIdx >= 0 && UENBIdx < parts.Length) ? (parts[UENBIdx]?.Trim() ?? "") : "";
        float power = 0f;
        if (Pidx >= 0 && Pidx < parts.Length) TryParseFloat(parts[Pidx], out power);

        // Create container
        var containerName = !string.IsNullOrEmpty(name) ? name : $"powerplant_{created}";
        var container = new GameObject(containerName);
        container.transform.position = worldPos;
        container.transform.SetParent(powerPlantParent, true);

        // Create model
        GameObject model = null;
        if (modelFactory != null)
        {
            model = modelFactory.BuildIcon(fuel, power);
            if (model != null)
            {
                model.transform.SetParent(container.transform, false);
                model.name = "model";

                // Scale model appropriately
                float powerScale = CalculatePowerScale(power);
                FitModelToMap(model, powerScale);

                // Apply default color coding
                if (useColorCoding)
                {
                    ApplyColorCoding(model, fuel, uenb);
                }

                // Disable physics and interaction
                DisablePhysics(model);
                DisableInteraction(model);

                // Set layer to avoid raycast interference
                SetLayerRecursively(model.transform, LayerMask.NameToLayer("Ignore Raycast"));
            }
        }
        else
        {
            // Fallback cube
            model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(model.GetComponent<Collider>());
            model.transform.SetParent(container.transform, false);
            model.transform.localScale = Vector3.one * 0.8f;
            model.name = "model_fallback";

            // Apply color coding to fallback
            if (useColorCoding)
            {
                ApplyColorCoding(model, fuel, uenb);
            }
        }

        // Add to tracking dictionaries
        if (model != null)
        {
            AddToTrackingDictionaries(model, fuel, uenb);
        }

        created++;
    }

    bool IsValidPosition(Vector3 worldPos, float lat, float lon)
    {
        return worldBounds.Contains(new Vector3(worldPos.x, worldBounds.center.y, worldPos.z));
    }

    bool PlaceOnSurface(ref Vector3 worldPos)
    {
        if (!useRaycastToSurface)
        {
            worldPos.y = worldBounds.center.y + yOffset;
            return true;
        }

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

    float CalculatePowerScale(float power)
    {
        return (power <= 0f) ? 0.8f : Mathf.Clamp(0.6f + Mathf.Sqrt(power) * 0.06f, 0.6f, 2.5f);
    }

    void FitModelToMap(GameObject model, float powerScale = 1f)
    {
        if (model == null) return;

        var rends = model.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) return;

        var modelBounds = rends[0].bounds;
        foreach (var r in rends) modelBounds.Encapsulate(r.bounds);

        float modelHeight = Mathf.Max(0.0001f, modelBounds.size.y);
        Vector2 xz = new Vector2(worldBounds.size.x, worldBounds.size.z);
        float mapDiagXZ = xz.magnitude;
        float targetHeight = mapDiagXZ * 0.01f * powerScale;
        float factor = Mathf.Clamp(targetHeight / modelHeight, 0.05f, 50f);

        model.transform.localScale *= factor;
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

    void DisableInteraction(GameObject obj)
    {
        // Remove any Oculus Interaction components
        var interactionComponents = obj.GetComponentsInChildren<MonoBehaviour>();
        foreach (var component in interactionComponents)
        {
            if (component != null && (component.GetType().Name.Contains("Interactable") ||
                                     component.GetType().Name.Contains("Grabbable") ||
                                     component.GetType().Name.Contains("Grab")))
            {
                DestroyImmediate(component);
            }
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

    Vector3 GeoToWorld(float lat, float lon)
    {
        float nx = Mathf.InverseLerp(lonRange.x, lonRange.y, lon);
        float nz = Mathf.InverseLerp(latRange.x, latRange.y, lat);

        float x = Mathf.Lerp(worldBounds.min.x, worldBounds.max.x, nx);
        float z = Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, invertZ ? 1f - nz : nz);

        return new Vector3(x, worldBounds.max.y, z);
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

    #endregion

    #region Update and Input Handling

    void Update()
    {
        // Individual power plant type keys
        if ((Keyboard.current != null && Keyboard.current.numpad1Key.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.One))
        {
            Debug.Log("PowerPlantManager: 0 key pressed - showing garbage (Abfall) power plants");
            ShowOnlyAbfallPowerPlants();
        }

        if ((Keyboard.current != null && Keyboard.current.numpad2Key.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.Two))
        {
            Debug.Log("PowerPlantManager: 1 key pressed - showing gas (Erdgas) power plants");
            ShowOnlyErdgasPowerPlants();
        }

        if ((Keyboard.current != null && Keyboard.current.numpad3Key.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.Three))
        {
            Debug.Log("PowerPlantManager: 2 key pressed - showing hard coal (Steinkohle) power plants");
            ShowOnlySteinkohlePowerPlants();
        }

        if ((Keyboard.current != null && Keyboard.current.numpad4Key.wasPressedThisFrame))
        {
            Debug.Log("PowerPlantManager: 3 key pressed - showing nuclear (Kernenergie) power plants");
            ShowOnlyKernenergiePowerPlants();
        }

        if (Keyboard.current != null && Keyboard.current.numpad5Key.wasPressedThisFrame
             || OVRInput.GetDown(OVRInput.Button.Four))
        {
            Debug.Log("PowerPlantManager: 4 key pressed - showing oil (Mineraloelprodukte) power plants");
            ShowOnlyMineraloelproduktePowerPlants();
        }

        if ((Keyboard.current != null && Keyboard.current.numpad5Key.wasPressedThisFrame))
        {
            Debug.Log("PowerPlantManager: 5 key pressed - showing hydro (Wasserkraft) power plants");
            ShowOnlyWasserkraftPowerPlants();
        }



        // Show all power plants on Space key
        if ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
        {
            Debug.Log("PowerPlantManager: Space key pressed - showing all power plants");
            ShowAllPowerPlants();
        }

        // Hide all power plants on C key
        if ((Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
        {
            Debug.Log("PowerPlantManager: C key pressed - hiding all power plants");
            HideAllPowerPlants();
        }

        if ((Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame) ||
            OVRInput.GetDown(OVRInput.Button.SecondaryHandTrigger))
        {
            Debug.Log("PowerPlantManager: p pressed - color based on UENB");
            ColorPowerPlantsByProvider("TenneT", new Color(1.0f, 0.2f, 0.2f));
            ColorPowerPlantsByProvider("50Hertz", new Color(0.2f, 0.8f, 0.2f));
            ColorPowerPlantsByProvider("Amprion", new Color(0.2f, 0.2f, 1.0f));
            ColorPowerPlantsByProvider("TransnetBW", new Color(1.0f, 0.8f, 0.2f));
            ColorPowerPlantsByProvider("Unknown", new Color(0.6f, 0.6f, 0.6f));

        }
    }

    /// <summary>
    /// Test method to debug visibility issues
    /// </summary>
    public void TestVisibility()
    {
        if (powerPlantParent == null)
        {
            Debug.LogError("PowerPlantManager: powerPlantParent is null!");
            return;
        }

        Debug.Log($"PowerPlantManager: Testing visibility - Found {powerPlantParent.childCount} power plant containers");

        for (int i = 0; i < Mathf.Min(5, powerPlantParent.childCount); i++) // Test first 5
        {
            var container = powerPlantParent.GetChild(i);
            Debug.Log($"PowerPlantManager: Container {i}: '{container.name}' - Active: {container.gameObject.activeInHierarchy}");

            // Check if it has a model child
            for (int j = 0; j < container.childCount; j++)
            {
                var child = container.GetChild(j);
                Debug.Log($"PowerPlantManager:   Child {j}: '{child.name}' - Active: {child.gameObject.activeInHierarchy}");

                // Check renderer on this child
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Debug.Log($"PowerPlantManager:     Renderer on '{child.name}' enabled: {renderer.enabled}, visible: {renderer.isVisible}");
                }

                // Check renderers in children of this child (like "default")
                var childRenderers = child.GetComponentsInChildren<Renderer>();
                foreach (var childRenderer in childRenderers)
                {
                    if (childRenderer != renderer) // Don't duplicate the above
                    {
                        Debug.Log($"PowerPlantManager:     Child renderer on '{childRenderer.name}' enabled: {childRenderer.enabled}, visible: {childRenderer.isVisible}");
                    }
                }
            }
        }

        // Force show first few power plants
        Debug.Log("PowerPlantManager: Force showing first 3 power plants...");
        for (int i = 0; i < Mathf.Min(3, powerPlantParent.childCount); i++)
        {
            var container = powerPlantParent.GetChild(i);
            container.gameObject.SetActive(true);

            for (int j = 0; j < container.childCount; j++)
            {
                var child = container.GetChild(j);
                child.gameObject.SetActive(true);

                var renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
        }
    }

    // Removed old methods - now using individual keys for each power plant type

    /// <summary>
    /// Applies default colors to all power plants (no special coloring)
    /// </summary>
    public void ApplyDefaultColors()
    {
        if (powerPlantParent == null) return;

        var allModels = powerPlantParent.GetComponentsInChildren<Renderer>();
        foreach (var renderer in allModels)
        {
            var material = new Material(renderer.material);
            material.color = defaultColor;
            renderer.material = material;
        }
        Debug.Log("PowerPlantManager: Applied default colors to all power plants.");
    }

    /// <summary>
    /// Sets the visibility of a power plant model
    /// </summary>
    void SetPowerPlantVisibility(GameObject powerPlant, bool visible)
    {
        if (powerPlant == null)
        {
            Debug.LogWarning("PowerPlantManager: SetPowerPlantVisibility called with null GameObject");
            return;
        }

        Debug.Log($"PowerPlantManager: Setting visibility of '{powerPlant.transform.parent.gameObject.name}' to {visible}");

        // Set the main GameObject and all its children active/inactive
        powerPlant.SetActive(visible);
        // set all children active
        SetChildrenActiveRecursive(powerPlant.transform, visible);


        // Find all renderers in children (including nested children)
        var renderers = powerPlant.GetComponentsInChildren<Renderer>(true); // Include inactive objects
        Debug.Log($"PowerPlantManager: Found {renderers.Length} renderers in '{powerPlant.name}' and its children");

        foreach (var renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
                Debug.Log($"PowerPlantManager: Set renderer '{renderer.name}' enabled to {visible}");
            }
        }

        // Set all child GameObjects active/inactive recursively
        SetChildrenActiveRecursive(powerPlant.transform, visible);
    }

    void SetChildrenActiveRecursive(Transform parent, bool active)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != null)
            {
                child.gameObject.SetActive(active);
                Debug.Log($"PowerPlantManager: Set child '{child.name}' active to {active}");

                // Recursively set children of children
                SetChildrenActiveRecursive(child, active);
            }
        }
    }

    #endregion
}
