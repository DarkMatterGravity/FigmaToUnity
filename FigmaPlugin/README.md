# Figma to Unity UGUI Exporter - Figma Plugin

This Figma plugin exports designs to Unity UGUI-compatible JSON + PNG bundles.

## Installation

### Option 1: Install from Figma Community (Recommended)
1. Search for "Figma to Unity UGUI Exporter" in the Figma Community
2. Click Install

### Option 2: Development Mode
1. Open Figma Desktop
2. Go to Plugins > Development > Import plugin from manifest...
3. Select the `manifest.json` file in this folder

## Building from Source

```bash
# Install dependencies
npm install

# Build the plugin
npm run build
```

This creates `code.js` from `code.ts`.

## Usage

1. Select a frame in Figma
2. Run the plugin (Plugins > Figma to Unity UGUI Exporter)
3. Click "Export Selected Frame" or "Export All Frames"
4. Download the ZIP file

## Layer Naming Conventions

| Prefix | Behavior |
|--------|----------|
| `CTN_` | Container only (no image, recurses into children) |
| `BTN_` | Baked as image + Unity Button component |
| `IMG_` | Baked as composite PNG image |
| `ICON_` | Baked as icon PNG image |
| `9SLICE_` | 9-slice scalable background |
| `TEXT` | Native text (TextMeshPro, not baked) |

## Export Output

The plugin generates:
- `{FrameName}_Screen.json` - Hierarchical node data
- `{FrameName}_Images/` - Baked PNG images

Import these into Unity using the FigmaImporter Unity package.

## Save to Folder Feature

For direct file saving (bypassing ZIP download):

1. Run the helper script:
   ```bash
   python figma_export_helper.py
   ```

2. Enter your Unity project's Assets path in the plugin
3. Click "Save to Folder"

## JSON Export Format (v7)

Nodes contain:
- `id`, `name`, `kind` (container/image/text/9slice)
- `x`, `y`, `w`, `h`, `rotation`, `visible`
- `constraints` (horizontal/vertical anchoring)
- `layout` (auto-layout settings)
- `text` (font, size, color, alignment)
- `image` (file reference)
- `interactions` (prototype navigation)
- `components`/`componentSets` (for variants)

## License

Part of the Figma Importer for Unity package.
