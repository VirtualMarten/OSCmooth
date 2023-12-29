﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using UnityEditor.Animations;
using OSCmooth.Types;
using static OSCmooth.Filters;

namespace OSCmooth.Util
{
    public static class ParameterExtensions
    {
        private static List<VRCExpressionParameters.Parameter> _parameters = new List<VRCExpressionParameters.Parameter>();

        private static VRCExpressionParameters.Parameter[] BakeAndClearParameters(List<VRCExpressionParameters.Parameter> paramsToAppend = null)
        {
            if (paramsToAppend != null)
                _parameters.AddRange(paramsToAppend);
            var _bakedParameters = _parameters.ToArray();
            _parameters.Clear();
            return _bakedParameters;
        }

        private static void AddParameter(string name, VRCExpressionParameters.ValueType type, bool synced) =>
            _parameters.Add(new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = 0f,
                saved = false,
                networkSynced = synced
            });

        public static bool CreateExpressionParameters(this OSCmoothSetup setup, VRCAvatarDescriptor avatarDescriptor, string directory)
        {
            var isEncoding = setup.configParam.binaryEncoding;
            var _oscmParameters = setup.parameters;
            int _oscmCost = _oscmParameters.ParameterCost();
            int _descCost = avatarDescriptor.expressionParameters.CalcAvailableSpace();
            if (_oscmCost > _descCost)
            {
                EditorUtility.DisplayDialog("OSCmooth", $"OSCmooth parameters take up too much Expression Parameter space on your avatar ({_oscmCost} used / {_descCost} available). Reduce the parameter usage of OSCmooth to upload.", "OK");
                return false;
            }

            var _vrcParameters = avatarDescriptor.expressionParameters.parameters.ToList();

            foreach (OSCmoothParameter oscmParam in _oscmParameters)
            {
                var _vrcParameterMatch = _vrcParameters.FirstOrDefault(p => p.name == oscmParam.paramName);

                if (oscmParam.binarySizeSelection != 0 && oscmParam.binarySizeSelection <= 7)
                {
                    if (_vrcParameterMatch.name == oscmParam.paramName)
                        _vrcParameters.Remove(_vrcParameterMatch);
                    if (isEncoding)
                        AddParameter(oscmParam.paramName,
                                     VRCExpressionParameters.ValueType.Float,
                                     false);


                    var binaryName = $"{oscmPrefix}{binaryPrefix}{oscmParam.paramName}";
                    for (int binarySize = 0; binarySize < oscmParam.binarySizeSelection; binarySize++)
                    {
                        AddParameter(Obfuscate($"{binaryName}{1 << binarySize}"),
                                     VRCExpressionParameters.ValueType.Bool,
                                     true);
                    }
                    if (oscmParam.binaryNegative)
                    {
                        AddParameter(Obfuscate($"{binaryName}Negative"),
                                     VRCExpressionParameters.ValueType.Bool,
                                     true);
                    }
                }
                else if (_vrcParameterMatch.name == oscmParam.paramName)
                    continue;
                else
                    AddParameter(oscmParam.paramName, 
                                 VRCExpressionParameters.ValueType.Float, 
                                 true);
            }
            var _expressionsPath = AssetDatabase.GetAssetPath(avatarDescriptor.expressionParameters);
            var _proxyPath = $"{directory}{avatarDescriptor.expressionParameters.name}Proxy.asset"; 
            AssetDatabase.CopyAsset(_expressionsPath, _proxyPath);
            AssetDatabase.SaveAssets();
            var _proxyExpressions = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(_proxyPath);
            _proxyExpressions.parameters = BakeAndClearParameters(_vrcParameters);

            avatarDescriptor.expressionParameters = _proxyExpressions;
            AssetDatabase.SaveAssets();
            return true;
        }

        public static int ParameterCost(this List<OSCmoothParameter> oscmParameters)
        {
            int _oscmUsage = 0;
            oscmParameters.ForEach(delegate (OSCmoothParameter p)
            {
                _oscmUsage += (p.binarySizeSelection > 0) ? p.binarySizeSelection : VRCExpressionParameters.TypeCost(VRCExpressionParameters.ValueType.Float);
            });
            return _oscmUsage;
        }

        public static void RemoveOSCmParameters(this VRCAvatarDescriptor avatarDescriptor, List<OSCmoothParameter> oscmParameters)
        {
            var vrcParameters = avatarDescriptor.expressionParameters.parameters.ToList();
            var toRemove = new List<VRCExpressionParameters.Parameter>();
            foreach (OSCmoothParameter oscmParameter in oscmParameters)
            {
                foreach (VRCExpressionParameters.Parameter vrcParameter in vrcParameters)
                {
                    if (vrcParameter.name.Contains(oscmParameter.paramName))
                    {
                        toRemove.Add(vrcParameter);
                    }
                }
            }
            avatarDescriptor.expressionParameters.parameters = vrcParameters.Except(toRemove).ToArray();
        }

        public static int CalcAvailableSpace(this VRCExpressionParameters parameters)
        {
            return (int)Mathf.Max(0, 256 - parameters.CalcTotalCost());
        }

        private static HashSet<string> existingParameterNames;

        public static string Obfuscate(string name)
        {
            // Simple Ceasar cypher.
            var shift = 16;
            var maxChar = Convert.ToInt32(char.MaxValue);
            var minChar = Convert.ToInt32(char.MinValue);

            var buffer = name.ToCharArray();
            for (var i = 0; i < buffer.Length; i++)
            {
                var shifted = Convert.ToInt32(buffer[i]) + shift;

                if (shifted > maxChar)
                {
                    shifted -= maxChar;
                }
                else if (shifted < minChar)
                {
                    shifted += maxChar;
                }
                buffer[i] = Convert.ToChar(shifted);
            }

            return new string(buffer);
        }

        public static AnimatorControllerParameter CreateParameter(this AnimatorController animatorController,
                                                                  HashSet<string> existingParameterNames,                                            
                                                                  string paramName, 
                                                                  AnimatorControllerParameterType type, 
                                                                  bool checkForExisting,
                                                                  bool obfuscate,
                                                                  float defaultVal = 0f)
        {
            var _paramName = obfuscate ? Obfuscate(paramName) : paramName;
            if (checkForExisting)
            {
                if (existingParameterNames.Contains(_paramName))
                {
                    return animatorController.parameters.First((AnimatorControllerParameter p) => p.name == _paramName);
                }
            }
            AnimatorControllerParameter parameter = new AnimatorControllerParameter
            {
                name = _paramName,
                type = type,
                defaultFloat = defaultVal,
                defaultInt = (int)defaultVal,
                defaultBool = Convert.ToBoolean(defaultVal)
            };
            animatorController.AddParameter(parameter);
            existingParameterNames.Add(_paramName);
            return parameter;
        }
    }
}
