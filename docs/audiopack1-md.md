You add custom key audio by creating/importing a **sound pack folder** with a `manifest.json` plus `.wav` files, then selecting those sound sets in a profile.

## 1. Create a sound pack folder

Example folder:

```text
my-custom-pack/
├─ manifest.json
└─ samples/
   ├─ alpha-01.wav
   ├─ alpha-02.wav
   ├─ space-01.wav
   ├─ enter-01.wav
   └─ backspace-01.wav
```

Use short WAV files. Best target format:

```text
WAV
48,000 Hz
mono or stereo
PCM or IEEE float
5 ms to 5 seconds
small files, ideally under a few hundred KB each
```

For keyboard sounds, practical duration is usually:

```text
20–150 ms
```

## 2. Add a `manifest.json`

Example:

```json
{
  "schemaVersion": 1,
  "id": "my-custom-pack",
  "name": "My Custom Pack",
  "author": "Me",
  "description": "Custom keyboard click sounds",
  "soundSets": [
    {
      "id": "alpha-down",
      "name": "Alpha Key Down",
      "files": [
        "samples/alpha-01.wav",
        "samples/alpha-02.wav"
      ]
    },
    {
      "id": "space-down",
      "name": "Spacebar Down",
      "files": [
        "samples/space-01.wav"
      ]
    },
    {
      "id": "enter-down",
      "name": "Enter Key Down",
      "files": [
        "samples/enter-01.wav"
      ]
    },
    {
      "id": "backspace-down",
      "name": "Backspace Key Down",
      "files": [
        "samples/backspace-01.wav"
      ]
    }
  ]
}
```

Important rules:

```text
id must use lowercase letters, numbers, hyphen, or underscore.
Example: my-custom-pack
Do not use spaces in the pack id.
File paths must be relative.
Do not use absolute paths.
Do not use ../ path traversal.
Only referenced WAV files are imported.
```

Valid pack ID examples:

```text
deep-linear
typewriter_v1
soft-clicks
my_pack_01
```

Invalid examples:

```text
My Custom Pack
my custom pack
../pack
C:\sounds\pack
```

## 3. Import it in the app

In the app:

```text
Sound Packs → Import sound pack → select the folder containing manifest.json
```

Select the **folder itself**, not the `samples` folder.

So select:

```text
my-custom-pack/
```

Not:

```text
my-custom-pack/samples/
```

If import succeeds, the pack appears in the Sound Packs page.

## 4. Assign sounds to key groups

Go to:

```text
Profiles → select or create profile → Key Groups
```

For each group, choose:

```text
Sound Pack: My Custom Pack
Key-down sound set: alpha-down / space-down / enter-down / etc.
```

Example mapping:

| Key group | Sound set                       |
| --------- | ------------------------------- |
| Alpha     | `alpha-down`                    |
| NumberRow | `alpha-down` or `number-down`   |
| Spacebar  | `space-down`                    |
| Enter     | `enter-down`                    |
| Backspace | `backspace-down`                |
| Modifier  | `alpha-down` or `modifier-down` |
| Arrow     | `alpha-down` or `arrow-down`    |

Then click:

```text
Save and Activate
```

## 5. Multiple variations

To avoid robotic repetition, put multiple files in one sound set:

```json
{
  "id": "alpha-down",
  "name": "Alpha Key Down",
  "files": [
    "samples/alpha-01.wav",
    "samples/alpha-02.wav",
    "samples/alpha-03.wav",
    "samples/alpha-04.wav"
  ]
}
```

Then in the profile editor, set variation mode to:

```text
Avoid Immediate Repeat
```

That is usually the best default.

## 6. Key-up sounds

You can also create release sounds:

```json
{
  "id": "alpha-up",
  "name": "Alpha Key Up",
  "files": [
    "samples/alpha-up-01.wav",
    "samples/alpha-up-02.wav"
  ]
}
```

Then in the profile:

```text
Enable Play on Key Up
Set Key-up sound set: alpha-up
Save and Activate
```

## 7. Per-key custom sound

For one specific key:

```text
Profiles → Per-Key Overrides → Capture Key
```

Press the key once, then assign a custom sound set for that key.

Example:

```text
Spacebar → space-down
Enter → enter-down
Escape → escape-click
Backspace → backspace-heavy
```

This stores only the physical key identity, not typed text or key sequences.

## Minimal working pack

This is the smallest valid pack:

```text
minimal-pack/
├─ manifest.json
└─ samples/
   └─ click.wav
```

```json
{
  "schemaVersion": 1,
  "id": "minimal-pack",
  "name": "Minimal Pack",
  "author": "Me",
  "description": "One sound used for all keys",
  "soundSets": [
    {
      "id": "click",
      "name": "Click",
      "files": [
        "samples/click.wav"
      ]
    }
  ]
}
```

Then assign `click` to every key group.

## Best practical recommendation

Use one sound set per major key type:

```text
alpha-down
space-down
enter-down
backspace-down
modifier-down
arrow-down
key-up
```

And put 3–10 variations inside each frequently used set, especially `alpha-down`.

That gives the best realism without making the pack large.
