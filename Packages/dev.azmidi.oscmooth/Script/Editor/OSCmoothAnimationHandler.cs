﻿using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Jobs;
using OSCmooth.Types;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using OSCmooth.Util;
using UnityEditor.Animations;
using System.IO;
using static OSCmooth.Filters;

namespace OSCmooth.Editor.Animation
{
    public class OSCmoothAnimationHandler
    {
        private AnimatorController _animatorController;
        private string _animatorPath;
        private string _animatorGUID;
        private List<OSCmoothParameter> _parameters;
        private string _smoothExportDirectory;
        private string _binaryExportDirectory;

        public OSCmoothAnimationHandler(List<OSCmoothParameter> parameters,
                                        AnimatorController animatorController, 
                                        string smoothExportDirectory, 
                                        string binaryExportDirectory)
        {
            _animatorController = animatorController;
            _animatorPath = AssetDatabase.GetAssetPath(_animatorController);
            _animatorGUID = AssetDatabase.AssetPathToGUID(_animatorPath);
            _parameters = parameters;
            _smoothExportDirectory = smoothExportDirectory;
            _binaryExportDirectory = binaryExportDirectory;
        }

        public void RemoveAllOSCmoothFromController()
        {
            CleanAnimatorBlendTreeBloat("OSCm");
            RevertStateMachineParameters();
            RemoveExtendedParametersInController("OSCm");
            RemoveContainingLayersInController("OSCm");
        }

        public void CreateLayer()
        {
            AssetDatabase.StartAssetEditing();

            AnimatorControllerLayer animLayer = CreateAnimLayerInController("_OSCmooth_Gen");
            var state = animLayer.stateMachine.AddState("OSCmooth", new Vector3(30, 170, 0));

            var rootBlend = new BlendTree()
            {
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                name = "OSCmooth_Root",
                useAutomaticThresholds = false
            };

            List<BlendTree> _trees = new List<BlendTree>();
            if (_parameters.Any(p => p.binarySizeSelection > 0))
                _trees.Add(CreateBinaryBlend());
            _trees.Add(CreateSmoothBlend());

            var childs = new List<ChildMotion>();
            foreach (var tree in _trees)
            {
                childs.Add(new ChildMotion
                {
                    directBlendParameter = $"{oscmPrefix}{blendSuffix}",
                    motion = tree,
                    timeScale = 1
                });
            }

            rootBlend.children = childs.ToArray();

            state.motion = rootBlend;

            AssetDatabase.AddObjectToAsset(rootBlend, _animatorPath);

            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        public BlendTree CreateSmoothBlend()
        {
            _animatorController.CheckAndCreateParameter("IsLocal", AnimatorControllerParameterType.Float);

            var rootBlend = new BlendTree()
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                name = "OSCm_Smoother_Root",
                useAutomaticThresholds = false,
                blendParameter = "IsLocal"
            };

            var nameLocalWD = "OSCm_Local";
            var nameRemoteWD = "OSCm_Remote";

            var basisLocalBlendTree = new BlendTree()
            {
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                name = nameLocalWD,
                useAutomaticThresholds = false

            };

            var basisRemoteBlendTree = new BlendTree()
            {
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                name = nameRemoteWD,
                useAutomaticThresholds = false
            };

            // Creating a '1Set' parameter that holds a value of one at all times for the Direct BlendTree
            _animatorController.CheckAndCreateParameter($"{oscmPrefix}{blendSuffix}", AnimatorControllerParameterType.Float, 1f);

            var localChildMotions = new List<ChildMotion>();
            var remoteChildMotions = new List<ChildMotion>();

            int i = 0;
            foreach (OSCmoothParameter p in _parameters)
            {
                EditorUtility.DisplayProgressBar("OSCmooth", "Creating Smoothing Direct BlendTree", (float)i++/_parameters.Count);
                if (p.convertToProxy)
                {
                    RenameAllStateMachineInstancesOfBlendParameter(p.paramName, $"{oscmPrefix}{proxyPrefix}{p.paramName}");
                }

                var motionLocal = CreateSmoothingBlendTree(p.localSmoothness, p.paramName, $"{oscmPrefix}{localPrefix}");
                var motionRemote = CreateSmoothingBlendTree(p.remoteSmoothness, p.paramName, $"{oscmPrefix}{remotePrefix}");

                localChildMotions.Add(new ChildMotion
                {
                    directBlendParameter = $"{oscmPrefix}{blendSuffix}",
                    motion = motionLocal,
                    timeScale = 1
                });

                remoteChildMotions.Add(new ChildMotion
                {
                    directBlendParameter = $"{oscmPrefix}{blendSuffix}",
                    motion = motionRemote,
                    timeScale = 1,
                });
            }
            EditorUtility.ClearProgressBar();
            basisLocalBlendTree.children = localChildMotions.ToArray();
            basisRemoteBlendTree.children = remoteChildMotions.ToArray();

            rootBlend.AddChild(basisRemoteBlendTree, 0f);
            rootBlend.AddChild(basisLocalBlendTree, 1f);

            AssetDatabase.AddObjectToAsset(rootBlend, _animatorPath);
            AssetDatabase.AddObjectToAsset(basisRemoteBlendTree, _animatorPath);
            AssetDatabase.AddObjectToAsset(basisLocalBlendTree, _animatorPath);

            return rootBlend;
        }

        public BlendTree CreateBinaryBlend()
        {
            // Creating BlendTree objects to better customize them in the AC Editor         

            var binaryTreeRoot = new BlendTree()
            {
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                name = "OSCm_Binary_Root",
                useAutomaticThresholds = false
            };

            // Creating a '1Set' parameter that holds a value of one at all times for the Direct BlendTree
            _animatorController.CheckAndCreateParameter("OSCm/BlendSet", AnimatorControllerParameterType.Float, 1f);

            var childBinary = new List<ChildMotion>();

            // Go through each parameter and create each child to eventually stuff into the Direct BlendTrees. 
            int i = 0;
            foreach (OSCmoothParameter p in _parameters)
            {
                EditorUtility.DisplayProgressBar("OSCmooth", "Creating Binary Parameter Direct BlendTree", (float)i++/_parameters.Count);
                if (p.binarySizeSelection == 0) continue;
                var decodeBinary = CreateBinaryBlendTree(p.paramName, p.binarySizeSelection, p.binaryNegative);

                childBinary.Add(new ChildMotion
                {
                    directBlendParameter = $"{oscmPrefix}{blendSuffix}",
                    motion = decodeBinary,
                    timeScale = 1
                });
            }

            binaryTreeRoot.children = childBinary.ToArray();

            AssetDatabase.AddObjectToAsset(binaryTreeRoot, _animatorPath);

            return binaryTreeRoot;
        }

        public string NameNoSymbol(string name)
        {
            string nameNoSym = "";

            for (int j = 0; j < name.Length; j++)
            {
                if (name[j] != '/')
                {
                    nameNoSym += name[j];
                }

            }
            return nameNoSym;
        }

        public void CleanAnimatorBlendTreeBloat(string filter)
        {
            var _animatorAssets = AssetDatabase.LoadAllAssetsAtPath(_animatorPath);
            foreach (Object asset in _animatorAssets)
            {
                if (asset?.GetType() == typeof(BlendTree) && asset != null)
                {
                    if (((BlendTree)asset).name.Contains(filter))
                    {
                        Object.DestroyImmediate(asset, true);
                    }
                }
            }
        }

        public void RenameAllStateMachineInstancesOfBlendParameter(string initParameter, string newParameter)
        {
            var _animatorAssets = AssetDatabase.LoadAllAssetsAtPath(_animatorPath);
            foreach (Object asset in _animatorAssets)
            {
                switch (asset)
                {
                    case BlendTree blendTree:
                        Debug.Log($"Rewriting parameter for:{blendTree.name} in {_animatorController.name} to {newParameter}");
                        if (blendTree.blendParameter == initParameter)
                            blendTree.blendParameter = newParameter;

                        if (blendTree.blendParameterY == initParameter)
                            blendTree.blendParameterY = newParameter;

                        var _children = blendTree.children;
                        for (int i = 0; i < blendTree.children.Length; i++)
                        {
                            if (_children[i].directBlendParameter == initParameter)
                                _children[i].directBlendParameter = newParameter;
                        }
                        blendTree.children = _children;
                        break;

                    case AnimatorState animatorState:
                        Debug.Log($"Rewriting parameter for:{animatorState.name} in {_animatorController.name} to {newParameter}");
                        if (animatorState.timeParameter == initParameter)
                            animatorState.timeParameter = newParameter;

                        if (animatorState.speedParameter == initParameter)
                            animatorState.speedParameter = newParameter;

                        if (animatorState.cycleOffsetParameter == initParameter)
                            animatorState.cycleOffsetParameter = newParameter;

                        if (animatorState.mirrorParameter == initParameter)
                            animatorState.mirrorParameter = newParameter;
                        break;
                }
            }
        }
        
        public List<string> GetAllStateMachineParameters()
        {
            var _animatorAssets = AssetDatabase.LoadAllAssetsAtPath(_animatorPath);
            List<string> stateParams = new List<string>();

            foreach (Object asset in _animatorAssets)
            {
                switch (asset)
                {
                    case BlendTree blendTree:
                        AddParameter(blendTree.blendParameter, stateParams);
                        AddParameter(blendTree.blendParameterY, stateParams);

                        for (int i = 0; i < blendTree.children.Length; i++)
                        {
                            AddParameter(blendTree.children[i].directBlendParameter, stateParams);
                        }

                        break;

                    case AnimatorState animatorState:
                        AddParameter(animatorState.timeParameter, stateParams);
                        AddParameter(animatorState.speedParameter, stateParams);
                        AddParameter(animatorState.cycleOffsetParameter, stateParams);
                        AddParameter(animatorState.mirrorParameter, stateParams);
                        break;
                }
            }

            return stateParams;
        }

        private static void AddParameter(string parameter, List<string> stateParams)
        {
            if (!string.IsNullOrEmpty(parameter)) 
                stateParams.Add(parameter);
        }

        public void RevertStateMachineParameters()
        {
            string[] stateParams = GetAllStateMachineParameters().ToArray();
            int i = 0;
            foreach (var oscmParam in Filters.ParameterExtensions)
            {
                EditorUtility.DisplayProgressBar("OSCmooth", "Removing Smoothing Direct BlendTree", (float)i++/oscmParam.Count());
                foreach (var stateParam in stateParams)
                {
                    if (stateParam.Contains(oscmParam))
                    {
                        RenameAllStateMachineInstancesOfBlendParameter(stateParam, stateParam.Replace(oscmParam, ""));
                    }
                }
            }
            EditorUtility.ClearProgressBar();
        }

        public void RemoveExtendedParametersInController(string name)
        {
            for (int i = 0; i < _animatorController.parameters.Length;)
            {
                EditorUtility.DisplayProgressBar("OSCmooth", "Removing Extra Parameters.", (float)i/_animatorController.parameters.Length);
                if (_animatorController.parameters[i].name.Contains(name))
                {
                    _animatorController.RemoveParameter(i);
                    continue;
                }
                i++;
            }
            EditorUtility.ClearProgressBar();
        }

        public void RemoveContainingLayersInController(string name)
        {
            for (int i = 0; i < _animatorController.layers.Length;)
            {
                EditorUtility.DisplayProgressBar("OSCmooth", "Removing Animation Layers.", (float)i/_animatorController.layers.Length);
                if (_animatorController.layers[i].name.Contains(name))
                {
                    _animatorController.RemoveLayer(i);
                    continue;
                }
                i++;
            }
            EditorUtility.ClearProgressBar();
        }

        public AnimatorControllerLayer CreateAnimLayerInController(string layerName)
        {
            for (int i = 0; i < _animatorController.layers.Length; i++)
            {
                if (_animatorController.layers[i].name == layerName)
                {
                    _animatorController.RemoveLayer(i);
                }
            }

            AnimatorControllerLayer layer = new AnimatorControllerLayer
            {
                name = layerName,
                stateMachine = new AnimatorStateMachine
                {
                    name = layerName,
                    hideFlags = HideFlags.HideInHierarchy
                },
                defaultWeight = 1f
            };


            // Store Layer into Animator Controller, as creating a Layer object is not serialized unless we store it inside an asset.
            AssetDatabase.AddObjectToAsset(layer.stateMachine, _animatorPath);

            _animatorController.AddLayer(layer);

            return layer;
        }

        public AnimationClip[] CreateFloatSmootherAnimation(string paramName, 
                                                            string smoothSuffix, 
                                                            string proxyPrefix, 
                                                            string directory, 
                                                            float initThreshold = -1, 
                                                            float finalThreshold = 1)
        {
            string baseName = NameNoSymbol(paramName);
            string initAssetPath = directory + baseName + "-1" + smoothSuffix + "_" + _animatorGUID + ".anim";
            string finalAssetPath = directory + baseName + "1" + smoothSuffix + "_" + _animatorGUID + ".anim";

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var curvesInit = new AnimationCurve(new Keyframe(0.0f, initThreshold));
            var curvesFinal = new AnimationCurve(new Keyframe(0.0f, finalThreshold));

            var _animationClipInit = FindOrCreateAnimationClip(initAssetPath, proxyPrefix + paramName, curvesInit);
            var _animationClipFinal = FindOrCreateAnimationClip(finalAssetPath, proxyPrefix + paramName, curvesFinal);

            return new AnimationClip[] { _animationClipInit, _animationClipFinal };
        }

        public BlendTree CreateSmoothingBlendTree(float smoothness,
                                                  string paramName,
                                                  string prefix)
        {
            var proxyPrefix = "OSCm/Proxy/";
            var smoothSuffix = "Smoother";

            _animatorController.CheckAndCreateParameter(prefix + paramName + smoothSuffix, AnimatorControllerParameterType.Float, smoothness);
            _animatorController.CheckAndCreateParameter(proxyPrefix + paramName, AnimatorControllerParameterType.Float);
            _animatorController.CheckAndCreateParameter(paramName, AnimatorControllerParameterType.Float);

            // Creating 3 blend trees to create the feedback loop
            BlendTree rootTree = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                blendParameter = prefix + paramName + "Smoother",
                name = "OSCm_" + paramName + " Root",
                useAutomaticThresholds = false
            };
            BlendTree falseTree = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                blendParameter = paramName,
                name = "OSCm_Input",
                useAutomaticThresholds = false
            };
            BlendTree trueTree = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                blendParameter = proxyPrefix + paramName,
                name = "OSCm_Driver",
                useAutomaticThresholds = false
            };

            // Create smoothing anims
            AnimationClip[] driverAnims = CreateFloatSmootherAnimation(paramName, smoothSuffix, proxyPrefix, _smoothExportDirectory);

            rootTree.AddChild(falseTree, 0f);
            rootTree.AddChild(trueTree, 1f);

            falseTree.AddChild(driverAnims[0], -1f);
            falseTree.AddChild(driverAnims[1], 1f);

            trueTree.AddChild(driverAnims[0], -1f);
            trueTree.AddChild(driverAnims[1], 1f);

            var controllerPath = _animatorPath;

            AssetDatabase.AddObjectToAsset(rootTree, controllerPath);
            AssetDatabase.AddObjectToAsset(falseTree, controllerPath);
            AssetDatabase.AddObjectToAsset(trueTree, controllerPath);

            return rootTree;
        }

        public BlendTree CreateBinaryBlendTree(string paramName, int binarySizeSelection, bool combinedParameter)
        {
            // Create each binary step decode layer. Expression Parameters are bools and are implicitly cast as floats in the animator.
            // This creates one monolithic animation layer with all of the binary conversion logic.

            string prefix = "OSCm/Binary/";

            var blendRootPara = "OSCm/BlendSet";
            if (combinedParameter)
            {
                _animatorController.CheckAndCreateParameter(prefix + paramName + "Negative", AnimatorControllerParameterType.Float);
                blendRootPara = prefix + paramName + "Negative";
            }

            BlendTree decodeBinaryRoot = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                blendParameter = blendRootPara,
                name = "OSCm_Binary_" + paramName + "_Root",
                useAutomaticThresholds = false
            };

            // Go through binary steps and create each child to eventually stuff into the Direct BlendTrees.
            for (int sign = combinedParameter ? -1 : 1; sign <= 1; sign += 2)
            {
                BlendTree decodeBinaryChildTree = new BlendTree
                {
                    blendType = BlendTreeType.Direct,
                    hideFlags = HideFlags.HideInHierarchy,
                    name = "OSCm_Binary_ + " + paramName + "_" + (sign < 0 ? "Negative" : "Positive") + "_" + _animatorGUID,
                    useAutomaticThresholds = false
                };

                List<ChildMotion> childBinaryDecode = new List<ChildMotion>();

                for (int i = 0; i < binarySizeSelection; i++)
                {
                    var decodeBinaryPositive = CreateBinaryDecode(paramName, _binaryExportDirectory, i, binarySizeSelection, sign <= 0);

                    childBinaryDecode.Add(new ChildMotion
                    {
                        directBlendParameter = "OSCm/BlendSet",
                        motion = decodeBinaryPositive,
                        timeScale = 1
                    });
                }

                decodeBinaryChildTree.children = childBinaryDecode.ToArray();
                decodeBinaryRoot.AddChild(decodeBinaryChildTree, sign >= 0 ? 0f : 1f);
                AssetDatabase.AddObjectToAsset(decodeBinaryChildTree, _animatorPath);
            }

            AssetDatabase.AddObjectToAsset(decodeBinaryRoot, _animatorPath);

            return decodeBinaryRoot;
        }

        public AnimationClip FindOrCreateAnimationClip(string directory, string paramName, AnimationCurve curve)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(directory);

            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, directory);
            }
            else clip.ClearCurves();

            clip.SetCurve("", typeof(Animator), paramName, curve);

            return clip;
        }

        public AnimationClip[] CreateBinaryAnimation(string paramName, string directory, float weight, int step)
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var trueNamePath = directory + NameNoSymbol(paramName) + "_True_" + step.ToString() + weight + _animatorGUID + ".anim";
            var falseNamePath = directory + NameNoSymbol(paramName) + "_False_" + step.ToString() + weight + _animatorGUID + ".anim";

            var _trueClip = FindOrCreateAnimationClip(trueNamePath, paramName, new AnimationCurve(new Keyframe(0.0f, 0.0f)));
            var _falseClip = FindOrCreateAnimationClip(falseNamePath, paramName, new AnimationCurve(new Keyframe(0.0f, weight)));

            return new AnimationClip[] { _trueClip, _falseClip };
        }

        public BlendTree CreateBinaryDecode(string paramName, string directory, int binaryPow, int binarySize, bool negative)
        {
            string prefix = "OSCm/Binary/";
            float binaryPowValue = Mathf.Pow(2, binaryPow);
            _animatorController.CheckAndCreateParameter(prefix + paramName + binaryPowValue, AnimatorControllerParameterType.Float);

            BlendTree decodeBinary = new BlendTree
            {
                blendType = BlendTreeType.Simple1D,
                hideFlags = HideFlags.HideInHierarchy,
                blendParameter = prefix + paramName + (int)binaryPowValue,
                name = "Binary_" + paramName + "_Decode_" + binaryPowValue,
                useAutomaticThresholds = false
            };

            // Create Decode anims and weight per binary
            AnimationClip[] decodeAnims = CreateBinaryAnimation(paramName, directory, (negative ? -1f : 1f) * binaryPowValue / (Mathf.Pow(2, binarySize) - 1f), binaryPow);
            decodeBinary.AddChild(decodeAnims[0], 0f);
            decodeBinary.AddChild(decodeAnims[1], 1f);

            AssetDatabase.AddObjectToAsset(decodeBinary, _animatorPath);

            return decodeBinary;
        }
    }
}