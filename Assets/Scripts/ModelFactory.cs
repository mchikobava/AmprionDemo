using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ModelFactory", menuName = "ScriptableObjects/ModelFactory")]
public class ModelFactory : ScriptableObject
{
    [System.Serializable]
    public class FuelPrefab
    {
        public string fuelName;     // z.B. "Erdgas"
        public GameObject prefab;   // dein zugewiesenes Prefab
    }

    public List<FuelPrefab> prefabs;

    public GameObject BuildIcon(string fuel, float powerMw = 0f)
    {
        if (string.IsNullOrEmpty(fuel)) return null;

        string key = fuel.Trim().ToLowerInvariant();

        // Suche passendes Prefab
        foreach (var fp in prefabs)
        {
            if (fp.fuelName.Trim().ToLowerInvariant() == key && fp.prefab != null)
            {
                // Instanz erzeugen
                var go = Instantiate(fp.prefab);
                // Skalierung anhand Leistung anpassen (optional)
                float scale = ComputeScaleFromPower(powerMw);
                go.transform.localScale *= scale;
                return go;
            }
        }

        // Fallback: kleiner Cube falls kein Mapping
        var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(fallback.GetComponent<Collider>());
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
