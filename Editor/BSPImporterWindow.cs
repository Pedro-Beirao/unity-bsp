#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using BSPImporter;

/// <summary>
/// Editor window for importing BSPs, a simple example of how to provide a GUI
/// for importing a BSP. This class can be deleted without causing any problems.
/// </summary>
public class BSPImporterWindow : EditorWindow
{

    protected BSPLoader.Settings settings;

    /// <summary>
    /// Shows this window.
    /// </summary>
    [MenuItem("BSP Importer/Import BSP")]
    public static void ShowWindow()
    {
        BSPImporterWindow window = GetWindow<BSPImporterWindow>();
#if UNITY_5_1 || UNITY_5_2 || UNITY_5_3_OR_NEWER
        window.titleContent = new GUIContent("Example BSP Importer GUI");
#else
        window.title = "Example BSP Importer GUI";
#endif
        window.autoRepaintOnSceneChange = true;
        DontDestroyOnLoad(window);
    }

    /// <summary>
    /// GUI for this window.
    /// </summary>
    protected virtual void OnGUI()
    {
        EditorGUILayout.BeginVertical();
        {
            DrawImportOptions();
            DrawImportButton();
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Draws GUI elements for BSP Importer settings.
    /// </summary>
    protected virtual void DrawImportOptions()
    {
        if (settings.path == null)
        {
            settings.path = "";
            settings.meshCombineOptions = BSPLoader.MeshCombineOptions.PerEntity;
            settings.curveTessellationLevel = 3;
            settings.scaleFactor = MeshUtils.defaultScale;
        }

        EditorGUILayout.BeginHorizontal();
        {
            settings.path = EditorGUILayout.TextField(new GUIContent("Import BSP file", "The path to a BSP file on the hard drive."), settings.path);
            if (GUILayout.Button("Browse...", GUILayout.MaxWidth(100)))
            {
                string dir = string.IsNullOrEmpty(settings.path) ? "." : Path.GetDirectoryName(settings.path);
#if UNITY_5_2 || UNITY_5_3_OR_NEWER
                string[] filters = {
                    "BSP Files", "BSP",
                    "D3DBSP Files", "D3DBSP",
                    "All Files", "*",
                };

                settings.path = EditorUtility.OpenFilePanelWithFilters("Select BSP file", dir, filters);
#else
                settings.path = EditorUtility.OpenFilePanel("Select BSP file", dir, "*BSP");
#endif
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {

        }
        EditorGUILayout.EndHorizontal();

        settings.meshCombineOptions = (BSPLoader.MeshCombineOptions)EditorGUILayout.EnumPopup(new GUIContent("Mesh combining", "Options for combining meshes. Per entity gives the cleanest hierarchy but may corrupt meshes with too many vertices."), settings.meshCombineOptions);
        settings.curveTessellationLevel = EditorGUILayout.IntSlider(new GUIContent("Curve detail", "Number of triangles used to tessellate curves. Higher values give smoother curves with exponentially more vertices."), settings.curveTessellationLevel, 1, 50);
        settings.scaleFactor = EditorGUILayout.FloatField(new GUIContent("Scale", "Amount to scale coordinates by. 0.0254 converts inches to meters."), settings.scaleFactor);
    }

    /// <summary>
    /// Draws a button to start the import process.
    /// </summary>
    protected virtual void DrawImportButton()
    {
        if (GUILayout.Button("Import"))
        {
            BSPLoader loader = new BSPLoader()
            {
                settings = settings
            };

            GameObject obj = loader.LoadBSP(null);
        }
    }

}
#endif
