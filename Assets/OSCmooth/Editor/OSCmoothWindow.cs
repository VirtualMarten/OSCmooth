﻿using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using OSCTools.OSCmooth.Animation;
using OSCTools.OSCmooth.Types;

namespace OSCTools.OSCmooth
{
    public class OSCmoothWindow : EditorWindow
    {
        private VRCAvatarDescriptor _avDescriptor;
        private AnimatorController _animatorController;

        [SerializeField]
        private List<OSCmoothLayer> _smoothLayer = new List<OSCmoothLayer>();

        private int _layerSelect = 4;
        private string _baseParamName;
        private bool _showParameters;
        private Vector2 paramMenuScroll;

        readonly private string[] _animatorSelection = new string[]
        {
            "Base","Additive","Gesture","Action","FX"
        };

        [MenuItem("Tools/OSCmooth")]
        public static void ShowWindow()
        {
            var window = EditorWindow.GetWindow<OSCmoothWindow>("OSCmooth");
            window.maxSize = new Vector2(512, 1024);
            window.minSize = new Vector2(256, 768);
            window.Show();
        }

        private void OnGUI()
        {
            _avDescriptor = (VRCAvatarDescriptor)EditorGUILayout.ObjectField
            (
                new GUIContent
                (
                    "Avatar",
                    "The VRC Avatar that will have the smoothing animation layers set up on. " +
                    "The Avatar must have a VRCAvatarDescriptor to show up in this field."
                ),
                _avDescriptor,
                typeof(VRCAvatarDescriptor),
                true
            );

            if (_avDescriptor != null)
            {
                _layerSelect = EditorGUILayout.Popup
                (
                    new GUIContent
                    (
                        "Layer",
                        "This selects what VRChat Playable Layer you would like to set up " +
                        "the following Binary Animation Layer into. A layer must be populated " +
                        "in order for the tool to properly set up an Animation Layer."
                    ),
                    _layerSelect,
                    _animatorSelection
                );

                _animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GetAssetPath(_avDescriptor.baseAnimationLayers[_layerSelect].animatorController));

                EditorGUILayout.Space();

                _showParameters = EditorGUILayout.Foldout(_showParameters, "Parameter Configuration");

                EditorGUI.indentLevel = 1;

                EditorGUILayout.BeginScrollView(paramMenuScroll, GUILayout.MaxWidth(512));
                if (_showParameters)
                {                    
                    foreach (OSCmoothLayer layer in _smoothLayer)
                    {
                        EditorGUI.indentLevel = 2;
                        layer.isVisible = EditorGUILayout.Foldout(layer.isVisible, layer.paramName);

                        if (layer.isVisible)
                        {
                            EditorGUIUtility.labelWidth = 80;

                            layer.paramName = EditorGUILayout.TextField
                            (
                                new GUIContent
                                (
                                    "",
                                    "The specific float parameter that will have the smoothness layer have applied to."
                                ),
                                layer.paramName
                            );
                            EditorGUILayout.BeginHorizontal();

                            EditorGUILayout.LabelField
                            (
                                new GUIContent
                                (
                                    "Smoothness",
                                    "How much of a percentage the previous float values influence the current one."
                                )
                            );

                            EditorGUIUtility.labelWidth = 80;

                            layer.localSmoothness = EditorGUILayout.FloatField
                            (
                                new GUIContent
                                (
                                    "Local",
                                    "How much % smoothness you (locally) will see when a parameter" +
                                    "changes value. Higher values represent more smoothness, and vice versa."
                                ),
                                layer.localSmoothness,
                                GUILayout.MaxWidth(110)
                            );
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();

                            layer.remoteSmoothness = EditorGUILayout.FloatField
                            (
                                new GUIContent
                                (
                                    "Net",
                                    "How much % smoothness remote users will see when a parameter" +
                                    "changes value. Higher values represent more smoothness, and vice versa."
                                ),
                                layer.remoteSmoothness, 
                                GUILayout.MaxWidth(110)
                            );

                            EditorGUILayout.EndHorizontal();

                            //layer.flipInputOutput = EditorGUILayout.Toggle
                            //(
                            //    new GUIContent
                            //    (
                            //        "Flip IO",
                            //        "This setting will automatically switch out the provided base parameter in exising animations  " +
                            //        "out with the generated Proxy parameter from this tool. This is useful if you're looking to " +
                            //        "convert your existing animation setup initially.\n\nWARNING: This setting is " +
                            //        "potentially very destructive to the animator, It would be recommended to back-" +
                            //        "up the Animator before using this setting, as a precaution, or manually swap out " +
                            //        "the animations to use the Proxy float."
                            //    ),
                            //    layer.flipInputOutput
                            //);

                            EditorGUILayout.Space();
                            GUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();

                            if (GUILayout.Button
                            (
                                new GUIContent
                                (
                                    "Remove Parameter",
                                    "Removes specified parameter from smoothness creation."
                                ),
                                GUILayout.MaxWidth(256)
                            ))
                            {
                                _smoothLayer.Remove(layer);
                            }

                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            EditorGUILayout.Space();
                        }
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                EditorGUILayout.Space();

                if (GUILayout.Button
                (
                    new GUIContent
                    (
                        "Add New Parameter",
                        "Adds a new Parameter configuration."
                    ),
                    GUILayout.MaxWidth(256)
                ))
                {
                    _smoothLayer.Add(new OSCmoothLayer());
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                EditorGUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button
                (
                    new GUIContent
                    (
                        "Apply OSCmooth to Animator",
                        "Creates a new Layer in the selected Animator Controller that will apply smoothing " +
                        "to the listed configured parameters."
                    ),
                    GUILayout.MaxWidth(256)
                ))
                {
                    OSCmoothAnimationHandler animHandler = new OSCmoothAnimationHandler();

                    animHandler.animatorController = _animatorController;
                    animHandler.smoothLayer = _smoothLayer;

                    animHandler.CreateSmoothAnimationLayer();
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
    }
}