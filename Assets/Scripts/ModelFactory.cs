using UnityEngine;

[CreateAssetMenu(fileName = "ModelFactory", menuName = "ScriptableObjects/ModelFactory")]
public class ModelFactory : ScriptableObject
{
    [Header("Prefabs für die Brennstoffe")]
    public GameObject AbfallPrefab;
    public GameObject BiogasPrefab;
    public GameObject BraunkohlePrefab;
    public GameObject ErdgasPrefab;
    public GameObject KernenergiePrefab;
    public GameObject KuppelgasPrefab;
    public GameObject MineraloelproduktePrefab;
    public GameObject OelschieferPrefab;
    public GameObject SonstigePrefab;
    public GameObject SteinkohlePrefab;

    public GameObject BuildIcon(string fuel, float powerMw = 0f)
    {
        if (string.IsNullOrEmpty(fuel)) return null;

        GameObject prefab = null;
        switch (fuel)
        {
            case "Abfall": prefab = AbfallPrefab; break;
            case "Biogas": prefab = BiogasPrefab; break;
            case "Braunkohle": prefab = BraunkohlePrefab; break;
            case "Erdgas": prefab = ErdgasPrefab; break;
            case "Kernenergie": prefab = KernenergiePrefab; break;
            case "Kuppelgas": prefab = KuppelgasPrefab; break;
            case "Mineraloelprodukte": prefab = MineraloelproduktePrefab; break;
            case "Oelschiefer": prefab = OelschieferPrefab; break;
            case "Sonstige": prefab = SonstigePrefab; break;
            case "Steinkohle": prefab = SteinkohlePrefab; break;
        }

        if (prefab != null)
        {
            var go = Instantiate(prefab);
            float scale = ComputeScaleFromPower(powerMw);
            go.transform.localScale *= scale;
            go.name = $"icon_{fuel}";
            return go;
        }

        // Fallback: kleiner Cube falls nichts zugewiesen
        var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(fallback.GetComponent<Collider>());
        fallback.name = $"icon_{fuel}";
        fallback.transform.localScale = Vector3.one * ComputeScaleFromPower(powerMw);
        return fallback;
    }

    float ComputeScaleFromPower(float mw)
    {
        if (mw <= 0f) return 0.8f;
        float s = 0.6f + Mathf.Sqrt(mw) * 0.06f;
        return Mathf.Clamp(s, 0.6f, 2.5f);
    }
}