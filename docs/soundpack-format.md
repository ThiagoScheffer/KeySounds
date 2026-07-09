# KeySounds sound-pack format

## Layout

```text
my-pack/
  manifest.json
  samples/
    click.wav
```

## Manifest example

```json
{
  "schemaVersion": 1,
  "id": "my-pack",
  "name": "My Pack",
  "author": "Your Name",
  "description": "Short description",
  "soundSets": [
    {
      "id": "click",
      "name": "Click",
      "files": [ "samples/click.wav" ]
    }
  ]
}
```

## Rules

- Pack IDs use lowercase letters, numbers, hyphens, or underscores.
- Paths must be relative.
- Paths may not escape the pack directory.
- Only `.wav` files are supported.
- Files must be short keyboard-sound samples.
- Duplicated sound-set IDs and duplicated file references are rejected.
- Source folders are never modified during import.

## Troubleshooting validation

- `ManifestMissing`: add `manifest.json`.
- `ManifestMalformed`: fix JSON syntax.
- `InvalidPackId`: use a lowercase safe ID.
- `SampleMissing`: verify referenced WAV paths.
- `UnsupportedExtension`: use WAV files.
- `PathTraversal`: remove `..` or absolute paths.
- `InvalidWave`: re-export the WAV with a standard PCM format.
