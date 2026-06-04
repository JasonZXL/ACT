using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomEditor(typeof(PlayerAttackHitboxController))]
public class PlayerAttackHitboxControllerEditor : Editor
{
    private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();
    private int _sceneEditIndex = -1;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        string[] options = BuildDefinitionOptions();
        _sceneEditIndex = Mathf.Clamp(_sceneEditIndex, -1, options.Length - 2);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Hitbox Editing", EditorStyles.boldLabel);
        _sceneEditIndex = EditorGUILayout.Popup("Edit Definition", _sceneEditIndex + 1, options) - 1;
        EditorGUILayout.HelpBox(
            "Select one attack definition here, then adjust its hitbox directly in the Scene view.",
            MessageType.Info);
    }

    private void OnSceneGUI()
    {
        serializedObject.Update();

        SerializedProperty definition = GetSceneEditDefinition();
        if (definition == null)
        {
            return;
        }

        DrawDefinitionHandle(definition);
        serializedObject.ApplyModifiedProperties();
    }

    private string[] BuildDefinitionOptions()
    {
        SerializedProperty definitions = serializedObject.FindProperty("attackHitDefinitions");
        int count = definitions != null ? definitions.arraySize : 0;
        string[] options = new string[count + 1];
        options[0] = "Default";

        for (int i = 0; i < count; i++)
        {
            SerializedProperty definition = definitions.GetArrayElementAtIndex(i);
            SerializedProperty attackId = definition.FindPropertyRelative("attackId");
            string label = !string.IsNullOrEmpty(attackId.stringValue)
                ? attackId.stringValue
                : $"Element {i}";
            options[i + 1] = label;
        }

        return options;
    }

    private SerializedProperty GetSceneEditDefinition()
    {
        if (_sceneEditIndex < 0)
        {
            return serializedObject.FindProperty("defaultHitDefinition");
        }

        SerializedProperty definitions = serializedObject.FindProperty("attackHitDefinitions");
        if (definitions == null || _sceneEditIndex >= definitions.arraySize)
        {
            return null;
        }

        return definitions.GetArrayElementAtIndex(_sceneEditIndex);
    }

    private void DrawDefinitionHandle(SerializedProperty definition)
    {
        SerializedProperty shape = definition.FindPropertyRelative("shape");
        SerializedProperty originProperty = definition.FindPropertyRelative("origin");
        SerializedProperty centerOffset = definition.FindPropertyRelative("centerOffset");
        SerializedProperty boxHalfExtents = definition.FindPropertyRelative("boxHalfExtents");
        SerializedProperty sphereRadius = definition.FindPropertyRelative("sphereRadius");
        SerializedProperty rotationOffset = definition.FindPropertyRelative("rotationOffset");

        PlayerAttackHitboxController controller = (PlayerAttackHitboxController)target;
        Transform origin = originProperty.objectReferenceValue as Transform;
        if (origin == null)
        {
            origin = controller.transform;
        }

        Vector3 localCenter = centerOffset.vector3Value;
        Vector3 worldCenter = origin.TransformPoint(localCenter);
        Quaternion worldRotation = origin.rotation * Quaternion.Euler(rotationOffset.vector3Value);

        EditorGUI.BeginChangeCheck();
        Vector3 movedWorldCenter = Handles.PositionHandle(worldCenter, worldRotation);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Move Attack Hitbox");
            centerOffset.vector3Value = origin.InverseTransformPoint(movedWorldCenter);
        }

        EditorGUI.BeginChangeCheck();
        Quaternion editedWorldRotation = Handles.RotationHandle(worldRotation, movedWorldCenter);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Rotate Attack Hitbox");
            rotationOffset.vector3Value = (Quaternion.Inverse(origin.rotation) * editedWorldRotation).eulerAngles;
        }

        if (shape.enumValueIndex == (int)PlayerAttackHitboxController.HitboxShape.Sphere)
        {
            DrawSphereHandle(controller, sphereRadius, movedWorldCenter, editedWorldRotation);
        }
        else
        {
            DrawBoxHandle(controller, centerOffset, boxHalfExtents, origin, editedWorldRotation);
        }
    }

    private void DrawBoxHandle(
        PlayerAttackHitboxController controller,
        SerializedProperty centerOffset,
        SerializedProperty boxHalfExtents,
        Transform origin,
        Quaternion worldRotation)
    {
        Vector3 localCenter = centerOffset.vector3Value;
        Vector3 halfExtents = Vector3.Max(boxHalfExtents.vector3Value, Vector3.one * 0.01f);

        using (new Handles.DrawingScope(Matrix4x4.TRS(origin.position, worldRotation, Vector3.one)))
        {
            _boxHandle.center = Quaternion.Inverse(worldRotation) * (origin.TransformPoint(localCenter) - origin.position);
            _boxHandle.size = halfExtents * 2f;

            EditorGUI.BeginChangeCheck();
            _boxHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Resize Attack Hitbox");
                Vector3 worldCenter = origin.position + worldRotation * _boxHandle.center;
                centerOffset.vector3Value = origin.InverseTransformPoint(worldCenter);
                boxHalfExtents.vector3Value = Vector3.Max(_boxHandle.size * 0.5f, Vector3.one * 0.01f);
            }
        }
    }

    private void DrawSphereHandle(
        PlayerAttackHitboxController controller,
        SerializedProperty sphereRadius,
        Vector3 worldCenter,
        Quaternion worldRotation)
    {
        float radius = Mathf.Max(0.01f, sphereRadius.floatValue);

        EditorGUI.BeginChangeCheck();
        float editedRadius = Handles.RadiusHandle(worldRotation, worldCenter, radius);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Resize Attack Hitbox");
            sphereRadius.floatValue = Mathf.Max(0.01f, editedRadius);
        }
    }
}
