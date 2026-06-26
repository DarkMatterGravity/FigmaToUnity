// ==============================
// Figma → Unity UGUI Exporter
// ==============================

const g: any = globalThis as any;
if (typeof g.setImmediate !== "function") {
  g.setImmediate = (fn: (...args: any[]) => void, ...args: any[]) =>
      (setTimeout(() => fn(...args), 0) as any);
}
if (typeof g.clearImmediate !== "function") {
  g.clearImmediate = (id: any) => clearTimeout(id);
}

import JSZip from "jszip";

figma.showUI(__html__, { width: 580, height: 920 });

type ExportMsg =
    | { type: "EXPORT"; scale?: number }
    | { type: "EXPORT_ALL"; scale?: number }
    | { type: "CLEAR" };

type FileEntry = { path: string; base64: string };

figma.ui.onmessage = async (msg: ExportMsg) => {
  if (msg.type === "CLEAR") {
    figma.ui.postMessage({ type: "CLEARED" });
    return;
  }

  // Handle Export All Frames
  if (msg.type === "EXPORT_ALL") {
    await handleExportAll(msg);
    return;
  }

  if (msg.type !== "EXPORT") return;

  try {
    const selection = figma.currentPage.selection;
    if (!selection || selection.length === 0) {
      figma.ui.postMessage({ type: "ERROR", message: "No frame selected" });
      return;
    }

    const frame = selection[0];
    if (frame.type !== "FRAME") {
      figma.ui.postMessage({ type: "ERROR", message: "Selection is not a frame" });
      return;
    }

    const scale = typeof msg.scale === "number" ? msg.scale : 1;

    // Output layout: {FrameName}_Screen.json + {FrameName}_Images/
    const safeName = safeFilename(frame.name);
    const imagesPrefix = safeName + "_Images";

    const files: FileEntry[] = [];

    const { rootNode, imageFiles, components, componentSets } = await exportTree(frame, scale, imagesPrefix);
    files.push(...imageFiles);

    const bundle = {
      version: 7,
      documentName: figma.root.name,
      frameId: frame.id,
      frameName: frame.name,
      width: frame.width,
      height: frame.height,
      components: components,
      componentSets: componentSets,
      nodes: rootNode,
    };

    const screenJson = JSON.stringify(bundle, null, 2);
    const jsonFileName = safeName + "_Screen.json";
    files.push({ path: jsonFileName, base64: base64FromString(screenJson) });

    // Zip all files
    const zip = new JSZip();
    for (const f of files) zip.file(f.path, base64ToBytes(f.base64));

    const zipped = await zip.generateAsync({ type: "uint8array" });
    const zipBase64 = figma.base64Encode(zipped);

    figma.ui.postMessage({
      type: "ZIP_READY",
      zipName: `${safeName}_UNITY.zip`,
      zipBase64,
      files,
      fileCount: files.length,
    });
  } catch (e: any) {
    const msgText = [
      "Export failed: " + (e?.message || String(e)),
      "name: " + (e?.name || ""),
      "stack: " + (e?.stack || ""),
    ].join("\n");
    console.error(msgText);
    figma.ui.postMessage({ type: "ERROR", message: msgText });
  }
};

// ==============================
// Export All Frames Handler
// ==============================

async function handleExportAll(msg: { type: "EXPORT_ALL"; scale?: number }) {
  try {
    const selection = figma.currentPage.selection;
    let framesToExport: FrameNode[] = [];

    // If a section is selected, export all frames in that section
    if (selection.length === 1 && selection[0].type === "SECTION") {
      const section = selection[0] as SectionNode;
      framesToExport = section.children.filter(
        (child): child is FrameNode => child.type === "FRAME"
      );
    }
    // If multiple frames are selected, export those
    else if (selection.length > 0 && selection.every(s => s.type === "FRAME")) {
      framesToExport = selection as FrameNode[];
    }
    // Otherwise, export all top-level frames on the current page
    else {
      framesToExport = figma.currentPage.children.filter(
        (child): child is FrameNode => child.type === "FRAME"
      );
    }

    if (framesToExport.length === 0) {
      figma.ui.postMessage({ type: "ERROR", message: "No frames found to export" });
      return;
    }

    const scale = typeof msg.scale === "number" ? msg.scale : 1;

    const allFiles: FileEntry[] = [];
    let exportedCount = 0;

    for (const frame of framesToExport) {
      const safeName = safeFilename(frame.name);
      const imagesPrefix = safeName + "_Images";

      const { rootNode, imageFiles, components, componentSets } = await exportTree(frame, scale, imagesPrefix);
      allFiles.push(...imageFiles);

      const bundle = {
        version: 7,
        documentName: figma.root.name,
        frameId: frame.id,
        frameName: frame.name,
        width: frame.width,
        height: frame.height,
        components: components,
        componentSets: componentSets,
        nodes: rootNode,
      };

      const screenJson = JSON.stringify(bundle, null, 2);
      const jsonFileName = safeName + "_Screen.json";
      allFiles.push({ path: jsonFileName, base64: base64FromString(screenJson) });

      exportedCount++;
    }

    // Zip all files together
    const zip = new JSZip();
    for (const f of allFiles) zip.file(f.path, base64ToBytes(f.base64));

    const zipped = await zip.generateAsync({ type: "uint8array" });
    const zipBase64 = figma.base64Encode(zipped);

    figma.ui.postMessage({
      type: "ZIP_READY",
      zipName: "AllFrames_UNITY.zip",
      zipBase64,
      files: allFiles,
      fileCount: allFiles.length,
    });

    console.log(`Exported ${exportedCount} frames with ${allFiles.length} total files`);

  } catch (e: any) {
    const msgText = [
      "Export All failed: " + (e?.message || String(e)),
      "name: " + (e?.name || ""),
      "stack: " + (e?.stack || ""),
    ].join("\n");
    console.error(msgText);
    figma.ui.postMessage({ type: "ERROR", message: msgText });
  }
}

// ==============================
// Export rules
// ==============================
//
// - CTN_* : container only (no bake), recurse
// - BTN_* : bake ONE composite image, stop recursion (Unity importer makes Button)
// - IMG_* : bake ONE composite image, stop recursion
// - ICON_*: bake ONE composite image, stop recursion
// - Unprefixed DIRECT children under root frame: bake ONE composite image (convenience)
// - TEXT: exported as structured data (content, font, color, alignment, etc.)

async function exportTree(
    rootFrame: FrameNode,
    scale: number,
    imagesPrefix: string
): Promise<{ rootNode: any; imageFiles: FileEntry[]; components: any[]; componentSets: any[] }> {
  const imageFiles: FileEntry[] = [];
  const componentDefs = new Map<string, any>();

  const rootNode: any = {
    id: rootFrame.id,
    name: rootFrame.name,
    kind: "container",
    x: 0,
    y: 0,
    w: rootFrame.width,
    h: rootFrame.height,
    rotation: (rootFrame as any).rotation || 0,
    visible: rootFrame.visible !== false,
    children: [],
  };

  if (rootFrame.layoutMode !== "NONE") {
    rootNode.layout = {
      direction: rootFrame.layoutMode === "HORIZONTAL" ? "row" : "column",
      paddingL: rootFrame.paddingLeft || 0,
      paddingR: rootFrame.paddingRight || 0,
      paddingT: rootFrame.paddingTop || 0,
      paddingB: rootFrame.paddingBottom || 0,
      gap: rootFrame.itemSpacing || 0,
      alignPrimary: rootFrame.primaryAxisAlignItems || "",
      alignCounter: rootFrame.counterAxisAlignItems || "",
    };
  }

  for (const child of rootFrame.children) {
    const res = await exportNodeAndCollectFiles(child, scale, imagesPrefix, true, componentDefs, rootFrame);
    rootNode.children.push(res.rootNode);
    imageFiles.push(...res.imageFiles);
  }

  const components: any[] = [];
  const componentSets: any[] = [];

  componentDefs.forEach(function(value, key) {
    if (value == null) return; // skip anti-recursion placeholders
    if (typeof key === "string" && key.startsWith("SET:")) {
      componentSets.push(value);
    } else {
      components.push(value);
    }
  });

  return { rootNode, imageFiles, components, componentSets };
}

async function exportComponentSet(
    setNode: SceneNode & { children: readonly SceneNode[] },
    scale: number,
    imagesPrefix: string,
    componentDefs: Map<string, any>
): Promise<{ id: string; name: string; variants: any[]; imageFiles: FileEntry[] }> {
  const imageFiles: FileEntry[] = [];
  const variants: any[] = [];

  for (const variant of setNode.children) {
    if (variant.type !== "COMPONENT") continue;

    const variantResult = await exportNodeAndCollectFiles(
      variant as SceneNode, scale, imagesPrefix, false, componentDefs
    );

    // Build the flattened state key from variantProperties
    const props = (variant as any).variantProperties || {};
    const keys = Object.keys(props).sort();
    const stateKey = keys.map(function(k) { return k + "=" + props[k]; }).join(", ");

    variantResult.rootNode.variantStateKey = stateKey;

    variants.push(variantResult.rootNode);
    imageFiles.push(...variantResult.imageFiles);
  }

  return { id: setNode.id, name: (setNode as any).name || "", variants, imageFiles };
}

async function exportNodeAndCollectFiles(
    node: SceneNode,
    scale: number,
    imagesPrefix: string,
    isDirectChildOfRootFrame: boolean,
    componentDefs: Map<string, any>,
    parentNode?: SceneNode
): Promise<{ rootNode: any; imageFiles: FileEntry[] }> {
  const imageFiles: FileEntry[] = [];

  const name = (node.name || "").trim();
  const upper = name.toUpperCase();

  const isCNT = upper.startsWith("CTN_");
  const isBTN = upper.startsWith("BTN_");
  const isIMG = upper.startsWith("IMG_");
  const isICON = upper.startsWith("ICON_");
  const is9SLICE = upper.startsWith("9SLICE_");

  // Convenience rule: ANY unprefixed node directly under root becomes a composite bake
  // (except TEXT, CTN_, 9SLICE_, and component instances/definitions which need their structure preserved)
  const isUnprefixedUnderRoot =
      isDirectChildOfRootFrame &&
      !isCNT && !isBTN && !isIMG && !isICON && !is9SLICE;

  // Shape types that should always be baked (vectors, icons, design elements)
  const isShapeType = ["VECTOR", "ELLIPSE", "LINE", "POLYGON",
                       "STAR", "BOOLEAN_OPERATION"].includes(node.type);

  // Check if this is a simple rectangle with only solid/gradient fills (can use CSS)
  const isSimpleRect = node.type === "RECTANGLE" && (node as any).fills &&
    ((node as any).fills as any[]).every((f: any) =>
      f.visible !== false && (f.type === "SOLID" || f.type === "GRADIENT_LINEAR" || f.type === "GRADIENT_RADIAL")
    );

  // Check if node has no children (leaf node that should be baked)
  const hasNoChildren = !("children" in node) || (node as any).children.length === 0;

  const shouldCompositeBake =
      !isCNT && !is9SLICE &&
      (isBTN || isIMG || isICON ||
       // Bake shape types (but NOT simple rectangles that can use CSS)
       (isShapeType && !isSimpleRect) ||
       // Bake any leaf node that isn't text and isn't a simple CSS-renderable rect
       (hasNoChildren && node.type !== "TEXT" && !isSimpleRect) ||
       // Original logic for unprefixed root children
       (isUnprefixedUnderRoot && node.type !== "TEXT" && node.type !== "INSTANCE" && node.type !== "COMPONENT" && !isSimpleRect));

  // Normalize coordinates: Groups don't create their own coordinate space in
  // Figma — children of a Group have coords relative to the enclosing Frame.
  let relX = safeX(node);
  let relY = safeY(node);
  if (parentNode && (parentNode as any).type === "GROUP") {
    relX -= safeX(parentNode);
    relY -= safeY(parentNode);
  }

  const base: any = {
    id: node.id,
    name: node.name,
    kind: shouldCompositeBake ? "image" : "container",
    x: relX,
    y: relY,
    w: node.width,
    h: node.height,
    rotation: (node as any).rotation || 0,
    visible: node.visible !== false,
    children: [],
  };

  // Extract background fills for containers
  if (!shouldCompositeBake && (node as any).fills) {
    const fills = (node as any).fills;
    if (Array.isArray(fills) && fills.length > 0) {
      const visibleFills = fills.filter((f: any) => f.visible !== false);
      if (visibleFills.length > 0) {
        base.fills = visibleFills.map((f: any) => {
          if (f.type === "SOLID") {
            return {
              type: "SOLID",
              r: f.color?.r ?? 0,
              g: f.color?.g ?? 0,
              b: f.color?.b ?? 0,
              a: f.opacity ?? 1
            };
          } else if (f.type === "GRADIENT_LINEAR" || f.type === "GRADIENT_RADIAL") {
            return {
              type: f.type,
              stops: (f.gradientStops || []).map((s: any) => ({
                position: s.position,
                r: s.color?.r ?? 0,
                g: s.color?.g ?? 0,
                b: s.color?.b ?? 0,
                a: s.color?.a ?? 1
              })),
              transform: f.gradientTransform
            };
          }
          return { type: f.type };
        });
      }
    }
  }

  // Extract corner radius
  if ((node as any).cornerRadius !== undefined && (node as any).cornerRadius !== figma.mixed) {
    base.cornerRadius = (node as any).cornerRadius;
  } else if ((node as any).topLeftRadius !== undefined) {
    base.cornerRadius = {
      tl: (node as any).topLeftRadius || 0,
      tr: (node as any).topRightRadius || 0,
      br: (node as any).bottomRightRadius || 0,
      bl: (node as any).bottomLeftRadius || 0
    };
  }

  const interactions = extractInteractions(node);
  if (interactions) base.interactions = interactions;

  // Component / Instance detection
  if (node.type === "COMPONENT") {
    base.isComponent = true;
    base.componentId = node.id;
    if (!componentDefs.has(node.id)) {
      componentDefs.set(node.id, base);
    }
  }
  if (node.type === "INSTANCE") {
    base.isInstance = true;
    try {
      const mainComp = (node as InstanceNode).mainComponent;
      if (mainComp) {
        const parentNode2 = mainComp.parent;
        if (parentNode2 && parentNode2.type === "COMPONENT_SET") {
          // --- Variant instance ---
          const setId = parentNode2.id;
          base.componentSetId = setId;

          // Build the state key for the active variant
          const props = (mainComp as ComponentNode).variantProperties || {};
          const keys = Object.keys(props).sort();
          base.defaultStateKey = keys.map(function(k) { return k + "=" + props[k]; }).join(", ");

          // Export all variants in the set (once)
          if (!componentDefs.has("SET:" + setId)) {
            componentDefs.set("SET:" + setId, null); // anti-recursion placeholder
            const setData = await exportComponentSet(parentNode2 as any, scale, imagesPrefix, componentDefs);
            componentDefs.set("SET:" + setId, setData);
            imageFiles.push(...setData.imageFiles);
          }
        } else {
          // --- Non-variant instance (existing behavior) ---
          base.componentId = mainComp.id;
          if (!componentDefs.has(mainComp.id)) {
            componentDefs.set(mainComp.id, null); // placeholder prevents infinite recursion
            const compResult = await exportNodeAndCollectFiles(
              mainComp as SceneNode, scale, imagesPrefix, false, componentDefs
            );
            componentDefs.set(mainComp.id, compResult.rootNode);
            imageFiles.push(...compResult.imageFiles);
          }
        }
      }
    } catch (e) {
      // mainComponent not accessible (external library, deleted, etc.)
    }
  }

  // constraints — try direct access, then validate against position-based inference
  let apiConstraints: { horizontal: string; vertical: string } | null = null;
  try {
    const c = (node as any).constraints;
    if (c && typeof c === "object") {
      apiConstraints = {
        horizontal: c.horizontal || "MIN",
        vertical: c.vertical || "MIN",
      };
    }
  } catch (_) { /* constraints not available */ }

  // Always infer constraints from position for validation/fallback
  let inferredConstraints: { horizontal: string; vertical: string } | null = null;
  if (parentNode) {
    const pw = (parentNode as any).width || 0;
    const ph = (parentNode as any).height || 0;
    inferredConstraints = {
      horizontal: inferConstraint(relX, node.width, pw),
      vertical:   inferConstraint(relY, node.height, ph),
    };
  }

  // Use API constraints if available, but override with inferred if API says MIN
  // and inference says something different (likely GroupNode with incorrect API values)
  if (apiConstraints) {
    base.constraints = {
      horizontal: (apiConstraints.horizontal === "MIN" && inferredConstraints && inferredConstraints.horizontal !== "MIN")
        ? inferredConstraints.horizontal
        : apiConstraints.horizontal,
      vertical: (apiConstraints.vertical === "MIN" && inferredConstraints && inferredConstraints.vertical !== "MIN")
        ? inferredConstraints.vertical
        : apiConstraints.vertical,
    };
  } else if (inferredConstraints) {
    base.constraints = inferredConstraints;
  }

  // sizing (auto-layout children — layoutSizingHorizontal/Vertical)
  if ("layoutSizingHorizontal" in node || "layoutSizingVertical" in node) {
    base.sizing = {
      horizontal: (node as any).layoutSizingHorizontal || "FIXED",
      vertical: (node as any).layoutSizingVertical || "FIXED",
    };
  }

  // layoutGrow (auto-layout fill behavior)
  if ("layoutGrow" in node) {
    base.layoutGrow = (node as any).layoutGrow || 0;
  }

  // layoutPositioning — "ABSOLUTE" means the child is free-positioned inside
  // an auto-layout parent (has constraints). "AUTO" means it's in the flow.
  try {
    const lp = (node as any).layoutPositioning;
    if (lp && lp !== "AUTO") base.layoutPositioning = lp;
  } catch (_) { /* not available */ }

  // auto-layout
  if (node.type === "FRAME" && node.layoutMode !== "NONE") {
    base.layout = {
      direction: node.layoutMode === "HORIZONTAL" ? "row" : "column",
      paddingL: node.paddingLeft || 0,
      paddingR: node.paddingRight || 0,
      paddingT: node.paddingTop || 0,
      paddingB: node.paddingBottom || 0,
      gap: node.itemSpacing || 0,
      alignPrimary: node.primaryAxisAlignItems || "",
      alignCounter: node.counterAxisAlignItems || "",
    };
  }

  // 9-slice detection: infer grid from children positions
  if (is9SLICE && "children" in node && (node.children.length === 8 || node.children.length === 9)) {
    base.kind = "9slice";

    // Sort children by position to get the 3x3 grid
    const kids = [...node.children].map(c => ({
      node: c,
      x: safeX(c) - safeX(node),
      y: safeY(c) - safeY(node),
      w: c.width,
      h: c.height
    }));

    // Sort by y then x to get row-major order
    kids.sort((a, b) => {
      if (Math.abs(a.y - b.y) > 10) return a.y - b.y;
      return a.x - b.x;
    });

    // Find corners by position (handles both 8 and 9 children)
    const bottomRowStart = node.children.length === 9 ? 6 : 5;
    const topLeft = kids[0];
    const topRight = kids[2];
    const bottomLeft = kids[bottomRowStart];

    // Extract grid dimensions (multiply by scale for exported texture size)
    const leftW = topLeft.w * scale;
    const rightW = topRight.w * scale;
    const topH = topLeft.h * scale;
    const bottomH = bottomLeft.h * scale;

    base.nineSlice = {
      left: leftW,
      right: rightW,
      top: topH,
      bottom: bottomH
    };

    // Bake as single composite image for Unity
    const bytes = await node.exportAsync({
      format: "PNG",
      constraint: { type: "SCALE", value: scale }
    });

    const short = shortId(node.id);
    const pretty = prettyLayerName(node.name || "");
    const fileName = `9SLICE_${pretty}_${short}.png`;
    const relPath = `${imagesPrefix}/${fileName}`;

    imageFiles.push({ path: relPath, base64: figma.base64Encode(bytes) });
    base.image = { file: relPath };
    base.children = []; // Don't export individual slice children

    return { rootNode: base, imageFiles };
  }

  // TEXT nodes → export structured text data (not rasterized)
  // Exception: BTN_ prefixed text should be baked as image
  if (node.type === "TEXT" && !shouldCompositeBake) {
    base.kind = "text";
    const tn = node as TextNode;

    try {
      // Helper: resolve mixed to first-character value
      const resolveFont = (): { family: string; style: string } => {
        const f = tn.fontName;
        if (f !== figma.mixed) return f;
        if (tn.characters.length > 0) return tn.getRangeFontName(0, 1) as FontName;
        return { family: "Inter", style: "Regular" };
      };
      const resolveNumber = (val: number | typeof figma.mixed, fallback: number): number => {
        return val === figma.mixed ? fallback : val;
      };

      const fontName = resolveFont();
      const fontSize = resolveNumber(tn.fontSize, 16);

      // Letter spacing: Figma gives {value, unit} — convert to px
      let letterSpacingPx = 0;
      try {
        const ls = tn.letterSpacing;
        if (ls !== figma.mixed && ls) {
          letterSpacingPx = ls.unit === "PERCENT" ? (ls.value / 100) * fontSize : ls.value;
        }
      } catch (_) {}

      // Line height: Figma gives {value, unit} — export px value, null for AUTO
      let lineHeightPx: number | null = null;
      try {
        const lh = tn.lineHeight;
        if (lh !== figma.mixed && lh && lh.unit !== "AUTO") {
          lineHeightPx = lh.unit === "PERCENT" ? (lh.value / 100) * fontSize : lh.value;
        }
      } catch (_) {}

      // Color from first visible solid fill
      let fillColor = { r: 1, g: 1, b: 1, a: 1 };
      try {
        const fills = tn.fills;
        if (Array.isArray(fills)) {
          const solid = fills.find((f: any) => f.type === "SOLID" && f.visible !== false);
          if (solid) {
            fillColor = {
              r: solid.color.r,
              g: solid.color.g,
              b: solid.color.b,
              a: solid.opacity ?? 1
            };
          }
        }
      } catch (_) {}

      // Font weight
      let fontWeight = 400;
      try {
        const fw = (tn as any).fontWeight;
        fontWeight = fw === figma.mixed ? 400 : (fw || 400);
      } catch (_) {}

      base.text = {
        content: tn.characters,
        fontSize,
        fontName: { family: fontName.family, style: fontName.style },
        fontWeight,
        letterSpacing: letterSpacingPx,
        lineHeight: lineHeightPx,
        textAlignHorizontal: tn.textAlignHorizontal || "LEFT",
        textAlignVertical: tn.textAlignVertical || "TOP",
        textAutoResize: tn.textAutoResize || "NONE",
        fills: [fillColor],
        textCase: (tn as any).textCase === figma.mixed ? "ORIGINAL" : ((tn as any).textCase || "ORIGINAL"),
        textDecoration: (tn as any).textDecoration === figma.mixed ? "NONE" : ((tn as any).textDecoration || "NONE"),
      };
    } catch (e) {
      // Fallback: export minimal text data so the node isn't lost
      console.warn("Text extraction failed for node: " + tn.name, e);
      base.text = {
        content: tn.characters || "",
        fontSize: 16,
        fontName: { family: "Inter", style: "Regular" },
        fontWeight: 400,
        letterSpacing: 0,
        lineHeight: null,
        textAlignHorizontal: "LEFT",
        textAlignVertical: "TOP",
        textAutoResize: "NONE",
        fills: [{ r: 1, g: 1, b: 1, a: 1 }],
        textCase: "ORIGINAL",
        textDecoration: "NONE",
      };
    }

    return { rootNode: base, imageFiles };
  }

  // Composite bake: export ONE PNG and stop recursion
  if (shouldCompositeBake) {
    const bytes = await node.exportAsync({
      format: "PNG",
      constraint: { type: "SCALE", value: scale },
    });

    const short = shortId(node.id);
    const pretty = prettyLayerName(node.name || "");
    const kindPrefix = isBTN ? "BTN" : isIMG ? "IMG" : isICON ? "ICON" : "CMP";

    const fileName = `${kindPrefix}_${pretty}_${short}.png`;
    const relPath = `${imagesPrefix}/${fileName}`;

    imageFiles.push({ path: relPath, base64: figma.base64Encode(bytes) });

    base.kind = "image";
    base.image = { file: relPath };
    base.children = [];

    return { rootNode: base, imageFiles };
  }

  // containers recurse
  if ("children" in node) {
    for (const child of node.children) {
      const res = await exportNodeAndCollectFiles(child, scale, imagesPrefix, false, componentDefs, node);
      base.children.push(res.rootNode);
      imageFiles.push(...res.imageFiles);
    }
  }

  return { rootNode: base, imageFiles };
}

// ==============================
// Helpers
// ==============================

// Infer a constraint axis value from the node's position and size relative to
// its parent. Used as a fallback when the Figma API doesn't expose constraints.
function inferConstraint(pos: number, size: number, parentSize: number): string {
  if (parentSize <= 0) return "MIN";

  const T = 5; // tolerance in px for edge snapping
  const endGap = parentSize - pos - size;

  // Fills or overflows the parent on both sides → STRETCH
  if (pos <= T && endGap <= T) return "STRETCH";

  // Overflows significantly → SCALE (proportional)
  if (size > parentSize + T) return "SCALE";

  // Centered (midpoint within tolerance of parent center)
  const center = pos + size / 2;
  const parentCenter = parentSize / 2;
  const centerTolerance = parentSize * 0.05; // 5% of parent size for center detection
  if (Math.abs(center - parentCenter) < centerTolerance) return "CENTER";

  // Compare distances: if closer to far edge (right/bottom), use MAX
  if (endGap < pos * 0.5) return "MAX";

  // Pinned to far edge (right / bottom) - original strict check
  if (Math.abs(endGap) < T && pos > T) return "MAX";

  // Default: pinned to near edge (left / top)
  return "MIN";
}

function safeX(node: SceneNode): number {
  return typeof (node as any).x === "number"
      ? (node as any).x
      : node.absoluteTransform[0][2];
}

function safeY(node: SceneNode): number {
  return typeof (node as any).y === "number"
      ? (node as any).y
      : node.absoluteTransform[1][2];
}

function base64FromString(str: string): string {
  const bytes = new Uint8Array(str.length);
  for (let i = 0; i < str.length; i++) bytes[i] = str.charCodeAt(i) & 0xff;
  return figma.base64Encode(bytes);
}

function base64ToBytes(base64: string): Uint8Array {
  return figma.base64Decode(base64);
}

function safeFilename(name: string) {
  return (name || "Export").replace(/[<>:"/\\|?*\x00-\x1F]/g, "_");
}

function shortId(id: string) {
  // "660:24183" -> "660_24183"
  return id.replace(/:/g, "_");
}

function extractInteractions(node: SceneNode): any[] | undefined {
  try {
    const reactions = (node as any).reactions;
    if (!reactions || !Array.isArray(reactions) || reactions.length === 0) return undefined;

    const result: any[] = [];
    for (const reaction of reactions) {
      if (!reaction || !reaction.trigger) continue;

      const triggerType = reaction.trigger.type; // "ON_CLICK", "ON_HOVER", etc.

      // Support both deprecated .action and new .actions array
      const actions = reaction.actions || (reaction.action ? [reaction.action] : []);

      for (const action of actions) {
        if (!action) continue;

        if (action.type === "NODE" && action.destinationId) {
          // Resolve destination node to get its name
          let targetName = "";
          try {
            const destNode = figma.getNodeById(action.destinationId);
            if (destNode) targetName = destNode.name || "";
          } catch (_) {}

          result.push({
            trigger: triggerType,
            action: "navigate",
            navigation: action.navigation || "NAVIGATE",
            targetNodeId: action.destinationId,
            targetName: targetName,
          });
        } else if (action.type === "BACK") {
          result.push({
            trigger: triggerType,
            action: "back",
          });
        } else if (action.type === "CLOSE") {
          result.push({
            trigger: triggerType,
            action: "close",
          });
        } else if (action.type === "URL" && action.url) {
          result.push({
            trigger: triggerType,
            action: "url",
            url: action.url,
          });
        }
      }
    }

    return result.length > 0 ? result : undefined;
  } catch (_) {
    return undefined;
  }
}

function prettyLayerName(name: string) {
  // Strip known prefixes and sanitize
  let s = (name || "").trim();

  s = s.replace(/^CTN_/i, "");
  s = s.replace(/^BTN_/i, "");
  s = s.replace(/^IMG_/i, "");
  s = s.replace(/^ICON_/i, "");
  s = s.replace(/^9SLICE_/i, "");

  // fallback if empty
  if (!s) s = "Layer";

  // sanitize for filenames
  s = s.replace(/[<>:"/\\|?*\x00-\x1F]/g, "_");
  s = s.replace(/\s+/g, "_");
  s = s.replace(/_+/g, "_");

  // keep it reasonably short
  if (s.length > 48) s = s.slice(0, 48);

  return s;
}
