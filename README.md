# Figma Importer for Unity

Import Figma designs directly into Unity as fully functional UGUI prefabs.

## Features

- **Direct Import**: Import from JSON exports or (coming soon) directly from Figma URLs
- **Auto-Layout**: Figma's auto-layout converts to Unity LayoutGroups
- **Constraints**: Responsive anchoring based on Figma constraints
- **Components & Variants**: Figma components become Unity prefabs with state management
- **Text Styling**: Full TextMeshPro support with fonts, sizes, colors, alignment
- **9-Slice Images**: Scalable UI backgrounds
- **Prototype Navigation**: Auto-wired button navigation from Figma prototypes
- **Entry Animations**: Automatic staggered animations based on layer prefixes
- **Re-import Safety**: Preserves onClick handlers and custom scripts

## Installation

### Unity Package Manager (Recommended)
1. Open Package Manager (Window > Package Manager)
2. Click "+" > "Add package from disk..."
3. Navigate to `FigmaImporter/package.json`

### Manual Installation
Copy the `Assets/FigmaImporter` folder into your project's Assets folder.

## Quick Start

1. **Export from Figma**: Use the Figma plugin to export your design as JSON + images
2. **Import to Unity**: Open Tools > Figma > Import to Unity...
3. **Select JSON**: Drag your `*_Screen.json` file into the field
4. **Import**: Click "Import / Rebuild Prefab"

## Layer Naming Conventions

| Prefix | Behavior |
|--------|----------|
| `BTN_` | Button (baked as image, receives clicks) |
| `IMG_` | Image (baked as PNG sprite) |
| `CTN_` | Container (recurses into children, no image) |
| `9SLICE_` | 9-slice scalable background |
| `TEXT` | Native text (uses TextMeshPro) |

## Entry Animations

Based on layer prefixes, elements automatically animate on screen entry:

- `BTN_` → PopIn (springy bounce)
- `IMG_` / `ICON_` → FadeIn
- `CTN_` → FadeSlideUp

Animations are staggered by sibling order for a cascading effect.

## Screen Management

The UIScreenManager component handles navigation between screens:

```csharp
// Navigate to a screen
UIScreenManager.Show("HomeScreen");

// Go back
UIScreenManager.Back();

// Check current screen
string current = UIScreenManager.CurrentScreen;
```

## Requirements

- Unity 2021.3 or later
- TextMeshPro (included with Unity)

## Support

For issues and feature requests, please visit the GitHub repository.
