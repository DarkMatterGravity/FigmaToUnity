#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FigmaImporter.Editor
{
    // ---- JSON DTOs for Figma Export Format ----

    [Serializable]
    public class ExportBundle
    {
        public int version;
        public string documentName;
        public string frameId;
        public string frameName;
        public float width;
        public float height;
        public List<ExportNode> components;
        public List<ExportComponentSet> componentSets;
        public ExportNode nodes;
    }

    [Serializable]
    public class ExportNode
    {
        public string id;
        public string name;
        public string kind;

        public float x, y, w, h;
        public float rotation;
        public float opacity;
        public bool visible;

        public ExportLayout layout;
        public ExportChild child;

        public ExportImage image;
        public ExportText text;
        public List<ExportNode> children;

        // Constraint & sizing data
        public ExportConstraints constraints;
        public ExportSizing sizing;
        public float layoutGrow;

        // Component / Instance support
        public bool isComponent;
        public bool isInstance;
        public string componentId;

        // Layout positioning ("ABSOLUTE" = free-positioned in auto-layout parent)
        public string layoutPositioning;

        // Variant component set support
        public string componentSetId;
        public string defaultStateKey;
        public string variantStateKey;

        // Prototype interactions (navigation, back, url)
        public List<ExportInteraction> interactions;

        // 9-slice support
        public ExportNineSlice nineSlice;
    }

    [Serializable]
    public class ExportComponentSet
    {
        public string id;
        public string name;
        public List<ExportNode> variants;
    }

    [Serializable]
    public class ExportLayout
    {
        public string direction; // "row" | "column"
        public float paddingL, paddingR, paddingT, paddingB;
        public float gap;
        public string alignPrimary; // can be SPACE_BETWEEN
        public string alignCounter;
        public string primarySizing;
        public string counterSizing;
    }

    [Serializable]
    public class ExportChild
    {
        public float grow;
        public string align;
    }

    [Serializable]
    public class ExportImage
    {
        public string file;
    }

    [Serializable]
    public class ExportConstraints
    {
        public string horizontal;
        public string vertical;
    }

    [Serializable]
    public class ExportSizing
    {
        public string horizontal;
        public string vertical;
    }

    [Serializable]
    public class ExportText
    {
        public string content;
        public float fontSize;
        public ExportFontName fontName;
        public int fontWeight;
        public float letterSpacing;
        public float lineHeight;
        public string textAlignHorizontal;
        public string textAlignVertical;
        public string textAutoResize;
        public List<ExportFill> fills;
        public string textCase;
        public string textDecoration;
    }

    [Serializable]
    public class ExportFontName
    {
        public string family;
        public string style;
    }

    [Serializable]
    public class ExportFill
    {
        public float r, g, b, a;
    }

    [Serializable]
    public class ExportInteraction
    {
        public string trigger;
        public string action;
        public string navigation;
        public string targetNodeId;
        public string targetName;
        public string url;
    }

    [Serializable]
    public class ExportNineSlice
    {
        public float left;
        public float right;
        public float top;
        public float bottom;
    }
}
#endif
