#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using System.Linq;

public class EZTogl : EditorWindow
{
    private readonly List<GameObject> toggleObjects = new();
    private GameObject rootObject;
    private DefaultAsset saveFolder;
    private DefaultAsset saveFolderMenu;
    private AnimatorController controller;
    private readonly List<bool> defaultStates = new(); // true = ON, false = OFF
    private Vector2 scrollPos;

    private VRCExpressionParameters vrcParams;

    [MenuItem("--EZ--TOGGLE--EZ--/EZTogl")]
    public static void ShowWindow() { GetWindow<EZTogl>("EZTogl"); }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(position.width), GUILayout.Height(position.height));

        EditorGUIUtility.labelWidth = Mathf.Min(200, position.width * 0.3f);

        rootObject = (GameObject)EditorGUILayout.ObjectField("Root", rootObject, typeof(GameObject), true);

        if (GUILayout.Button("Add Selected From Hierarchy")) foreach (var obj in Selection.gameObjects) if (!toggleObjects.Contains(obj))
                {
                    toggleObjects.Add(obj);
                    defaultStates.Add(false);
                }

        int remove = -1;

        for (int i = 0; i < toggleObjects.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            toggleObjects[i] = (GameObject)EditorGUILayout.ObjectField(toggleObjects[i], typeof(GameObject), true);
            defaultStates[i] = EditorGUILayout.ToggleLeft("Default ON", defaultStates[i], GUILayout.Width(90));

            if (GUILayout.Button("X", GUILayout.Width(20)))
                remove = i;

            EditorGUILayout.EndHorizontal();
        }

        if (remove >= 0)
        {
            toggleObjects.RemoveAt(remove);
            defaultStates.RemoveAt(remove);
        }

        saveFolder = EditorGUILayout.ObjectField("Save AnimationClips Folder", saveFolder, typeof(DefaultAsset), false) as DefaultAsset;
        saveFolderMenu = EditorGUILayout.ObjectField("Save Menu Folder", saveFolderMenu, typeof(DefaultAsset), false) as DefaultAsset;
        controller = EditorGUILayout.ObjectField("Animator Controller", controller, typeof(AnimatorController), false) as AnimatorController;
        vrcParams = EditorGUILayout.ObjectField("VRCParams", vrcParams, typeof(VRCExpressionParameters), false) as VRCExpressionParameters;

        if (GUILayout.Button("Generate Toggles")) GenerateAll();

        EditorGUILayout.EndScrollView();
    }

    private void GenerateAll()
    {
        var menuList = new List<VRCExpressionsMenu>();

        for (int i = 0; i < toggleObjects.Count; i += 8)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = $"EZToglmenu_{i / 8}";
            menuList.Add(menu);

            AssetDatabase.CreateAsset(menu, $"{AssetDatabase.GetAssetPath(saveFolderMenu)}/EZToglmenu_{i / 8}.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset($"{AssetDatabase.GetAssetPath(saveFolderMenu)}/EZToglmenu_{i / 8}.asset");
        }


        for (int i = 0; i < toggleObjects.Count; i++)
        {
            if (!toggleObjects[i]) continue;
            string pN = toggleObjects[i].name + "_Toggle";
            
            bool exists = false;
            foreach (var p in controller.parameters) if (p.name == pN) { exists = true; break; }

            if (!exists) controller.AddParameter(pN, AnimatorControllerParameterType.Bool);

            exists = false;

            foreach (var p in vrcParams.parameters)
                if (p.name == pN) { exists = true; break; }

            if (!exists)
            {
                vrcParams.parameters = new List<VRCExpressionParameters.Parameter>(vrcParams.parameters)
                    { new VRCExpressionParameters.Parameter { name = pN, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = defaultStates[i] ? 1f : 0f, saved = true } }.ToArray();
                EditorUtility.SetDirty(vrcParams);
                Debug.Log($"Added VRC param: {pN} (default {defaultStates[i]})");
            }

            CreateLayerAndTransitions(toggleObjects[i].name, CreateToggleClip(toggleObjects[i], "_On"), CreateToggleClip(toggleObjects[i], "_Off"), pN, defaultStates[i]);

            var currentMenu = menuList.FirstOrDefault(c => c.controls.Count < 8);
            currentMenu.Parameters = vrcParams;

            currentMenu.controls.Add(new VRCExpressionsMenu.Control
                { name = toggleObjects[i].name, parameter = new VRCExpressionsMenu.Control.Parameter { name = pN }, type = VRCExpressionsMenu.Control.ControlType.Toggle });
            EditorUtility.SetDirty(currentMenu);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(currentMenu));
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(vrcParams));
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(controller));
        AssetDatabase.Refresh();
        Debug.Log("Generation complete.");
    }

    private AnimationClip CreateToggleClip(GameObject obj, string name)
    {
        var binding = new EditorCurveBinding
        { path = AnimationUtility.CalculateTransformPath(obj.transform, rootObject.transform), propertyName = "m_IsActive", type = typeof(GameObject) };
        var clip = new AnimationClip();
        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, name.Contains("_On") ? 1f : 0f));
        AssetDatabase.CreateAsset(clip, $"{AssetDatabase.GetAssetPath(saveFolder)}/{obj.name + name}.anim");
        return clip;
    }

    private void CreateLayerAndTransitions(string baseName, AnimationClip onC, AnimationClip offC, string pN, bool startsOn)
    {
        var sm = new AnimatorStateMachine { name = baseName + "_SM", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(sm, AssetDatabase.GetAssetPath(controller));

        controller.AddLayer(new AnimatorControllerLayer { name = baseName + "_Toggle", defaultWeight = 1f, stateMachine = sm });

        var onState = sm.AddState("On");
        var offState = sm.AddState("Off");

        onState.motion = onC;
        offState.motion = offC;

        sm.defaultState = startsOn ? onState : offState;

        var tOn = offState.AddTransition(onState);
        tOn.hasExitTime = false;
        tOn.hasFixedDuration = false;
        tOn.duration = 0;
        tOn.exitTime = 0;
        tOn.AddCondition(AnimatorConditionMode.If, 0, baseName + "_Toggle");

        var tOff = onState.AddTransition(offState);
        tOff.hasExitTime = false;
        tOff.hasFixedDuration = false;
        tOff.duration = 0;
        tOff.exitTime = 0;
        tOff.AddCondition(AnimatorConditionMode.IfNot, 0, baseName + "_Toggle");

        Debug.Log($"Layer {baseName}_Toggle created. Default = {(startsOn ? "ON" : "OFF")}");
    }
}
#endif
