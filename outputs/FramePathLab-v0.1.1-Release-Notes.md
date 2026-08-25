# FramePath Lab v0.1.1

Unsigned Windows x64 developer preview.

## Fixed

- Fixed the startup failure: `A TwoWay or OneWayToSource binding cannot work on the read-only property 'DataDirectory'`.
- Explicitly marked all inline read-only WPF bindings as one-way.
- Marked the read-only capture-path display as one-way to prevent accidental UI write-back.

## Verification

- Release build completed with 0 warnings and 0 errors.
- All 8 automated tests passed.
- Framework-dependent startup smoke test passed.
- Published self-contained startup smoke test passed and remained running after window construction.
- Published executable reports product version `0.1.1`.

## Running

1. Extract the entire ZIP to a normal local folder.
2. Run `FramePathLab.exe` from the extracted folder.
3. Do not run the executable from inside the ZIP preview.

This build remains read-only and does not modify Windows, CS2, drivers, the registry, services, security settings or network configuration.
