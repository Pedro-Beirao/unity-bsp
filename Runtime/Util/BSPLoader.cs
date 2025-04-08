using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor.AssetImporters;
using UnityEditor;
#endif
using LibBSP;

namespace BSPImporter
{
#if UNITY_5_6_OR_NEWER
    using Vertex = UIVertex;
#endif

    public class BSPLoader
    {
        public enum MeshCombineOptions
        {
            None,
            PerMaterial,
            PerEntity,
        }

        /// <summary>
        /// Struct containing various settings for the BSP Import process.
        /// </summary>
        [Serializable]
        public struct Settings
        {
            public string path;
            public MeshCombineOptions meshCombineOptions;
            public int curveTessellationLevel;
            public Action<EntityInstance, List<EntityInstance>> entityCreatedCallback;
            public float scaleFactor;
        }

        public struct EntityInstance
        {
            public Entity entity;
            public GameObject gameObject;
        }

        public static bool IsRuntime
        {
            get
            {
#if UNITY_EDITOR
                return EditorApplication.isPlaying;
#else
                return true;
#endif
            }
        }
#if UNITY_EDITOR

        public Settings settings;

        private BSP bsp;
        private GameObject root;
        private List<EntityInstance> entityInstances = new List<EntityInstance>();
        private Dictionary<string, List<EntityInstance>> namedEntities = new Dictionary<string, List<EntityInstance>>();
        private Dictionary<string, Material> materialDirectory = new Dictionary<string, Material>();
        private Texture2D lightmapAtlas;
        private int lightmapIndex = 0;
        private Color[] palette = new Color[256];
        private AssetImportContext ctx;
        private bool savePrefab = false;
        private List<Vector3> lightProbesPositions = new List<Vector3>();
        private List<SphericalHarmonicsL2> lightProbesValues = new List<SphericalHarmonicsL2>();


        public GameObject LoadBSP(AssetImportContext ctx)
        {
            if (string.IsNullOrEmpty(settings.path) || !File.Exists(settings.path))
            {
                Debug.LogError("Cannot import " + settings.path + ": The path is invalid.");
                return null;
            }

            BSP bsp = new BSP(new FileInfo(settings.path));

            if (bsp == null)
            {
                Debug.LogError("Cannot import BSP: The object was null.");
                return null;
            }
            this.bsp = bsp;

            if (ctx != null)
            {
                this.ctx = ctx;
                savePrefab = true;
            }

            LoadPalette("Assets/qpalette.png");

            lightmapAtlas = new Texture2D(MeshUtils.lightmapAtlasSize, MeshUtils.lightmapAtlasSize);
            for (int i = 0; i < MeshUtils.lightmapAtlasSize; i++)
            {
                for (int j = 0; j < MeshUtils.lightmapAtlasSize; j++)
                {
                    lightmapAtlas.SetPixel(j, i, Color.black);
                }
            }
            //lightmapAtlas.filterMode = FilterMode.Point;
            lightmapAtlas.Apply();

            for (int i = 0; i < bsp.Entities.Count; ++i)
            {
                Entity entity = bsp.Entities[i];

                EntityInstance instance = CreateEntityInstance(entity);
                entityInstances.Add(instance);
                namedEntities[entity.Name].Add(instance);

                int modelNumber = entity.ModelNumber;
                if (modelNumber >= 0)
                {
                    BuildMesh(instance);
                }
                else
                {
                    Vector3 angles = entity.Angles;
                    instance.gameObject.transform.rotation = Quaternion.Euler(-angles.x, angles.y, angles.z);
                }

                instance.gameObject.transform.position = entity.Origin.SwizzleYZ() * settings.scaleFactor;

                if (instance.entity.ClassName == "worldspawn")
                {
                    instance.gameObject.layer = LayerMask.NameToLayer("worldspawn");
                    GameObjectUtility.SetStaticEditorFlags(instance.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                    MeshRenderer meshRenderer = instance.gameObject.GetComponent<MeshRenderer>();
                    meshRenderer.staticShadowCaster = true;
                    meshRenderer.renderingLayerMask = 2;
                    meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                }
            }

            root = new GameObject(bsp.MapName);
            foreach (KeyValuePair<string, List<EntityInstance>> pair in namedEntities)
            {
                SetUpEntityHierarchy(pair.Value);
            }

            if (settings.entityCreatedCallback != null)
            {
                foreach (EntityInstance instance in entityInstances)
                {
                    string target = instance.entity["target"];
                    if (namedEntities.ContainsKey(target) && !string.IsNullOrEmpty(target))
                    {
                        settings.entityCreatedCallback(instance, namedEntities[target]);
                    }
                    else
                    {
                        settings.entityCreatedCallback(instance, new List<EntityInstance>(0));
                    }
                }
            }

            // Light probes
            LightProbeGroup lightProbeGroup = root.AddComponent<LightProbeGroup>();
            lightProbeGroup.probePositions = lightProbesPositions.ToArray();

            //LightmapSettings.lightProbes.bakedProbes = lightProbesValues.ToArray();

            if (savePrefab)
            {
                lightmapAtlas.name = "lightmap";
                ctx.AddObjectToAsset("lightmap", lightmapAtlas);

                ctx.AddObjectToAsset(root.name, root);
                ctx.SetMainObject(root);
            }
            
            return root;
        }

        private void LoadPalette(string palettePath)
        {
            Texture2D png = AssetDatabase.LoadAssetAtPath("Assets/qpalette.png", typeof(Texture2D)) as Texture2D;

            for (int i = 0; i < 16; i++)
            {
                for (int j = 0; j < 16; j++)
                {
                    palette[i * 16 + j] = png.GetPixel(j * 16, (15 - i) * 16);
                }
            }
        }

        private void LoadLightProbe(IList<Vertex> vertices, Color color)
        {
            lightProbesPositions.Add(vertices[0].position.SwizzleYZ() * MeshUtils.defaultScale);

            SphericalHarmonicsL2 value = new SphericalHarmonicsL2();
            value.AddAmbientLight(color);
            lightProbesValues.Add(value);
        }

        private Vector2 LoadLightmap(Face face)
        {
            if (face.Lightmap == -1 || face.TextureInfoIndex == -1 || face.NumEdgeIndices <= 0)
            {
                return new Vector2(-1, -1);
            }

            IList<Vertex> vertices = MeshUtils.GetVerticesFromEdges(bsp, face);

            List<int> Us = new List<int>();
            List<int> Vs = new List<int>();
            for (int i = 0; i < face.NumEdgeIndices; i++)
            {
                Us.Add((int)(vertices[i].position.x * face.TextureInfo.UAxis.x + vertices[i].position.y * face.TextureInfo.UAxis.y + vertices[i].position.z * face.TextureInfo.UAxis.z));
                Vs.Add((int)(vertices[i].position.x * face.TextureInfo.VAxis.x + vertices[i].position.y * face.TextureInfo.VAxis.y + vertices[i].position.z * face.TextureInfo.VAxis.z));
            }
            Us.Sort();
            Vs.Sort();

            int _minU = Us[0] / 16;
            int _maxU = Us[face.NumEdgeIndices - 1] / 16;
            int _minV = Vs[0] / 16;
            int _maxV = Vs[face.NumEdgeIndices - 1] / 16;

            int sizeWidth = _maxU - _minU + 1;
            int sizeHeight = _maxV - _minV + 1;

            int typelight = face.LightmapStyles[0];
            int baselight = face.LightmapStyles[1];

            byte[] byteArray = bsp.Lightmaps.GetLightmap(face.Lightmap, sizeWidth * sizeHeight);

            Vector2 lightmapPos = new Vector2(lightmapIndex % MeshUtils.lightmapAtlasSize, (lightmapIndex / MeshUtils.lightmapAtlasSize) * 18);

            for (int i = 0; i < sizeWidth * sizeHeight; i++)
            {
                float colorValue = (((float)(byteArray[i])) / 255) * 2f;
 
                lightmapAtlas.SetPixel((int)lightmapPos.x + (i % sizeWidth) + 1, (int)lightmapPos.y + (i / sizeWidth) + 1, new Color(colorValue, colorValue, colorValue));
            }

            // Fill borders (cuz bilinear filter would have seams otherwise)
            for (int i = 0; i < sizeWidth; i++)
            {
                lightmapAtlas.SetPixel((int)lightmapPos.x + (i % sizeWidth), (int)lightmapPos.y + 0, lightmapAtlas.GetPixel((int)lightmapPos.x + (i % sizeWidth), (int)lightmapPos.y + 1));
                lightmapAtlas.SetPixel((int)lightmapPos.x + (i % sizeWidth), (int)lightmapPos.y + sizeWidth + 1, lightmapAtlas.GetPixel((int)lightmapPos.x + (i % sizeWidth), (int)lightmapPos.y + sizeWidth));
            }
            for (int i = 0; i < sizeHeight; i++)
            {
                lightmapAtlas.SetPixel((int)lightmapPos.x + 0, (int)lightmapPos.y + (i % sizeHeight), lightmapAtlas.GetPixel((int)lightmapPos.x + 1, (int)lightmapPos.y + (i % sizeHeight)));
                lightmapAtlas.SetPixel((int)lightmapPos.x + sizeHeight + 1, (int)lightmapPos.y + (i % sizeHeight), lightmapAtlas.GetPixel((int)lightmapPos.x + sizeHeight, (int)lightmapPos.y + (i % sizeHeight)));
            }

            lightmapIndex += 18; // 16 + 2 (borders)

            lightmapAtlas.Apply(true, false);

            // Light probes
            if (sizeWidth > 5 && sizeHeight > 5)
            {
                LoadLightProbe(vertices, lightmapAtlas.GetPixel((int)lightmapPos.x + sizeWidth / 2, (int)lightmapPos.y + sizeHeight / 2));
            }

            return new Vector2((lightmapIndex - 18) % MeshUtils.lightmapAtlasSize + 1, ((lightmapIndex - 18) / MeshUtils.lightmapAtlasSize) * 18 + 1);
        }

        private Texture2D LoadTexture(Face face, string textureName)
        {
            int textureIndex = bsp.GetTextureIndex(face);

            if (textureIndex == -1 || textureName == "") return new Texture2D(1,1);

            LibBSP.Texture textureData = bsp.Textures[textureIndex];

            Texture2D texture = new Texture2D((int)textureData.Dimensions.x, (int)textureData.Dimensions.y);
            texture.filterMode = FilterMode.Point;

            if (textureData.Mipmaps.Length > 0 && (bsp.MapType == MapType.Quake || bsp.MapType == MapType.Quake2))
            {
                for (int i = 0; i < textureData.Mipmaps[0].Length; i++)
                {
                    texture.SetPixel((int)(i % textureData.Dimensions.x), (int)(textureData.Dimensions.y - (i / textureData.Dimensions.x)), palette[textureData.Mipmaps[0][i]]);
                }
            }
            else // Half Life uses external wad
            {

            }

            texture.name = textureName;
            texture.Apply(true, false);

            return texture;
        }

        public void LoadMaterial(Face face, string textureName)
        {
            //Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Shader shader = Shader.Find("Shader Graphs/lightmapped");

            Texture2D texture = LoadTexture(face, textureName);

            Material material = new Material(shader);
            material.name = textureName;

            material.SetTexture("_MainTex", texture);
            material.SetTexture("_LightMap", lightmapAtlas);
            material.SetFloat("_Glossiness", 0);
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

            if (savePrefab)
            {
                ctx.AddObjectToAsset(textureName + ".asset", texture);
                ctx.AddObjectToAsset(textureName + ".mat", material);
            }

            materialDirectory[textureName] = material;
        }

        protected EntityInstance CreateEntityInstance(Entity entity)
        {
            // Entity.name guaranteed not to be null, empty string is a valid Dictionary key
            if (!namedEntities.ContainsKey(entity.Name) || namedEntities[entity.Name] == null)
            {
                namedEntities[entity.Name] = new List<EntityInstance>();
            }

            EntityInstance instance = new EntityInstance()
            {
                entity = entity,
                gameObject = new GameObject(entity.ClassName + (!string.IsNullOrEmpty(entity.Name) ? " " + entity.Name : string.Empty))
            };

            instance.gameObject.name += instance.gameObject.GetInstanceID();

            return instance;
        }

        protected void SetUpEntityHierarchy(List<EntityInstance> instances)
        {
            foreach (EntityInstance instance in instances)
            {
                SetUpEntityHierarchy(instance);
            }
        }

        protected void SetUpEntityHierarchy(EntityInstance instance)
        {
            if (!instance.entity.ContainsKey("parentname"))
            {
                instance.gameObject.transform.parent = root.transform;
                return;
            }

            if (namedEntities.ContainsKey(instance.entity["parentname"]))
            {
                if (namedEntities[instance.entity["parentname"]].Count > 1)
                {
                    Debug.LogWarning(string.Format("Entity \"{0}\" claims to have parent \"{1}\" but more than one matching entity exists.",
                        instance.gameObject.name,
                        instance.entity["parentname"]), instance.gameObject);
                }
                instance.gameObject.transform.parent = namedEntities[instance.entity["parentname"]][0].gameObject.transform;
            }
            else
            {
                Debug.LogWarning(string.Format("Entity \"{0}\" claims to have parent \"{1}\" but no such entity exists.",
                    instance.gameObject.name,
                    instance.entity["parentname"]), instance.gameObject);
            }
        }

        protected void BuildMesh(EntityInstance instance)
        {
            int modelNumber = instance.entity.ModelNumber;
            Model model = bsp.Models[modelNumber];
            Dictionary<string, List<Mesh>> textureMeshMap = new Dictionary<string, List<Mesh>>();
            GameObject gameObject = instance.gameObject;

            List<Face> faces = bsp.GetFacesInModel(model);
            int i = 0;
            for (i = 0; i < faces.Count; ++i)
            {
                Face face = faces[i];
                if (face.NumEdgeIndices <= 0 && face.NumVertices <= 0)
                {
                    continue;
                }

                int textureIndex = bsp.GetTextureIndex(face);
                string textureName = "";
                if (textureIndex >= 0)
                {
                    LibBSP.Texture texture = bsp.Textures[textureIndex];
                    textureName = LibBSP.Texture.SanitizeName(texture.Name, bsp.MapType);

                    if (!textureName.StartsWith("tools/", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (!textureMeshMap.ContainsKey(textureName) || textureMeshMap[textureName] == null)
                        {
                            textureMeshMap[textureName] = new List<Mesh>();
                        }

                        textureMeshMap[textureName].Add(CreateFaceMesh(face, textureName));
                    }
                }
            }

            if (modelNumber == 0)
            {
                if (bsp.LODTerrains != null)
                {
                    foreach (LODTerrain lodTerrain in bsp.LODTerrains)
                    {
                        if (lodTerrain.TextureIndex >= 0)
                        {
                            LibBSP.Texture texture = bsp.Textures[lodTerrain.TextureIndex];
                            string textureName = texture.Name;

                            if (!textureMeshMap.ContainsKey(textureName) || textureMeshMap[textureName] == null)
                            {
                                textureMeshMap[textureName] = new List<Mesh>();
                            }

                            textureMeshMap[textureName].Add(CreateLoDTerrainMesh(lodTerrain, textureName));
                        }
                    }
                }
            }

            if (settings.meshCombineOptions != MeshCombineOptions.None)
            {
                Mesh[] textureMeshes = new Mesh[textureMeshMap.Count];
                Material[] materials = new Material[textureMeshes.Length];
                i = 0;
                foreach (KeyValuePair<string, List<Mesh>> pair in textureMeshMap)
                {
                    textureMeshes[i] = MeshUtils.CombineAllMeshes(pair.Value.ToArray(), true, false);

                    if (textureMeshes[i].vertices.Length > 0)
                    {
                        if (materialDirectory.ContainsKey(pair.Key))
                        {
                            materials[i] = materialDirectory[pair.Key];
                        }
                        if (settings.meshCombineOptions == MeshCombineOptions.PerMaterial) // MeshCombineOptions.PerMaterial
                        {
                            GameObject textureGameObject = new GameObject(pair.Key);
                            textureGameObject.transform.parent = gameObject.transform;
                            textureGameObject.transform.localPosition = Vector3.zero;
                            textureMeshes[i].Scale(settings.scaleFactor);

                            textureMeshes[i].RecalculateNormals();
                            //Unwrapping.GenerateSecondaryUVSet(textureMeshes[i]);

                            textureMeshes[i].name = gameObject.name + "_mesh_" + materials[i].name;

                            textureMeshes[i].AddToGameObject(new Material[] { materials[i] }, textureGameObject);

                            if (savePrefab)
                                ctx.AddObjectToAsset(textureMeshes[i].name, textureMeshes[i]);
                        }
                        ++i;
                    }
                }

                if (settings.meshCombineOptions == MeshCombineOptions.PerEntity) // MeshCombineOptions.PerEntity
                { 
                    Mesh mesh = MeshUtils.CombineAllMeshes(textureMeshes, false, false);

                    if (mesh.vertices.Length > 0)
                    {
                        mesh.TransformVertices(gameObject.transform.localToWorldMatrix);
                        mesh.Scale(settings.scaleFactor);

                        mesh.RecalculateNormals();
                        //Unwrapping.GenerateSecondaryUVSet(mesh);

                        mesh.name = gameObject.name + "_mesh";

                        mesh.AddToGameObject(materials, gameObject);

                        if (savePrefab)
                            ctx.AddObjectToAsset(mesh.name, mesh);
                    }
                }
            }

            else // MeshCombineOptions.None
            {
                i = 0;
                foreach (KeyValuePair<string, List<Mesh>> pair in textureMeshMap)
                {
                    GameObject textureGameObject = new GameObject(pair.Key);
                    textureGameObject.transform.parent = gameObject.transform;
                    textureGameObject.transform.localPosition = Vector3.zero;
                    Material material = materialDirectory[pair.Key];
                    foreach (Mesh mesh in pair.Value)
                    {
                        if (mesh.vertices.Length > 0)
                        {
                            GameObject faceGameObject = new GameObject("Face");
                            faceGameObject.transform.parent = textureGameObject.transform;
                            faceGameObject.transform.localPosition = Vector3.zero;
                            mesh.Scale(settings.scaleFactor);

                            mesh.RecalculateNormals();
                            //Unwrapping.GenerateSecondaryUVSet(mesh);

                            mesh.AddToGameObject(new Material[] { material }, faceGameObject);

                            if (savePrefab)
                                ctx.AddObjectToAsset("mesh", mesh);
                        }
                    }
                    ++i;
                }
            }

        }

        protected Mesh CreateFaceMesh(Face face, string textureName)
        {
            Vector2 dims;
            if (!materialDirectory.ContainsKey(textureName))
            {
                LoadMaterial(face, textureName);
            }
            if (materialDirectory[textureName].HasProperty("_MainTex") && materialDirectory[textureName].mainTexture != null)
            {
                dims = new Vector2(materialDirectory[textureName].mainTexture.width, materialDirectory[textureName].mainTexture.height);
            }
            else
            {
                dims = new Vector2(128, 128);
            }

            Vector2 lightmapPos = LoadLightmap(face);

            Mesh mesh;
            if (face.DisplacementIndex >= 0)
            {
                mesh = MeshUtils.CreateDisplacementMesh(bsp, face, dims);
            }
            else
            {
                mesh = MeshUtils.CreateFaceMesh(bsp, face, dims, lightmapPos, settings.curveTessellationLevel);
            }

            return mesh;
        }

        // Ainda nao sei para que é isto
        protected Mesh CreateLoDTerrainMesh(LODTerrain lodTerrain, string textureName)
        {
            if (!materialDirectory.ContainsKey(textureName))
            {
                //LoadMaterial(textureName);
            }

            return MeshUtils.CreateMoHAATerrainMesh(bsp, lodTerrain);
        }
#endif
    }
}