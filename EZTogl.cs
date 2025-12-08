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
    private readonly List<bool> defaultStates = new();
    private Vector2 scrollPos;

    private VRCExpressionParameters vrcParams;

    [MenuItem("--EZ--TOGGLE--EZ--/EZTogl")]
    public static void ShowWindow() { GetWindow<EZTogl>("EZTogl"); }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Width(position.width), GUILayout.Height(position.height));
        EditorGUIUtility.labelWidth = Mathf.Min(200, position.width * 0.5f);

        rootObject = (GameObject)EditorGUILayout.ObjectField("Root", rootObject, typeof(GameObject), true);

        if (GUILayout.Button("Add Selected From Hierarchy"))
            foreach (var obj in Selection.gameObjects)
                if (!toggleObjects.Contains(obj)) { toggleObjects.Add(obj); defaultStates.Add(false); }

        int remove = -1;
        for (int i = 0; i < toggleObjects.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            toggleObjects[i] = (GameObject)EditorGUILayout.ObjectField(toggleObjects[i], typeof(GameObject), true);
            defaultStates[i] = EditorGUILayout.ToggleLeft("Default ON", defaultStates[i], GUILayout.Width(90));
            if (GUILayout.Button("X", GUILayout.Width(20))) remove = i;
            EditorGUILayout.EndHorizontal();
        }
        if (remove >= 0) { toggleObjects.RemoveAt(remove); defaultStates.RemoveAt(remove); }

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
            AssetDatabase.CreateAsset(menu, $"{AssetDatabase.GetAssetPath(saveFolderMenu)}/{menu.name}.asset");
        }

        bool ParamExists(IEnumerable<AnimatorControllerParameter> list, string name) => list.Any(p => p.name == name);

        bool VRCParamExists(IEnumerable<VRCExpressionParameters.Parameter> list, string name) => list.Any(p => p.name == name);

        var currentMenu = menuList[0];
        for (int i = 0; i < toggleObjects.Count; i++)
        {
            if (!toggleObjects[i]) continue;
            string pN = toggleObjects[i].name + "_Toggle";

            if (!ParamExists(controller.parameters, pN))
            {
                controller.AddParameter(pN, AnimatorControllerParameterType.Bool);
                EditorUtility.SetDirty(controller);
            }

            if (!VRCParamExists(vrcParams.parameters, pN))
            {
                vrcParams.parameters = vrcParams.parameters.Append(new VRCExpressionParameters.Parameter { name = pN, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = defaultStates[i] ? 1f : 0f, saved = true }).ToArray();
                EditorUtility.SetDirty(vrcParams);
                Debug.Log($"Added VRC param: {pN} (default {defaultStates[i]})");
            }

            CreateLayerAndTransitions(toggleObjects[i].name, CreateToggleClip(toggleObjects[i], "_On"), CreateToggleClip(toggleObjects[i], "_Off"), defaultStates[i] );

            currentMenu = menuList.First(c => c.controls.Count < 8);
            currentMenu.Parameters = vrcParams;
            currentMenu.controls.Add(new VRCExpressionsMenu.Control
                { name = toggleObjects[i].name, parameter = new VRCExpressionsMenu.Control.Parameter { name = pN }, type = VRCExpressionsMenu.Control.ControlType.Toggle });
            EditorUtility.SetDirty(currentMenu);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(currentMenu));
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(vrcParams));
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(controller));
        AssetDatabase.Refresh();
        Debug.Log("Generation complete.");
    }

    private AnimationClip CreateToggleClip(GameObject o, string n)
    {
        var c = new AnimationClip();
        AnimationUtility.SetEditorCurve(c, new EditorCurveBinding { path = AnimationUtility.CalculateTransformPath(o.transform, rootObject.transform), propertyName = "m_IsActive", type = typeof(GameObject) }, AnimationCurve.Constant(0f, 0f, n.Contains("_On") ? 1f : 0f));
        AssetDatabase.CreateAsset(c, $"{AssetDatabase.GetAssetPath(saveFolder)}/{o.name + n}.anim");
        return c;
    }

    private void CreateLayerAndTransitions(string baseName, AnimationClip onC, AnimationClip offC, bool startsOn)
    {
        var sm = new AnimatorStateMachine { name = baseName + "_SM", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(sm, AssetDatabase.GetAssetPath(controller));

        controller.AddLayer(new AnimatorControllerLayer { name = baseName + "_Toggle", defaultWeight = 1f, stateMachine = sm });

        var onState = sm.AddState("On"); onState.motion = onC;
        var offState = sm.AddState("Off"); offState.motion = offC;
        sm.defaultState = startsOn ? onState : offState;

        void SetupTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = false;
            t.duration = 0;
            t.exitTime = 0;
            t.AddCondition(mode, 0, baseName + "_Toggle");
        }

        SetupTransition(offState, onState, AnimatorConditionMode.If);
        SetupTransition(onState, offState, AnimatorConditionMode.IfNot);

        EditorUtility.SetDirty(controller);
        Debug.Log($"Layer {baseName}_Toggle created. Default = {(startsOn ? "ON" : "OFF")}");
    }

}
#endif
