/*
 * ====================================================================================
 * ValueOverlayRowDrawer.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Inspector for Value Overlay rows. Drag a GameObject in, then pick any
 *              number, bool, enum, vector or colour on its components from one list
 *              ("NBERRain / targetIntensity"); material rows list the shader's
 *              properties. Also adds a menu item that puts a Value Overlay in the scene.
 * ====================================================================================
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NPIREEXE.Capture.Editor
{
    [CustomPropertyDrawer(typeof(ValueOverlay.Row))]
    public class ValueOverlayRowDrawer : PropertyDrawer
    {
        private static readonly HashSet<Type> Supported = new HashSet<Type>
        {
            typeof(float), typeof(double), typeof(int), typeof(long), typeof(bool),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Color)
        };

        private static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        private static int LineCount(SerializedProperty p)
        {
            if (!p.isExpanded) return 1;
            int n = 3; // foldout, label, source
            n += (ValueOverlay.Source)p.FindPropertyRelative("source").enumValueIndex == ValueOverlay.Source.ShaderGlobal ? 1 : 2;
            n += 1; // show bar
            if (p.FindPropertyRelative("showBar").boolValue) n += 1; // min / max
            n += 1; // decimals
            return n;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            LineCount(property) * Line;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var r = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            SerializedProperty rowLabel = property.FindPropertyRelative("label");
            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded,
                string.IsNullOrEmpty(rowLabel.stringValue) ? label.text : rowLabel.stringValue, true);
            if (!property.isExpanded) { EditorGUI.EndProperty(); return; }

            EditorGUI.indentLevel++;
            r.y += Line; EditorGUI.PropertyField(r, rowLabel);
            SerializedProperty source = property.FindPropertyRelative("source");
            r.y += Line; EditorGUI.PropertyField(r, source);

            switch ((ValueOverlay.Source)source.enumValueIndex)
            {
                case ValueOverlay.Source.Member:
                    DrawMember(ref r, property);
                    break;
                case ValueOverlay.Source.MaterialProperty:
                    DrawMaterial(ref r, property);
                    break;
                case ValueOverlay.Source.ShaderGlobal:
                    r.y += Line; EditorGUI.PropertyField(r, property.FindPropertyRelative("property"), new GUIContent("Global Name"));
                    break;
            }

            SerializedProperty showBar = property.FindPropertyRelative("showBar");
            r.y += Line; EditorGUI.PropertyField(r, showBar);
            if (showBar.boolValue)
            {
                r.y += Line;
                Rect field = EditorGUI.PrefixLabel(r, new GUIContent("Min / Max"));
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                float half = (field.width - 4f) / 2f;
                EditorGUI.PropertyField(new Rect(field.x, field.y, half, field.height), property.FindPropertyRelative("min"), GUIContent.none);
                EditorGUI.PropertyField(new Rect(field.x + half + 4f, field.y, half, field.height), property.FindPropertyRelative("max"), GUIContent.none);
                EditorGUI.indentLevel = indent;
            }
            r.y += Line; EditorGUI.PropertyField(r, property.FindPropertyRelative("decimals"));
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static void DrawMember(ref Rect r, SerializedProperty property)
        {
            SerializedProperty target = property.FindPropertyRelative("target");
            SerializedProperty member = property.FindPropertyRelative("member");

            r.y += Line;
            // Accept a GameObject or any component; the list below picks the actual component.
            var current = target.objectReferenceValue as Component;
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object picked = EditorGUI.ObjectField(r, new GUIContent("Object", "GameObject or component that holds the value."),
                current != null ? current.gameObject : null, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                var go = picked as GameObject;
                target.objectReferenceValue = go != null ? go.transform : null;
                member.stringValue = "";
            }

            r.y += Line;
            if (current == null)
            {
                EditorGUI.LabelField(r, "Value", "Drag in a GameObject first");
                return;
            }

            var options = new List<(Component comp, string name)>();
            var names = new List<string>();
            foreach (Component comp in current.GetComponents<Component>())
            {
                if (comp == null || comp is ValueOverlay) continue;
                foreach (string name in Members(comp.GetType()))
                {
                    options.Add((comp, name));
                    names.Add($"{comp.GetType().Name} / {name}");
                }
            }
            int index = options.FindIndex(o => o.comp == current && o.name == member.stringValue);
            if (index < 0 && !string.IsNullOrEmpty(member.stringValue))
            {
                options.Insert(0, (current, member.stringValue));
                names.Insert(0, $"{current.GetType().Name} / {member.stringValue} (not found)");
                index = 0;
            }
            int chosen = EditorGUI.Popup(r, "Value", index, names.ToArray());
            if (chosen != index && chosen >= 0)
            {
                target.objectReferenceValue = options[chosen].comp;
                member.stringValue = options[chosen].name;
            }
        }

        /// <summary>Fields and properties worth showing: supported types, declared on game or package types.</summary>
        private static IEnumerable<string> Members(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var seen = new HashSet<string>();
            for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component); t = t.BaseType)
            {
                bool engineType = t.Namespace != null && t.Namespace.StartsWith("UnityEngine");
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (f.Name.Contains("<") || !IsSupported(f.FieldType)) continue;
                    if (!f.IsPublic && !Attribute.IsDefined(f, typeof(SerializeField)) && !f.Name.StartsWith("_")) continue;
                    if (seen.Add(f.Name)) yield return f.Name;
                }
                foreach (PropertyInfo p in t.GetProperties(flags))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0 || !IsSupported(p.PropertyType)) continue;
                    if (p.GetGetMethod() == null) continue; // public getters only
                    if (engineType && Attribute.IsDefined(p, typeof(ObsoleteAttribute))) continue;
                    if (seen.Add(p.Name)) yield return p.Name;
                }
            }
        }

        private static bool IsSupported(Type type) => Supported.Contains(type) || type.IsEnum;

        private static void DrawMaterial(ref Rect r, SerializedProperty property)
        {
            SerializedProperty mat = property.FindPropertyRelative("material");
            SerializedProperty prop = property.FindPropertyRelative("property");
            r.y += Line; EditorGUI.PropertyField(r, mat);

            r.y += Line;
            var material = mat.objectReferenceValue as Material;
            if (material == null || material.shader == null)
            {
                EditorGUI.PropertyField(r, prop, new GUIContent("Property"));
                return;
            }
            Shader shader = material.shader;
            var names = new List<string>();
            var labels = new List<string>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture) continue;
                names.Add(shader.GetPropertyName(i));
                labels.Add($"{shader.GetPropertyDescription(i)} ({shader.GetPropertyName(i)})");
            }
            int index = names.IndexOf(prop.stringValue);
            int chosen = EditorGUI.Popup(r, "Property", index, labels.ToArray());
            if (chosen != index && chosen >= 0) prop.stringValue = names[chosen];
        }

        [MenuItem("NPIRE EXE/Tools/Capture/Add Value Overlay")]
        private static void AddToScene()
        {
            var go = new GameObject("Value Overlay");
            Undo.RegisterCreatedObjectUndo(go, "Add Value Overlay");
            go.AddComponent<ValueOverlay>().rows.Add(new ValueOverlay.Row());
            Selection.activeGameObject = go;
        }
    }
}
