#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

namespace FigmaImporter.Editor
{
    /// <summary>
    /// Core logic for building Unity UGUI prefabs from Figma export data.
    /// Full feature parity with the original FigmaUgUiImporter.
    /// </summary>
    public class FigmaNodeBuilder
    {
        // Settings
        public float PixelsPerUnit { get; set; } = 100f;
        public bool RemoveOrphans { get; set; } = true;
        public bool CreateCanvasIfMissing { get; set; } = true;
        public bool CreateEventSystemIfMissing { get; set; } = true;
        public bool AutoSetupScreenManager { get; set; } = true;
        public bool CreateEnvironmentPrefab { get; set; } = false;
        public bool CreateTransitionPrefab { get; set; } = false;
        public bool CreateVideoPlane { get; set; } = false;
        public bool AddEntryAnimations { get; set; } = true;
        public float AnimationStaggerDelay { get; set; } = 0.05f;

        // Component prefab cache (componentId → prefab asset)
        private Dictionary<string, GameObject> componentPrefabs;

        // Variant component set lookup (setId → ExportComponentSet)
        private Dictionary<string, ExportComponentSet> componentSetLookup;

        // Variant component set prefab cache (setId → prefab asset)
        private Dictionary<string, GameObject> componentSetPrefabs;

        private const string SpacerPrefix = "__SPACER__";

        public void Import(ExportBundle data, string rootFolder)
        {
            var imagesFolder = rootFolder + "/" + SafeName(data.frameName) + "_Images";

            string prefabPath;
            if (AutoSetupScreenManager)
            {
                EnsureFolder("Assets/Resources");
                EnsureFolder("Assets/Resources/Screens");
                prefabPath = "Assets/Resources/Screens/" + data.frameName + ".prefab";
            }
            else
            {
                prefabPath = rootFolder + "/" + SafeName(data.frameName) + "_UGUI.prefab";
            }

            // Collect 9-slice data for sprite border configuration
            var nineSliceData = new Dictionary<string, ExportNineSlice>();
            CollectNineSliceData(data.nodes, nineSliceData);

            EnsureSpritesImported(imagesFolder, PixelsPerUnit, nineSliceData);

            if (CreateEventSystemIfMissing)
                EnsureEventSystemInScene();

            GameObject rootInstance;
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (existingPrefab != null)
                rootInstance = (GameObject)PrefabUtility.InstantiatePrefab(existingPrefab);
            else
            {
                rootInstance = new GameObject(SafeName(data.frameName) + "_UGUI", typeof(RectTransform));
                if (CreateCanvasIfMissing)
                {
                    rootInstance.AddComponent<Canvas>();
                    rootInstance.AddComponent<CanvasScaler>();
                    rootInstance.AddComponent<GraphicRaycaster>();
                }
            }

            // Canvas config
            var canvas = rootInstance.GetComponent<Canvas>();
            var scaler = rootInstance.GetComponent<CanvasScaler>();

            if (canvas != null)
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(data.width, data.height);
                scaler.matchWidthOrHeight = 0.5f;
            }

            var rootRt = rootInstance.GetComponent<RectTransform>();
            var screenRoot = GetOrCreateScreenRoot(rootRt, data.width, data.height);

            // Get existing prefabs before building (for orphan cleanup)
            var prefabFolder = rootFolder + "/Prefabs";
            var existingPrefabs = new HashSet<string>();
            if (AssetDatabase.IsValidFolder(prefabFolder))
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
                foreach (var guid in guids)
                    existingPrefabs.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            // Build component prefabs first (Figma Components → Unity Prefabs)
            var createdPrefabs = new HashSet<string>();
            componentPrefabs = BuildComponentPrefabs(data.components, rootFolder, createdPrefabs);

            // Build variant component set lookup
            componentSetLookup = new Dictionary<string, ExportComponentSet>();
            if (data.componentSets != null)
            {
                foreach (var cs in data.componentSets)
                {
                    if (cs != null && !string.IsNullOrEmpty(cs.id))
                        componentSetLookup[cs.id] = cs;
                }
            }

            // Build variant component set prefabs
            componentSetPrefabs = BuildComponentSetPrefabs(data.componentSets, rootFolder, createdPrefabs);

            // Remove orphaned prefabs (components deleted in Figma)
            if (RemoveOrphans)
            {
                foreach (var oldPrefab in existingPrefabs)
                {
                    if (!createdPrefabs.Contains(oldPrefab))
                    {
                        Debug.Log($"Figma: Removing orphaned prefab: {oldPrefab}");
                        AssetDatabase.DeleteAsset(oldPrefab);
                    }
                }
            }

            var existing = IndexExisting(rootInstance);
            var touched = new HashSet<string>();

            SyncNode(
                node: data.nodes,
                parent: screenRoot,
                rootFolder: rootFolder,
                existing: existing,
                touched: touched,
                parentAbsX: 0f,
                parentAbsY: 0f,
                parentUsesLayout: false,
                parentW: data.width,
                parentH: data.height
            );

            // The root frame must stretch to fill ScreenRoot
            if (existing.TryGetValue(data.nodes.id, out var rootFrameRt) && rootFrameRt != null)
            {
                rootFrameRt.anchorMin = Vector2.zero;
                rootFrameRt.anchorMax = Vector2.one;
                rootFrameRt.pivot = new Vector2(0.5f, 0.5f);
                rootFrameRt.offsetMin = Vector2.zero;
                rootFrameRt.offsetMax = Vector2.zero;
            }

            if (RemoveOrphans)
                RemoveOrphaned(existing, touched);

            PrefabUtility.SaveAsPrefabAsset(rootInstance, prefabPath);
            UnityEngine.Object.DestroyImmediate(rootInstance);

            // Create 3D environment prefab if requested
            if (CreateEnvironmentPrefab)
                EnsureEnvironmentPrefab(data.frameName);

            // Create screen transition prefab if requested
            if (CreateTransitionPrefab)
                EnsureTransitionPrefab(data.frameName);

            // Create video plane for AI video if requested
            if (CreateVideoPlane)
                EnsureVideoPlane(data.frameName, data.width, data.height);

            // Auto-setup UIScreenManager
            if (AutoSetupScreenManager)
                EnsureUIScreenManagerInScene(data.frameName);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Figma: Built prefab at {prefabPath}");
            if (CreateEnvironmentPrefab)
                Debug.Log($"Figma: Environment prefab at Assets/Resources/Environments/{data.frameName}_Environment.prefab");
            if (CreateTransitionPrefab)
                Debug.Log($"Figma: Transition prefab at Assets/Resources/Transitions/{data.frameName}_Transition.prefab");
            if (CreateVideoPlane)
                Debug.Log($"Figma: Video plane prefab at Assets/Resources/VideoPlanes/{data.frameName}_VideoPlane.prefab");

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        // ==================== Core Node Sync ====================

        private void SyncNode(
            ExportNode node,
            RectTransform parent,
            string rootFolder,
            Dictionary<string, RectTransform> existing,
            HashSet<string> touched,
            float parentAbsX,
            float parentAbsY,
            bool parentUsesLayout,
            float parentW,
            float parentH)
        {
            // --- Component instance: use nested prefab for containers only ---
            if (node.kind != "image" &&
                string.IsNullOrEmpty(node.componentSetId) &&
                (node.isInstance || node.isComponent) &&
                !string.IsNullOrEmpty(node.componentId) &&
                componentPrefabs != null &&
                componentPrefabs.TryGetValue(node.componentId, out var compPrefab) &&
                compPrefab != null)
            {
                SyncInstanceNode(node, parent, existing, touched,
                    parentAbsX, parentAbsY, parentUsesLayout, parentW, parentH, compPrefab);
                return;
            }

            // --- Variant component set instance: use nested prefab ---
            if (node.kind != "image" &&
                !string.IsNullOrEmpty(node.componentSetId) &&
                componentSetPrefabs != null &&
                componentSetPrefabs.TryGetValue(node.componentSetId, out var csPrefab) &&
                csPrefab != null)
            {
                SyncVariantInstanceNode(node, parent, existing, touched,
                    parentAbsX, parentAbsY, parentUsesLayout, parentW, parentH,
                    csPrefab, node.defaultStateKey);
                return;
            }

            // --- Normal (non-instance) path ---
            RectTransform rt;
            bool isNewNode = false;

            if (existing.TryGetValue(node.id, out rt) && rt != null)
            {
                if (rt.parent != parent) rt.SetParent(parent, false);
            }
            else
            {
                isNewNode = true;
                var go = new GameObject(string.IsNullOrEmpty(node.name) ? node.id : node.name, typeof(RectTransform), typeof(FigmaNodeKey));
                rt = go.GetComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.GetComponent<FigmaNodeKey>().Id = node.id;
                existing[node.id] = rt;
            }

            touched.Add(node.id);

            rt.localRotation = Quaternion.Euler(0, 0, node.rotation);
            rt.gameObject.SetActive(node.visible);

            // Coordinates are always relative to the immediate parent
            bool isAbsoluteInLayout = parentUsesLayout &&
                !string.IsNullOrEmpty(node.layoutPositioning) &&
                node.layoutPositioning.Equals("ABSOLUTE", StringComparison.OrdinalIgnoreCase);

            if (!parentUsesLayout || isAbsoluteInLayout)
            {
                float localX = node.x - parentAbsX;
                float localY = node.y - parentAbsY;
                ApplyConstraintAnchoring(rt, node, localX, localY, parentW, parentH);
            }
            else
            {
                SetTopLeftAnchored(rt);
                rt.sizeDelta = new Vector2(node.w, node.h);
                rt.anchoredPosition = Vector2.zero;
                EnsureLayoutElementForLayoutChild(rt, node);
            }

            bool thisUsesLayout = node.layout != null &&
                (node.layout.direction == "row" || node.layout.direction == "column");

            if (thisUsesLayout)
            {
                UpsertLayoutGroup(rt, node.layout);

                if (IsSpaceBetween(node.layout.alignPrimary))
                    EnsureSpacers(rt, node.children);
                else
                    RemoveSpacers(rt);
            }
            else
            {
                RemoveSpacers(rt);
            }

            if (node.kind == "9slice" && node.image != null && !string.IsNullOrEmpty(node.image.file) && node.nineSlice != null)
                UpsertSlicedImage(rt, rootFolder + "/" + node.image.file, node.nineSlice);
            else if (node.kind == "image" && node.image != null && !string.IsNullOrEmpty(node.image.file))
                UpsertImage(rt, rootFolder + "/" + node.image.file);

            if (!string.IsNullOrEmpty(node.name) && node.name.StartsWith("BTN_", StringComparison.OrdinalIgnoreCase))
            {
                EnsureButton(rt);
                if (isNewNode)
                {
                    var pretty = node.name.Length > 4 ? node.name.Substring(4) : node.name;
                    if (!string.IsNullOrEmpty(pretty)) rt.gameObject.name = pretty;
                }
            }

            // Auto-wire prototype navigation
            if (node.interactions != null && node.interactions.Count > 0)
            {
                TryAutoWireNavigation(rt, node.interactions, isNewNode);
            }

            if (node.kind == "text" && node.text != null)
                UpsertText(rt, node.text);

            // Add entry animation if enabled
            if (AddEntryAnimations && isNewNode)
                TryAddEntryAnimation(rt, node.name, node.kind);

            // Variant instance — build variant children instead of normal recursion
            if (!string.IsNullOrEmpty(node.componentSetId) &&
                componentSetLookup != null &&
                componentSetLookup.TryGetValue(node.componentSetId, out var compSet) &&
                compSet.variants != null && compSet.variants.Count > 0)
            {
                BuildVariantChildren(rt, compSet, node.defaultStateKey, rootFolder, existing, touched);
                return;
            }

            if (node.children != null && node.children.Count > 0)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    SyncNode(
                        node: node.children[i],
                        parent: rt,
                        rootFolder: rootFolder,
                        existing: existing,
                        touched: touched,
                        parentAbsX: 0f,
                        parentAbsY: 0f,
                        parentUsesLayout: thisUsesLayout,
                        parentW: node.w,
                        parentH: node.h
                    );
                }

                if (thisUsesLayout && IsSpaceBetween(node.layout.alignPrimary))
                    ApplySpaceBetweenSiblingOrder(rt, node.children, existing);
                else
                {
                    for (int i = 0; i < node.children.Count; i++)
                    {
                        if (existing.TryGetValue(node.children[i].id, out var childRt) && childRt != null)
                            childRt.SetSiblingIndex(i);
                    }
                }
            }
        }

        // ==================== Component Instance Support ====================

        private void SyncInstanceNode(
            ExportNode node,
            RectTransform parent,
            Dictionary<string, RectTransform> existing,
            HashSet<string> touched,
            float parentAbsX, float parentAbsY,
            bool parentUsesLayout,
            float parentW, float parentH,
            GameObject compPrefab)
        {
            RectTransform rt = null;
            bool needsCreate = true;

            if (existing.TryGetValue(node.id, out rt) && rt != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(rt.gameObject);
                if (source == compPrefab)
                {
                    if (rt.parent != parent) rt.SetParent(parent, false);
                    needsCreate = false;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(rt.gameObject);
                    existing.Remove(node.id);
                    rt = null;
                }
            }

            if (needsCreate)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(compPrefab);
                rt = instance.GetComponent<RectTransform>();
                rt.SetParent(parent, false);

                var key = rt.GetComponent<FigmaNodeKey>();
                if (key == null) key = rt.gameObject.AddComponent<FigmaNodeKey>();
                key.Id = node.id;

                existing[node.id] = rt;
            }

            touched.Add(node.id);

            rt.localRotation = Quaternion.Euler(0, 0, node.rotation);
            rt.gameObject.SetActive(node.visible);

            bool isAbsoluteInLayout = parentUsesLayout &&
                !string.IsNullOrEmpty(node.layoutPositioning) &&
                node.layoutPositioning.Equals("ABSOLUTE", StringComparison.OrdinalIgnoreCase);

            if (!parentUsesLayout || isAbsoluteInLayout)
            {
                float localX = node.x - parentAbsX;
                float localY = node.y - parentAbsY;
                ApplyConstraintAnchoring(rt, node, localX, localY, parentW, parentH);
            }
            else
            {
                SetTopLeftAnchored(rt);
                rt.sizeDelta = new Vector2(node.w, node.h);
                rt.anchoredPosition = Vector2.zero;
                EnsureLayoutElementForLayoutChild(rt, node);
            }
        }

        // ==================== Variant Component Set Instance Support ====================

        private void SyncVariantInstanceNode(
            ExportNode node,
            RectTransform parent,
            Dictionary<string, RectTransform> existing,
            HashSet<string> touched,
            float parentAbsX, float parentAbsY,
            bool parentUsesLayout,
            float parentW, float parentH,
            GameObject csPrefab,
            string defaultStateKey)
        {
            RectTransform rt = null;
            bool needsCreate = true;

            if (existing.TryGetValue(node.id, out rt) && rt != null)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(rt.gameObject);
                if (source == csPrefab)
                {
                    if (rt.parent != parent) rt.SetParent(parent, false);
                    needsCreate = false;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(rt.gameObject);
                    existing.Remove(node.id);
                    rt = null;
                }
            }

            if (needsCreate)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(csPrefab);
                rt = instance.GetComponent<RectTransform>();
                rt.SetParent(parent, false);

                var key = rt.GetComponent<FigmaNodeKey>();
                if (key == null) key = rt.gameObject.AddComponent<FigmaNodeKey>();
                key.Id = node.id;

                existing[node.id] = rt;
            }

            touched.Add(node.id);

            rt.localRotation = Quaternion.Euler(0, 0, node.rotation);
            rt.gameObject.SetActive(node.visible);

            bool isAbsoluteInLayout = parentUsesLayout &&
                !string.IsNullOrEmpty(node.layoutPositioning) &&
                node.layoutPositioning.Equals("ABSOLUTE", StringComparison.OrdinalIgnoreCase);

            if (!parentUsesLayout || isAbsoluteInLayout)
            {
                float localX = node.x - parentAbsX;
                float localY = node.y - parentAbsY;
                ApplyConstraintAnchoring(rt, node, localX, localY, parentW, parentH);
            }
            else
            {
                SetTopLeftAnchored(rt);
                rt.sizeDelta = new Vector2(node.w, node.h);
                rt.anchoredPosition = Vector2.zero;
                EnsureLayoutElementForLayoutChild(rt, node);
            }

            // Override defaultStateKey and activate the correct variant child
            var controller = rt.GetComponent<FigmaVariantController>();
            if (controller != null && !string.IsNullOrEmpty(defaultStateKey))
            {
                controller.defaultStateKey = defaultStateKey;

                for (int i = 0; i < controller.stateKeys.Length && i < controller.stateChildren.Length; i++)
                {
                    if (controller.stateChildren[i] != null)
                        controller.stateChildren[i].SetActive(controller.stateKeys[i] == defaultStateKey);
                }
            }
        }

        // ==================== Variant Children Building ====================

        private void BuildVariantChildren(
            RectTransform instanceRt,
            ExportComponentSet compSet,
            string defaultStateKey,
            string rootFolder,
            Dictionary<string, RectTransform> existing,
            HashSet<string> touched)
        {
            var controller = instanceRt.GetComponent<FigmaVariantController>();
            if (controller == null)
                controller = instanceRt.gameObject.AddComponent<FigmaVariantController>();

            var stateKeys = new List<string>();
            var stateChildren = new List<GameObject>();
            var instanceId = instanceRt.GetComponent<FigmaNodeKey>().Id;

            for (int i = 0; i < compSet.variants.Count; i++)
            {
                var variant = compSet.variants[i];
                var stateKey = !string.IsNullOrEmpty(variant.variantStateKey)
                    ? variant.variantStateKey
                    : variant.name;

                var syntheticId = instanceId + "::variant::" + stateKey;

                RectTransform variantRt;
                if (existing.TryGetValue(syntheticId, out variantRt) && variantRt != null)
                {
                    if (variantRt.parent != instanceRt)
                        variantRt.SetParent(instanceRt, false);
                }
                else
                {
                    var go = new GameObject(stateKey, typeof(RectTransform), typeof(FigmaNodeKey));
                    variantRt = go.GetComponent<RectTransform>();
                    variantRt.SetParent(instanceRt, false);
                    variantRt.GetComponent<FigmaNodeKey>().Id = syntheticId;
                    existing[syntheticId] = variantRt;
                }

                touched.Add(syntheticId);

                variantRt.anchorMin = Vector2.zero;
                variantRt.anchorMax = Vector2.one;
                variantRt.offsetMin = Vector2.zero;
                variantRt.offsetMax = Vector2.zero;
                variantRt.pivot = new Vector2(0.5f, 0.5f);
                variantRt.localScale = Vector3.one;

                bool isDefault = stateKey == defaultStateKey;
                variantRt.gameObject.SetActive(isDefault);

                bool variantUsesLayout = variant.layout != null &&
                    (variant.layout.direction == "row" || variant.layout.direction == "column");
                if (variantUsesLayout)
                    UpsertLayoutGroup(variantRt, variant.layout);

                if (variant.children != null)
                {
                    var idPrefix = instanceId + "::";
                    for (int c = 0; c < variant.children.Count; c++)
                    {
                        var prefixedChild = CloneNodeWithIdPrefix(variant.children[c], idPrefix);
                        SyncNode(
                            node: prefixedChild,
                            parent: variantRt,
                            rootFolder: rootFolder,
                            existing: existing,
                            touched: touched,
                            parentAbsX: 0f,
                            parentAbsY: 0f,
                            parentUsesLayout: variantUsesLayout,
                            parentW: variant.w,
                            parentH: variant.h
                        );
                    }
                }

                stateKeys.Add(stateKey);
                stateChildren.Add(variantRt.gameObject);
            }

            controller.stateKeys = stateKeys.ToArray();
            controller.stateChildren = stateChildren.ToArray();
            controller.defaultStateKey = defaultStateKey;

            TryAutoWireButtonStates(instanceRt, controller, compSet);
        }

        // ==================== Node Cloning ====================

        private static ExportNode CloneNodeWithIdPrefix(ExportNode source, string prefix)
        {
            var clone = new ExportNode();
            clone.id = prefix + source.id;
            clone.name = source.name;
            clone.kind = source.kind;
            clone.x = source.x;
            clone.y = source.y;
            clone.w = source.w;
            clone.h = source.h;
            clone.rotation = source.rotation;
            clone.opacity = source.opacity;
            clone.visible = source.visible;
            clone.layout = source.layout;
            clone.child = source.child;
            clone.image = source.image;
            clone.text = source.text;
            clone.constraints = source.constraints;
            clone.sizing = source.sizing;
            clone.layoutGrow = source.layoutGrow;
            clone.isComponent = source.isComponent;
            clone.isInstance = source.isInstance;
            clone.componentId = source.componentId;
            clone.layoutPositioning = source.layoutPositioning;
            clone.componentSetId = source.componentSetId;
            clone.defaultStateKey = source.defaultStateKey;
            clone.variantStateKey = source.variantStateKey;
            clone.interactions = source.interactions;
            clone.nineSlice = source.nineSlice;

            if (source.children != null)
            {
                clone.children = new List<ExportNode>();
                foreach (var child in source.children)
                    clone.children.Add(CloneNodeWithIdPrefix(child, prefix));
            }

            return clone;
        }

        // ==================== Button State Auto-Wiring ====================

        private static readonly HashSet<string> ButtonStateValues = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) { "default", "normal", "hover", "highlighted", "pressed", "disabled" };

        private static void TryAutoWireButtonStates(
            RectTransform instanceRt,
            FigmaVariantController controller,
            ExportComponentSet compSet)
        {
            if (compSet.variants == null || compSet.variants.Count == 0) return;

            bool hasStateProperty = false;
            bool allMatchButtonStates = true;

            foreach (var variant in compSet.variants)
            {
                var key = variant.variantStateKey ?? "";
                foreach (var part in key.Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith("State=", StringComparison.OrdinalIgnoreCase))
                    {
                        hasStateProperty = true;
                        var stateVal = trimmed.Substring(6).Trim();
                        if (!ButtonStateValues.Contains(stateVal))
                            allMatchButtonStates = false;
                    }
                }
            }

            if (!hasStateProperty || !allMatchButtonStates) return;

            var wirer = instanceRt.GetComponent<FigmaButtonStateWirer>();
            if (wirer == null)
                wirer = instanceRt.gameObject.AddComponent<FigmaButtonStateWirer>();

            var img = instanceRt.GetComponent<Image>();
            if (img == null)
                img = instanceRt.gameObject.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0);
            img.raycastTarget = true;

            var btn = instanceRt.GetComponent<Button>();
            if (btn == null)
            {
                btn = instanceRt.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = img;
            }

            wirer.controller = controller;

            foreach (var variant in compSet.variants)
            {
                var key = variant.variantStateKey ?? "";
                foreach (var part in key.Split(','))
                {
                    var trimmed = part.Trim();
                    if (!trimmed.StartsWith("State=", StringComparison.OrdinalIgnoreCase)) continue;

                    var stateVal = trimmed.Substring(6).Trim().ToLowerInvariant();
                    var fullKey = variant.variantStateKey;

                    switch (stateVal)
                    {
                        case "default": case "normal":
                            wirer.normalStateKey = fullKey; break;
                        case "hover": case "highlighted":
                            wirer.hoverStateKey = fullKey; break;
                        case "pressed":
                            wirer.pressedStateKey = fullKey; break;
                        case "disabled":
                            wirer.disabledStateKey = fullKey; break;
                    }
                }
            }
        }

        // ==================== Component Prefab Building ====================

        private Dictionary<string, GameObject> BuildComponentPrefabs(
            List<ExportNode> components, string rootFolder, HashSet<string> createdPrefabs)
        {
            var result = new Dictionary<string, GameObject>();
            if (components == null || components.Count == 0)
                return result;

            var compFolder = rootFolder + "/Prefabs";
            EnsureFolder(compFolder);

            foreach (var comp in components)
            {
                if (comp == null || string.IsNullOrEmpty(comp.componentId))
                    continue;

                // Build the component subtree into a temp hierarchy
                // Canvas is required for TextMeshProUGUI to work
                var tempParent = new GameObject("__TEMP_COMP__", typeof(RectTransform), typeof(Canvas));
                var tempRt = tempParent.GetComponent<RectTransform>();

                var compExisting = new Dictionary<string, RectTransform>();
                var compTouched = new HashSet<string>();

                // Temporarily disable component prefab lookup to avoid circular refs
                var savedPrefabs = componentPrefabs;
                componentPrefabs = null;

                SyncNode(
                    node: comp,
                    parent: tempRt,
                    rootFolder: rootFolder,
                    existing: compExisting,
                    touched: compTouched,
                    parentAbsX: 0f,
                    parentAbsY: 0f,
                    parentUsesLayout: false,
                    parentW: comp.w,
                    parentH: comp.h
                );

                componentPrefabs = savedPrefabs;

                if (tempRt.childCount > 0)
                {
                    var compGo = tempRt.GetChild(0).gameObject;
                    compGo.transform.SetParent(null, false);

                    var compGoRt = compGo.GetComponent<RectTransform>();
                    SetTopLeftAnchored(compGoRt);
                    compGoRt.anchoredPosition = Vector2.zero;

                    var safeName = SafeName(comp.name ?? comp.id);
                    var prefabPath = compFolder + "/" + safeName + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(compGo, prefabPath);
                    createdPrefabs.Add(prefabPath);

                    var savedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (savedAsset != null)
                        result[comp.componentId] = savedAsset;

                    UnityEngine.Object.DestroyImmediate(compGo);
                }

                UnityEngine.Object.DestroyImmediate(tempParent);
            }

            return result;
        }

        private Dictionary<string, GameObject> BuildComponentSetPrefabs(
            List<ExportComponentSet> componentSets, string rootFolder, HashSet<string> createdPrefabs)
        {
            var result = new Dictionary<string, GameObject>();
            if (componentSets == null || componentSets.Count == 0)
                return result;

            var compFolder = rootFolder + "/Prefabs";
            EnsureFolder(compFolder);

            foreach (var cs in componentSets)
            {
                if (cs == null || string.IsNullOrEmpty(cs.id) || cs.variants == null || cs.variants.Count == 0)
                    continue;

                // Create root GameObject for the component set prefab
                var safeName = SafeName(cs.name ?? cs.id);
                var rootGo = new GameObject(safeName, typeof(RectTransform), typeof(Canvas), typeof(FigmaNodeKey), typeof(FigmaVariantController));
                var rootRt = rootGo.GetComponent<RectTransform>();
                rootGo.GetComponent<FigmaNodeKey>().Id = cs.id;

                SetTopLeftAnchored(rootRt);

                var controller = rootGo.GetComponent<FigmaVariantController>();
                var stateKeys = new List<string>();
                var stateChildren = new List<GameObject>();

                for (int i = 0; i < cs.variants.Count; i++)
                {
                    var variant = cs.variants[i];
                    var stateKey = !string.IsNullOrEmpty(variant.variantStateKey)
                        ? variant.variantStateKey
                        : variant.name;

                    var variantGo = new GameObject(stateKey, typeof(RectTransform), typeof(FigmaNodeKey));
                    var variantRt = variantGo.GetComponent<RectTransform>();
                    variantRt.SetParent(rootRt, false);
                    variantGo.GetComponent<FigmaNodeKey>().Id = cs.id + "::variant::" + stateKey;

                    variantRt.anchorMin = Vector2.zero;
                    variantRt.anchorMax = Vector2.one;
                    variantRt.offsetMin = Vector2.zero;
                    variantRt.offsetMax = Vector2.zero;
                    variantRt.pivot = new Vector2(0.5f, 0.5f);
                    variantRt.localScale = Vector3.one;

                    variantGo.SetActive(i == 0);

                    bool variantUsesLayout = variant.layout != null &&
                        (variant.layout.direction == "row" || variant.layout.direction == "column");
                    if (variantUsesLayout)
                        UpsertLayoutGroup(variantRt, variant.layout);

                    if (variant.children != null)
                    {
                        var compExisting = new Dictionary<string, RectTransform>();
                        var compTouched = new HashSet<string>();

                        var savedPrefabs = componentPrefabs;
                        var savedCsPrefabs = componentSetPrefabs;
                        componentPrefabs = null;
                        componentSetPrefabs = null;

                        for (int c = 0; c < variant.children.Count; c++)
                        {
                            SyncNode(
                                node: variant.children[c],
                                parent: variantRt,
                                rootFolder: rootFolder,
                                existing: compExisting,
                                touched: compTouched,
                                parentAbsX: 0f,
                                parentAbsY: 0f,
                                parentUsesLayout: variantUsesLayout,
                                parentW: variant.w,
                                parentH: variant.h
                            );
                        }

                        componentPrefabs = savedPrefabs;
                        componentSetPrefabs = savedCsPrefabs;
                    }

                    stateKeys.Add(stateKey);
                    stateChildren.Add(variantGo);
                }

                controller.stateKeys = stateKeys.ToArray();
                controller.stateChildren = stateChildren.ToArray();
                controller.defaultStateKey = stateKeys.Count > 0 ? stateKeys[0] : "";

                TryAutoWireButtonStates(rootRt, controller, cs);

                var prefabPath = compFolder + "/" + safeName + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
                createdPrefabs.Add(prefabPath);

                var savedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (savedAsset != null)
                    result[cs.id] = savedAsset;

                UnityEngine.Object.DestroyImmediate(rootGo);
            }

            return result;
        }

        // ==================== Layout Support ====================

        private static bool IsSpaceBetween(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.ToUpperInvariant();
            return s.Contains("SPACE_BETWEEN") || s.Contains("SPACEBETWEEN");
        }

        private static void UpsertLayoutGroup(RectTransform rt, ExportLayout layout)
        {
            var h = rt.GetComponent<HorizontalLayoutGroup>();
            var v = rt.GetComponent<VerticalLayoutGroup>();

            if (layout.direction == "row")
            {
                if (v != null) UnityEngine.Object.DestroyImmediate(v);
                if (h == null) h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
                ApplyLayoutGroup(h, layout);
            }
            else
            {
                if (h != null) UnityEngine.Object.DestroyImmediate(h);
                if (v == null) v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
                ApplyLayoutGroup(v, layout);
            }
        }

        private static void ApplyLayoutGroup(HorizontalOrVerticalLayoutGroup g, ExportLayout layout)
        {
            g.padding.left = Mathf.RoundToInt(layout.paddingL);
            g.padding.right = Mathf.RoundToInt(layout.paddingR);
            g.padding.top = Mathf.RoundToInt(layout.paddingT);
            g.padding.bottom = Mathf.RoundToInt(layout.paddingB);
            g.spacing = layout.gap;

            g.childAlignment = MapAlignment(layout.alignPrimary, layout.alignCounter, layout.direction);
            g.childControlWidth = false;
            g.childControlHeight = false;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
        }

        private static TextAnchor MapAlignment(string alignPrimary, string alignCounter, string direction)
        {
            string p = (alignPrimary ?? "").ToUpperInvariant();
            string c = (alignCounter ?? "").ToUpperInvariant();

            Func<string, int> mapMinCenterMax = (s) =>
            {
                if (s.Contains("MAX")) return 2;
                if (s.Contains("CENTER")) return 1;
                return 0;
            };

            int horiz = 0;
            int vert = 0;

            if (direction == "row")
            {
                vert = mapMinCenterMax(c);
                horiz = mapMinCenterMax(p);
            }
            else
            {
                vert = mapMinCenterMax(p);
                horiz = mapMinCenterMax(c);
            }

            if (vert == 0) return horiz == 0 ? TextAnchor.UpperLeft : horiz == 1 ? TextAnchor.UpperCenter : TextAnchor.UpperRight;
            if (vert == 1) return horiz == 0 ? TextAnchor.MiddleLeft : horiz == 1 ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight;
            return horiz == 0 ? TextAnchor.LowerLeft : horiz == 1 ? TextAnchor.LowerCenter : TextAnchor.LowerRight;
        }

        private static void EnsureLayoutElementForLayoutChild(RectTransform rt, ExportNode node)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();

            le.preferredWidth = node.w;
            le.preferredHeight = node.h;

            float flexW = 0f;
            float flexH = 0f;

            if (node.sizing != null)
            {
                if (!string.IsNullOrEmpty(node.sizing.horizontal) &&
                    node.sizing.horizontal.Equals("FILL", StringComparison.OrdinalIgnoreCase))
                    flexW = 1f;
                if (!string.IsNullOrEmpty(node.sizing.vertical) &&
                    node.sizing.vertical.Equals("FILL", StringComparison.OrdinalIgnoreCase))
                    flexH = 1f;
            }

            if (flexW == 0f && node.layoutGrow > 0f)
                flexW = node.layoutGrow;
            if (flexH == 0f && node.layoutGrow > 0f)
                flexH = node.layoutGrow;

            if (flexW == 0f && flexH == 0f && node.child != null && node.child.grow > 0f)
            {
                flexW = node.child.grow;
                flexH = node.child.grow;
            }

            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
        }

        // ==================== Spacer Support ====================

        private static void EnsureSpacers(RectTransform container, List<ExportNode> expectedChildren)
        {
            int desiredSpacerCount = Mathf.Max(0, (expectedChildren?.Count ?? 0) - 1);

            var existingSpacers = new List<RectTransform>();
            for (int i = 0; i < container.childCount; i++)
            {
                var ch = container.GetChild(i) as RectTransform;
                if (ch != null && ch.name.StartsWith(SpacerPrefix, StringComparison.Ordinal))
                    existingSpacers.Add(ch);
            }

            while (existingSpacers.Count < desiredSpacerCount)
            {
                int idx = existingSpacers.Count;
                var go = new GameObject($"{SpacerPrefix}{idx}", typeof(RectTransform), typeof(LayoutElement));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(container, false);
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.localScale = Vector3.one;

                var le = go.GetComponent<LayoutElement>();
                le.preferredWidth = 0;
                le.preferredHeight = 0;
                le.flexibleWidth = 1;
                le.flexibleHeight = 0;

                existingSpacers.Add(rt);
            }

            while (existingSpacers.Count > desiredSpacerCount)
            {
                var last = existingSpacers[existingSpacers.Count - 1];
                existingSpacers.RemoveAt(existingSpacers.Count - 1);
                if (last != null) UnityEngine.Object.DestroyImmediate(last.gameObject);
            }

            for (int i = 0; i < existingSpacers.Count; i++)
            {
                if (existingSpacers[i] != null) existingSpacers[i].name = $"{SpacerPrefix}{i}";
            }
        }

        private static void RemoveSpacers(RectTransform container)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var ch = container.GetChild(i);
                if (ch != null && ch.name.StartsWith(SpacerPrefix, StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(ch.gameObject);
            }
        }

        private static void ApplySpaceBetweenSiblingOrder(
            RectTransform container,
            List<ExportNode> children,
            Dictionary<string, RectTransform> existing)
        {
            var spacers = new List<Transform>();
            for (int i = 0; i < container.childCount; i++)
            {
                var ch = container.GetChild(i);
                if (ch != null && ch.name.StartsWith(SpacerPrefix, StringComparison.Ordinal))
                    spacers.Add(ch);
            }

            int sib = 0;
            for (int i = 0; i < children.Count; i++)
            {
                if (existing.TryGetValue(children[i].id, out var childRt) && childRt != null)
                    childRt.SetSiblingIndex(sib++);

                if (i < spacers.Count)
                    spacers[i].SetSiblingIndex(sib++);
            }
        }

        // ==================== UI Element Helpers ====================

        private static void EnsureButton(RectTransform rt)
        {
            var img = rt.GetComponent<Image>();
            bool isNewImage = (img == null);
            if (isNewImage)
                img = rt.gameObject.AddComponent<Image>();

            if (img.sprite == null)
                img.color = new Color(1f, 1f, 1f, 0f);

            img.raycastTarget = true;

            var btn = rt.GetComponent<Button>();
            if (btn == null)
            {
                btn = rt.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.ColorTint;
                btn.targetGraphic = img;
            }
        }

        private static void UpsertImage(RectTransform rt, string assetPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"Sprite not found at {assetPath}");
                return;
            }

            var img = rt.GetComponent<Image>();
            bool isNew = (img == null);
            if (isNew) img = rt.gameObject.AddComponent<Image>();

            img.sprite = sprite;

            if (isNew)
            {
                img.preserveAspect = false;
                img.color = Color.white;
                img.raycastTarget = false;
            }
        }

        private static void UpsertSlicedImage(RectTransform rt, string assetPath, ExportNineSlice slice)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                Debug.LogWarning($"Sprite not found at {assetPath}");
                return;
            }

            var img = rt.GetComponent<Image>();
            bool isNew = (img == null);
            if (isNew) img = rt.gameObject.AddComponent<Image>();

            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;

            if (isNew)
            {
                img.preserveAspect = false;
                img.color = Color.white;
                img.raycastTarget = false;
            }
        }

        // ==================== Text Support ====================

        private static void UpsertText(RectTransform rt, ExportText text)
        {
            if (rt == null || text == null) return;

            var tmp = rt.GetComponent<TextMeshProUGUI>();
            bool isNew = (tmp == null);
            if (isNew) tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();

            if (tmp == null)
            {
                Debug.LogError($"Failed to create TextMeshProUGUI on '{rt.name}'");
                return;
            }

            tmp.text = text.content ?? "";
            tmp.fontSize = text.fontSize > 0 ? text.fontSize : 16;

            if (text.fills != null && text.fills.Count > 0)
            {
                var f = text.fills[0];
                tmp.color = new Color(f.r, f.g, f.b, f.a);
            }

            tmp.alignment = MapTMPAlignment(
                text.textAlignHorizontal ?? "LEFT",
                text.textAlignVertical ?? "TOP"
            );

            if (text.fontSize > 0)
                tmp.characterSpacing = (text.letterSpacing / text.fontSize) * 100f;

            if (text.lineHeight > 0 && text.fontSize > 0)
            {
                float defaultLineH = text.fontSize * 1.2f;
                tmp.lineSpacing = ((text.lineHeight - defaultLineH) / defaultLineH) * 100f;
            }
            else if (isNew)
            {
                tmp.lineSpacing = 0;
            }

            if (!string.IsNullOrEmpty(text.textCase))
            {
                switch (text.textCase.ToUpperInvariant())
                {
                    case "UPPER": tmp.fontStyle |= FontStyles.UpperCase; break;
                    case "LOWER": tmp.fontStyle |= FontStyles.LowerCase; break;
                    case "TITLE": tmp.fontStyle |= FontStyles.UpperCase; break;
                    default: tmp.fontStyle &= ~(FontStyles.UpperCase | FontStyles.LowerCase); break;
                }
            }

            if (!string.IsNullOrEmpty(text.textDecoration))
            {
                switch (text.textDecoration.ToUpperInvariant())
                {
                    case "UNDERLINE": tmp.fontStyle |= FontStyles.Underline; break;
                    case "STRIKETHROUGH": tmp.fontStyle |= FontStyles.Strikethrough; break;
                    default: tmp.fontStyle &= ~(FontStyles.Underline | FontStyles.Strikethrough); break;
                }
            }

            if (text.fontWeight >= 700)
                tmp.fontStyle |= FontStyles.Bold;

            if (isNew)
            {
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.raycastTarget = false;
            }

            ApplyTextAutoResize(rt, text.textAutoResize);
        }

        private static TextAlignmentOptions MapTMPAlignment(string h, string v)
        {
            switch (v.ToUpperInvariant())
            {
                case "CENTER":
                    switch (h.ToUpperInvariant())
                    {
                        case "CENTER": return TextAlignmentOptions.Center;
                        case "RIGHT": return TextAlignmentOptions.Right;
                        case "JUSTIFIED": return TextAlignmentOptions.Justified;
                        default: return TextAlignmentOptions.Left;
                    }
                case "BOTTOM":
                    switch (h.ToUpperInvariant())
                    {
                        case "CENTER": return TextAlignmentOptions.Bottom;
                        case "RIGHT": return TextAlignmentOptions.BottomRight;
                        case "JUSTIFIED": return TextAlignmentOptions.BottomJustified;
                        default: return TextAlignmentOptions.BottomLeft;
                    }
                default:
                    switch (h.ToUpperInvariant())
                    {
                        case "CENTER": return TextAlignmentOptions.Top;
                        case "RIGHT": return TextAlignmentOptions.TopRight;
                        case "JUSTIFIED": return TextAlignmentOptions.TopJustified;
                        default: return TextAlignmentOptions.TopLeft;
                    }
            }
        }

        private static void ApplyTextAutoResize(RectTransform rt, string autoResize)
        {
            var csf = rt.GetComponent<ContentSizeFitter>();
            string mode = (autoResize ?? "NONE").ToUpperInvariant();

            if (mode == "NONE" || mode == "TRUNCATE")
            {
                if (csf != null) UnityEngine.Object.DestroyImmediate(csf);
                var tmp = rt.GetComponent<TextMeshProUGUI>();
                if (tmp != null && mode == "TRUNCATE")
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                return;
            }

            if (csf == null) csf = rt.gameObject.AddComponent<ContentSizeFitter>();

            if (mode == "HEIGHT")
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else if (mode == "WIDTH_AND_HEIGHT")
            {
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        // ==================== Navigation Auto-Wiring ====================

        private static void TryAutoWireNavigation(
            RectTransform rt,
            List<ExportInteraction> interactions,
            bool isNewNode)
        {
            ExportInteraction clickNav = null;
            foreach (var ia in interactions)
            {
                if (ia.trigger != "ON_CLICK") continue;
                if (ia.action == "navigate" || ia.action == "back" || ia.action == "url")
                {
                    clickNav = ia;
                    break;
                }
            }

            if (clickNav == null) return;

            EnsureButton(rt);

            var nav = rt.GetComponent<FigmaNavigationButton>();
            if (nav == null)
                nav = rt.gameObject.AddComponent<FigmaNavigationButton>();

            nav.action = clickNav.action ?? "";
            nav.targetName = clickNav.targetName ?? "";
            nav.url = clickNav.url ?? "";
        }

        // ==================== Entry Animation ====================

        private void TryAddEntryAnimation(RectTransform rt, string nodeName, string kind)
        {
            if (rt == null || string.IsNullOrEmpty(nodeName))
                return;

            var upper = nodeName.ToUpperInvariant();

            EntryAnimationType animType = EntryAnimationType.None;

            if (upper.StartsWith("BTN_"))
                animType = EntryAnimationType.PopIn;
            else if (upper.StartsWith("IMG_") || upper.StartsWith("ICON_"))
                animType = EntryAnimationType.FadeIn;
            else if (upper.StartsWith("CTN_"))
                animType = EntryAnimationType.FadeSlideUp;
            else if (kind == "image")
                animType = EntryAnimationType.FadeIn;
            else
                return;

            var existing = rt.GetComponent<FigmaEntryAnimation>();
            if (existing != null)
                return;

            var anim = rt.gameObject.AddComponent<FigmaEntryAnimation>();
            anim.animationType = animType;
            anim.duration = 0.4f;
            anim.playOnStart = true;

            int siblingIndex = rt.GetSiblingIndex();
            anim.delay = siblingIndex * AnimationStaggerDelay;

            bool usesFade = animType == EntryAnimationType.FadeIn ||
                            animType == EntryAnimationType.FadeSlideUp ||
                            animType == EntryAnimationType.FadeSlideDown ||
                            animType == EntryAnimationType.FadeSlideLeft ||
                            animType == EntryAnimationType.FadeSlideRight ||
                            animType == EntryAnimationType.FadePopIn;

            if (usesFade && rt.GetComponent<CanvasGroup>() == null)
            {
                rt.gameObject.AddComponent<CanvasGroup>();
            }

            switch (animType)
            {
                case EntryAnimationType.PopIn:
                    anim.popOvershoot = 1.15f;
                    anim.duration = 0.35f;
                    break;
                case EntryAnimationType.FadeSlideUp:
                    anim.slideDistance = 50f;
                    anim.duration = 0.45f;
                    break;
                case EntryAnimationType.FadeIn:
                    anim.duration = 0.3f;
                    break;
            }
        }

        // ==================== Constraint Anchoring ====================

        private static void ApplyConstraintAnchoring(
            RectTransform rt, ExportNode node,
            float localX, float localY,
            float parentW, float parentH)
        {
            string hc = node.constraints != null ? (node.constraints.horizontal ?? "MIN") : "MIN";
            string vc = node.constraints != null ? (node.constraints.vertical ?? "MIN") : "MIN";

            hc = hc.ToUpperInvariant();
            vc = vc.ToUpperInvariant();

            float anchorMinX, anchorMaxX, pivotX;
            float offsetMinX = 0f, offsetMaxX = 0f;
            float anchoredX = 0f;
            bool hStretchOrScale = false;

            switch (hc)
            {
                case "CENTER":
                    anchorMinX = 0.5f; anchorMaxX = 0.5f; pivotX = 0f;
                    anchoredX = localX - parentW * 0.5f;
                    break;
                case "MAX":
                    anchorMinX = 1f; anchorMaxX = 1f; pivotX = 0f;
                    anchoredX = localX - parentW;
                    break;
                case "STRETCH":
                    anchorMinX = 0f; anchorMaxX = 1f; pivotX = 0f;
                    offsetMinX = localX;
                    offsetMaxX = -(parentW - localX - node.w);
                    hStretchOrScale = true;
                    break;
                case "SCALE":
                    float sx = parentW > 0 ? localX / parentW : 0f;
                    float ex = parentW > 0 ? (localX + node.w) / parentW : 0f;
                    anchorMinX = sx; anchorMaxX = ex; pivotX = 0f;
                    hStretchOrScale = true;
                    break;
                default:
                    anchorMinX = 0f; anchorMaxX = 0f; pivotX = 0f;
                    anchoredX = localX;
                    break;
            }

            float anchorMinY, anchorMaxY, pivotY;
            float offsetMinY = 0f, offsetMaxY = 0f;
            float anchoredY = 0f;
            bool vStretchOrScale = false;

            switch (vc)
            {
                case "CENTER":
                    anchorMinY = 0.5f; anchorMaxY = 0.5f; pivotY = 1f;
                    anchoredY = -(localY - parentH * 0.5f);
                    break;
                case "MAX":
                    anchorMinY = 0f; anchorMaxY = 0f; pivotY = 1f;
                    anchoredY = -(localY - parentH);
                    break;
                case "STRETCH":
                    anchorMinY = 0f; anchorMaxY = 1f; pivotY = 1f;
                    offsetMaxY = -localY;
                    offsetMinY = parentH - localY - node.h;
                    vStretchOrScale = true;
                    break;
                case "SCALE":
                    float topRatio = parentH > 0 ? localY / parentH : 0f;
                    float bottomRatio = parentH > 0 ? (localY + node.h) / parentH : 0f;
                    anchorMinY = 1f - bottomRatio;
                    anchorMaxY = 1f - topRatio;
                    pivotY = 1f;
                    vStretchOrScale = true;
                    break;
                default:
                    anchorMinY = 1f; anchorMaxY = 1f; pivotY = 1f;
                    anchoredY = -localY;
                    break;
            }

            rt.anchorMin = new Vector2(anchorMinX, anchorMinY);
            rt.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
            rt.pivot = new Vector2(pivotX, pivotY);
            rt.localScale = Vector3.one;

            if (hStretchOrScale && vStretchOrScale)
            {
                rt.offsetMin = new Vector2(offsetMinX, offsetMinY);
                rt.offsetMax = new Vector2(offsetMaxX, offsetMaxY);
            }
            else if (hStretchOrScale)
            {
                rt.sizeDelta = new Vector2(0f, node.h);
                rt.offsetMin = new Vector2(offsetMinX, rt.offsetMin.y);
                rt.offsetMax = new Vector2(offsetMaxX, rt.offsetMax.y);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, anchoredY);
            }
            else if (vStretchOrScale)
            {
                rt.sizeDelta = new Vector2(node.w, 0f);
                rt.offsetMin = new Vector2(rt.offsetMin.x, offsetMinY);
                rt.offsetMax = new Vector2(rt.offsetMax.x, offsetMaxY);
                rt.anchoredPosition = new Vector2(anchoredX, rt.anchoredPosition.y);
            }
            else
            {
                rt.sizeDelta = new Vector2(node.w, node.h);
                rt.anchoredPosition = new Vector2(anchoredX, anchoredY);
            }
        }

        private static void SetTopLeftAnchored(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.localScale = Vector3.one;
        }

        // ==================== Utility Methods ====================

        private static string SafeName(string s) => string.IsNullOrEmpty(s) ? "Screen" : s.Replace(" ", "_");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static RectTransform GetOrCreateScreenRoot(RectTransform canvasRoot, float w, float h)
        {
            var t = canvasRoot.Find("ScreenRoot") as RectTransform;
            if (t == null)
            {
                var go = new GameObject("ScreenRoot", typeof(RectTransform), typeof(FigmaNodeKey));
                t = go.GetComponent<RectTransform>();
                t.SetParent(canvasRoot, false);
                t.GetComponent<FigmaNodeKey>().Id = "__SCREENROOT__";
            }

            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.pivot = new Vector2(0.5f, 0.5f);
            t.localScale = Vector3.one;
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;

            return t;
        }

        private static Dictionary<string, RectTransform> IndexExisting(GameObject root)
        {
            var dict = new Dictionary<string, RectTransform>();
            IndexExistingRecursive(root.transform, dict, true);
            return dict;
        }

        private static void IndexExistingRecursive(Transform t, Dictionary<string, RectTransform> dict, bool isRoot)
        {
            var key = t.GetComponent<FigmaNodeKey>();
            if (key != null && !string.IsNullOrEmpty(key.Id))
            {
                var rt = t.GetComponent<RectTransform>();
                if (rt != null) dict[key.Id] = rt;
            }

            if (!isRoot && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                return;

            for (int i = 0; i < t.childCount; i++)
                IndexExistingRecursive(t.GetChild(i), dict, false);
        }

        private static void RemoveOrphaned(Dictionary<string, RectTransform> existing, HashSet<string> touched)
        {
            foreach (var kvp in existing)
            {
                if (kvp.Key == "__SCREENROOT__") continue;
                if (!touched.Contains(kvp.Key) && kvp.Value != null)
                    UnityEngine.Object.DestroyImmediate(kvp.Value.gameObject);
            }
        }

        private static void CollectNineSliceData(ExportNode node, Dictionary<string, ExportNineSlice> result)
        {
            if (node == null) return;

            if (node.kind == "9slice" && node.image != null && node.nineSlice != null)
            {
                var fileName = Path.GetFileName(node.image.file);
                if (!string.IsNullOrEmpty(fileName))
                    result[fileName] = node.nineSlice;
            }

            if (node.children != null)
            {
                foreach (var child in node.children)
                    CollectNineSliceData(child, result);
            }
        }

        private static void EnsureSpritesImported(string imagesFolder, float ppu, Dictionary<string, ExportNineSlice> nineSliceData)
        {
            if (!AssetDatabase.IsValidFolder(imagesFolder))
            {
                Debug.LogWarning($"Images folder not found: {imagesFolder}");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { imagesFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                bool changed = false;
                if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
                if (Math.Abs(ti.spritePixelsPerUnit - ppu) > 0.01f) { ti.spritePixelsPerUnit = ppu; changed = true; }
                if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }

                var fileName = Path.GetFileName(path);
                if (nineSliceData != null && nineSliceData.TryGetValue(fileName, out var slice) && slice != null)
                {
                    var border = new Vector4(slice.left, slice.bottom, slice.right, slice.top);
                    if (ti.spriteBorder != border)
                    {
                        ti.spriteBorder = border;
                        changed = true;
                    }
                }

                if (changed) ti.SaveAndReimport();
            }
        }

        // ==================== Environment, Transition, Video Plane Setup ====================

        private static void EnsureEnvironmentPrefab(string frameName)
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Environments");

            var envName = frameName + "_Environment";
            var prefabPath = $"Assets/Resources/Environments/{envName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return;

            var go = new GameObject(envName);
            go.AddComponent<FigmaEnvironmentMarker>();

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void EnsureTransitionPrefab(string frameName)
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Transitions");

            var transitionName = frameName + "_Transition";
            var prefabPath = $"Assets/Resources/Transitions/{transitionName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return;

            var go = new GameObject(transitionName);
            var fade = go.AddComponent<FadeTransition>();
            fade.fadeColor = Color.black;
            fade.outDuration = 0.3f;
            fade.inDuration = 0.3f;
            fade.holdDuration = 0.1f;

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static void EnsureVideoPlane(string frameName, float screenWidth, float screenHeight)
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/VideoPlanes");

            var planeName = frameName + "_VideoPlane";
            var prefabPath = $"Assets/Resources/VideoPlanes/{planeName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = planeName;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            float aspectRatio = screenWidth / screenHeight;
            float planeHeight = 2f;
            float planeWidth = planeHeight * aspectRatio;
            go.transform.localScale = new Vector3(planeWidth, planeHeight, 1f);
            go.transform.position = new Vector3(0f, planeHeight / 2f, 3f);
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            var rtPath = $"Assets/Resources/VideoPlanes/{frameName}_VideoRT.renderTexture";
            RenderTexture rt = null;

            var existingRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
            if (existingRT == null)
            {
                rt = new RenderTexture((int)screenWidth, (int)screenHeight, 0);
                rt.name = frameName + "_VideoRT";
                AssetDatabase.CreateAsset(rt, rtPath);
            }
            else
            {
                rt = existingRT;
            }

            var matPath = $"Assets/Resources/VideoPlanes/{frameName}_VideoMat.mat";
            Material mat = null;

            var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existingMat == null)
            {
                mat = new Material(Shader.Find("Unlit/Texture"));
                mat.name = frameName + "_VideoMat";
                mat.mainTexture = rt;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat = existingMat;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var videoPlayer = go.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = rt;

            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);

            AssetDatabase.SaveAssets();

            AddVideoPlaneToEnvironment(frameName, prefabPath);
        }

        private static void AddVideoPlaneToEnvironment(string frameName, string videoplanePrefabPath)
        {
            var envPrefabPath = $"Assets/Resources/Environments/{frameName}_Environment.prefab";
            var envPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(envPrefabPath);

            var videoPlanePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(videoplanePrefabPath);
            if (videoPlanePrefab == null)
                return;

            if (envPrefab == null)
            {
                var scenePlane = (GameObject)PrefabUtility.InstantiatePrefab(videoPlanePrefab);
                scenePlane.transform.position = new Vector3(0f, 1f, 3f);
                scenePlane.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Debug.Log($"Instantiated video plane in scene: {scenePlane.name}");
                return;
            }

            var existingPlane = envPrefab.transform.Find(frameName + "_VideoPlane");
            if (existingPlane != null)
                return;

            var envInstance = (GameObject)PrefabUtility.InstantiatePrefab(envPrefab);

            var planeInstance = (GameObject)PrefabUtility.InstantiatePrefab(videoPlanePrefab);
            planeInstance.transform.SetParent(envInstance.transform, false);
            planeInstance.transform.localPosition = new Vector3(0f, 1f, 3f);
            planeInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            PrefabUtility.SaveAsPrefabAsset(envInstance, envPrefabPath);
            UnityEngine.Object.DestroyImmediate(envInstance);

            Debug.Log($"Added video plane to environment: {envPrefabPath}");
        }

        // ==================== Scene Setup ====================

        private static void EnsureEventSystemInScene()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");

            go.AddComponent<EventSystem>();

            var inputSystemModuleType =
                Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem") ??
                Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem.UI");

            if (inputSystemModuleType != null)
                go.AddComponent(inputSystemModuleType);
            else
                go.AddComponent<StandaloneInputModule>();
        }

        private static void EnsureUIScreenManagerInScene(string frameName)
        {
            var existing = UnityEngine.Object.FindObjectOfType<UIScreenManager>();
            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.initialScreen))
                {
                    existing.initialScreen = frameName;
                    EditorUtility.SetDirty(existing);
                }
                EnsureEnvironmentRoot(existing);
                return;
            }

            var go = new GameObject("UIScreenManager");
            Undo.RegisterCreatedObjectUndo(go, "Create UIScreenManager");

            var mgr = go.AddComponent<UIScreenManager>();
            mgr.initialScreen = frameName;

            EnsureEnvironmentRoot(mgr);

            Debug.Log($"Figma: Created UIScreenManager (initialScreen = \"{frameName}\")");
        }

        private static void EnsureEnvironmentRoot(UIScreenManager mgr)
        {
            if (mgr.environmentParent != null)
                return;

            var envRoot = GameObject.Find("EnvironmentRoot");
            if (envRoot == null)
            {
                envRoot = new GameObject("EnvironmentRoot");
                Undo.RegisterCreatedObjectUndo(envRoot, "Create EnvironmentRoot");
                Debug.Log("Figma: Created EnvironmentRoot for 3D environment prefabs");
            }

            mgr.environmentParent = envRoot.transform;
            EditorUtility.SetDirty(mgr);
        }
    }
}
#endif
