# Marketing Assets Guide

This folder contains templates and guides for creating store listing images.

## Quick Reference - Required Sizes

### Figma Community Plugin
| Asset | Dimensions | Format |
|-------|------------|--------|
| Cover Image | 1920 × 1080 px | PNG |
| Screenshots (3-5) | 1920 × 1080 px | PNG |

### Unity Asset Store
| Asset | Dimensions | Format |
|-------|------------|--------|
| Icon | 160 × 160 px | PNG (no alpha) |
| Card Image | 420 × 280 px | PNG (no alpha) |
| Cover/Key Image | 1950 × 1300 px | PNG (no alpha) |
| Social Media | 1200 × 630 px | PNG (no alpha) |
| Screenshots (5-10) | 2400 × 1600 px | PNG (no alpha) |

**Unity requirements:** 24-bit PNG, no alpha channel, 3:2 aspect ratio

---

## How to Use the SVG Templates

1. Open Figma
2. File > Import (or drag the SVG file into Figma)
3. The frames will import at the correct sizes
4. Replace placeholder content with your actual screenshots
5. Export each frame as PNG

---

## Figma Community Plugin Screenshots

### Cover Image (1920 × 1080)
**Content:** Show the plugin window alongside a Figma design
- Plugin UI on the right side (~400px wide)
- Figma canvas with a nice UI design on the left
- Dark Figma theme recommended for contrast
- Add subtle glow/highlight around the plugin window

### Screenshot 1: Export Flow
**Content:** Step-by-step export demonstration
- Frame selected in Figma
- Plugin window open
- Arrow pointing to "Export Selected Frame" button
- Status showing "Export ready"

### Screenshot 2: Layer Naming
**Content:** Show layer naming conventions in action
- Figma layers panel visible
- Highlight BTN_, IMG_, CTN_, TEXT prefixes
- Corresponding Unity result preview

### Screenshot 3: Output Files
**Content:** Show what gets exported
- ZIP file contents
- *_Screen.json file
- *_Images/ folder with PNGs

---

## Unity Asset Store Screenshots

### Icon (160 × 160)
**Content:** Simple, recognizable logo
- Figma logo → Unity logo with arrow
- Or stylized "F→U" design
- Bold, readable at small size
- Avoid fine details

### Card Image (420 × 280)
**Content:** Condensed version of cover
- Same visual style as cover
- "Figma to Unity" text
- Small UI preview

### Cover/Key Image (1950 × 1300)
**Content:** Hero shot showing the full workflow
- Left side: Figma design
- Center: Arrow or transition effect
- Right side: Unity Editor with imported prefab
- Title overlay: "Figma Importer for Unity"
- Subtitle: "Import Figma designs as UGUI prefabs"

### Social Media (1200 × 630)
**Content:** Clean promotional image
- **NO text or logos** (for Unity promo eligibility)
- Just the visual: Figma design → Unity prefab
- High contrast, eye-catching

### Screenshot 1: Importer Window
**Capture:** Tools > Figma > Import to Unity...
- Full importer window visible
- All settings expanded
- JSON file assigned
- Show import options

### Screenshot 2: Before & After
**Content:** Side-by-side comparison
- Left: Figma design (screenshot from Figma)
- Right: Unity Game view with same design
- Visual proof of fidelity

### Screenshot 3: Hierarchy View
**Capture:** Unity Hierarchy panel
- Expanded prefab structure
- Show Canvas, screens, buttons, images
- Highlight organized naming

### Screenshot 4: Component Inspector
**Capture:** Inspector panel with components
- Select a button with FigmaEntryAnimation
- Or FigmaVariantController for variants
- Show the component settings

### Screenshot 5: Auto-Layout
**Content:** Layout system conversion
- Figma auto-layout frame
- Corresponding VerticalLayoutGroup/HorizontalLayoutGroup
- Show spacing, padding preserved

### Screenshot 6: TextMeshPro
**Content:** Text styling preservation
- Figma text with specific font, size, color
- Unity TMP text matching exactly
- Inspector showing TMP settings

### Screenshot 7: Entry Animations
**Content:** Animation preview
- Animated GIF or sequence showing PopIn/FadeIn
- Or Inspector view of FigmaEntryAnimation component
- Show stagger delay settings

### Screenshot 8: Screen Navigation
**Content:** Multi-screen workflow
- UIScreenManager in hierarchy
- Multiple screen prefabs
- FigmaNavigationButton wiring

### Screenshot 9: Re-import Safety
**Content:** Demonstrate preserved handlers
- Button with onClick event configured
- Show it survives re-import
- Before/after comparison

---

## Capture Tips

### For Figma Plugin:
1. Use Figma's screenshot feature or OS screenshot tool
2. Dark theme photographs better
3. Zoom to 100% for crisp UI
4. Close unnecessary panels

### For Unity Editor:
1. Use Window > Screenshot or Game view screenshot
2. Set Game view to 16:10 aspect (close to 3:2)
3. Use dark Unity theme for consistency
4. Maximize windows to reduce clutter
5. Consider using Unity Recorder for animated GIFs

### Post-Processing:
1. Resize to exact dimensions
2. Save as 24-bit PNG (no alpha for Unity)
3. Optimize file size (TinyPNG)
4. Add subtle drop shadows to floating elements

---

## File Checklist

### Figma Community
- [ ] figma-cover.png (1920 × 1080)
- [ ] figma-screenshot-1.png (1920 × 1080)
- [ ] figma-screenshot-2.png (1920 × 1080)
- [ ] figma-screenshot-3.png (1920 × 1080)

### Unity Asset Store
- [ ] unity-icon.png (160 × 160)
- [ ] unity-card.png (420 × 280)
- [ ] unity-cover.png (1950 × 1300)
- [ ] unity-social.png (1200 × 630)
- [ ] unity-screenshot-01.png (2400 × 1600)
- [ ] unity-screenshot-02.png (2400 × 1600)
- [ ] unity-screenshot-03.png (2400 × 1600)
- [ ] unity-screenshot-04.png (2400 × 1600)
- [ ] unity-screenshot-05.png (2400 × 1600)
- [ ] unity-screenshot-06.png (2400 × 1600)
- [ ] unity-screenshot-07.png (2400 × 1600)
- [ ] unity-screenshot-08.png (2400 × 1600)
- [ ] unity-screenshot-09.png (2400 × 1600)
