# Application icon

The KeySounds icon combines an ivory keyboard keycap with cyan sound waves on a midnight-navy tile. Transparent pixels around the rounded tile let it sit on light and dark Windows surfaces.

## Assets and integration

- [Source PNG](../Assets/Icons/keysounds.png): original generated artwork, 1254 × 1254 pixels with transparency. Kept as an editable source asset and excluded from the embedded application resources.
- [Windows ICO](../Assets/Icons/keysounds.ico): 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 pixel frames, preserving transparency.
- `KeySounds.csproj` sets the Windows executable's `ApplicationIcon` and embeds the ICO as an Avalonia resource.
- `MainWindow.axaml` loads that resource; the tray uses the same window icon instead of the former one-pixel placeholder.

Pillow was used only to package the generated PNG into ICO sizes. It is not an application or build dependency; the committed assets are sufficient to build and publish.

## Generation provenance

Created on 10 October 2026 with the built-in imagegen tool, with transparent-background output. No CLI/API fallback was used. The original generated image is retained outside the project; the project copy is the source of truth for builds.

Final generation prompt:

> Create one production-ready desktop application icon for KeySounds, an open-source Windows app that makes configurable keyboard click sounds. Square 1:1 composition, polished distinctive modern icon with clean vector-like shapes. A dark midnight-navy rounded squircle tile contains one large ivory keyboard keycap in the lower-left/center, with subtle bevel depth, and three bold turquoise/cyan audio waveform bars rising at its upper-right, visually connecting the key press with sound. Very simple confident silhouette, thick forms readable at 16 and 32 pixels, strong light/dark contrast, balanced spacing and generous safe margins. Restrained electric-blue edge highlight and very subtle soft depth; professional desktop software identity. Transparent canvas outside the squircle tile. No typography, no letters, no words, no watermark, no tiny keyboard grid, no mockup, no multiple variants, no decorative background or cast shadow outside the icon. Deliver only the isolated icon artwork.
