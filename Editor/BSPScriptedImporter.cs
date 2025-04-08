using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using BSPImporter;
using System.Collections.Generic;
using UnityEditor;

[ScriptedImporter(1, "bsp")]
public class BSPScriptedImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        BSPLoader.Settings settings = new BSPLoader.Settings();
        settings.path = ctx.assetPath;
        settings.curveTessellationLevel = 3;
        settings.meshCombineOptions = BSPLoader.MeshCombineOptions.PerEntity;
        settings.scaleFactor = MeshUtils.defaultScale;

        settings.entityCreatedCallback = OnEntityCreated;

        BSPLoader loader = new BSPLoader()
        {
            settings = settings
        };
        loader.LoadBSP(ctx);
    }

    void OnEntityCreated(BSPLoader.EntityInstance instance, List<BSPLoader.EntityInstance> targets)
    {
        Object obj = AssetDatabase.LoadAssetAtPath("Assets/Prefabs/" + instance.entity.ClassName + ".prefab", typeof(GameObject));

        if (obj != null)
        {
            Instantiate(obj as GameObject, instance.gameObject.transform.position, instance.gameObject.transform.rotation, instance.gameObject.transform);
            return;
        }

        if (instance.entity.ClassName == "light")
        {
            instance.gameObject.isStatic = true;
            Light light = instance.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.intensity = instance.entity.GetFloat("intensity");
            light.range = instance.entity.GetFloat("range");
            light.bounceIntensity = 0;
            light.cullingMask = ~LayerMask.GetMask("worldspawn");
            light.shadows = LightShadows.None;
        }
    }
}