# Figma Importer for Unity - Project Memory

## Project Overview

A two-part system for importing Figma UI designs into Unity as fully functional UGUI prefabs. Bridges design-to-development by automating conversion of Figma components, layouts, text, images, and prototype interactions into production-ready Unity UI hierarchies.

**Version:** 1.0.0 (Initial release June 12, 2026)

## Architecture

```
FigmaToUnity/
├── FigmaPlugin/              # Figma Plugin (TypeScript)
│   ├── code.ts               # Main export logic (~1000 lines)
│   ├── ui.html               # Plugin UI
│   ├── manifest.json         # Plugin config
│   └── package.json          # npm dependencies (esbuild, jszip)
│
└── Assets/FigmaImporter/     # Unity Package (C#)
    ├── Editor/               # Import pipeline
    │   ├── FigmaImporterWindow.cs    # Editor UI (Tools > Figma > Import to Unity...)
    │   ├── FigmaNodeBuilder.cs       # Core importer (1,868 lines)
    │   ├── FigmaTypes.cs             # JSON DTOs (ExportBundle, ExportNode, etc.)
    │   ├── FigmaApiClient.cs         # REST API (future - URL import)
    │   └── FigmaAutoReimporter.cs    # File watching
    └── Runtime/              # Gameplay components
        ├── UIScreenManager.cs        # Navigation singleton
        ├── FigmaVariantController.cs # Component state management
        ├── FigmaEntryAnimation.cs    # Entry animations (11 types)
        ├── FigmaNavigationButton.cs  # Prototype interaction wiring
        ├── FigmaButtonStateWirer.cs  # Button visual states
        ├── FigmaNodeKey.cs           # Node tracking for re-imports
        ├── FigmaEnvironmentMarker.cs # 3D environment support
        └── Transitions/              # Screen transitions
            ├── ScreenTransition.cs   # Abstract base
            ├── FadeTransition.cs
            └── CameraTransition.cs
```

## Data Flow

1. **Designer in Figma** runs plugin → exports ZIP (JSON + PNGs)
2. **Developer in Unity** opens `Tools > Figma > Import to Unity...`
3. **FigmaNodeBuilder** generates UGUI prefabs in `Resources/Screens/`
4. **Runtime** uses `UIScreenManager.Show("ScreenName")` for navigation

## Export Format (v7)

JSON contains:
- `version`, `frameName`, `width`, `height`
- `nodes` - hierarchical node tree with position, constraints, layout, text, images
- `components` - Figma components (become Unity prefabs)
- `componentSets` - Variant definitions

## Layer Naming Conventions

| Prefix | Behavior | Entry Animation |
|--------|----------|-----------------|
| `BTN_` | Button (baked image + Button component) | PopIn |
| `IMG_` | Image sprite | FadeIn |
| `ICON_` | Icon sprite | FadeIn |
| `CTN_` | Container (no image, recurses children) | FadeSlideUp |
| `9SLICE_` | 9-slice scalable background | None |
| `TEXT` | Native TextMeshPro text | None |

## Key Systems

### Constraint → Anchor Mapping
- MIN (left/top) → anchor 0
- CENTER → anchor 0.5
- MAX (right/bottom) → anchor 1
- STRETCH → spans 0 to 1
- SCALE → proportional

### Auto-Layout Conversion
- Figma row → HorizontalLayoutGroup
- Figma column → VerticalLayoutGroup
- Handles padding, gap, alignment, child growth
- Space-between uses invisible spacer GameObjects

### Variant System
- `FigmaVariantController` manages state switching
- Button states auto-wired: Normal/Hover/Pressed/Disabled
- Runtime: `controller.SetState("State=Pressed")`

### Entry Animations
- Staggered by sibling index
- 11 types: FadeIn, PopIn, SlideFrom variants, FadeSlideUp, etc.
- Configurable duration, delay, easing, spring physics

### Re-import Safety
- Uses `FigmaNodeKey` to track node identity
- Preserves onClick handlers and custom MonoBehaviours
- Orphan cleanup removes deleted Figma nodes

## Runtime API

```csharp
// Navigate to a screen
UIScreenManager.Show("HomeScreen");

// Go back in history
UIScreenManager.Back();

// Instant swap (no transition)
UIScreenManager.ShowImmediate("Settings");

// Check current screen
string current = UIScreenManager.CurrentScreen;
```

## Requirements

- Unity 2021.3+
- TextMeshPro (included with Unity)
- Node.js + npm (for building Figma plugin)

## Build Commands

### Figma Plugin
```bash
cd FigmaPlugin
npm install
npm run build    # Creates code.js from code.ts
```

## Key Files to Know

| File | Purpose |
|------|---------|
| `FigmaNodeBuilder.cs:SyncNode()` | Core recursive tree building |
| `FigmaTypes.cs` | All JSON DTOs |
| `UIScreenManager.cs` | Runtime navigation singleton |
| `FigmaVariantController.cs` | Variant state switching |
| `FigmaEntryAnimation.cs` | All animation types |
| `code.ts:exportTree()` | Figma node traversal |

## TODOs / Future Work

- [ ] Figma REST API direct import (FigmaApiClient.cs is placeholder)
- [ ] Add automated tests
