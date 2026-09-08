# Windows operation safety

- The application manifest is `asInvoker`; Horizon does not run the entire UI as administrator.
- Administrative commands use an individual `runas` PowerShell process after the Horizon explanation modal.
- Registry value operations preserve whether a value existed, its value, and its value type.
- Power-plan restore records the exact prior scheme GUID.
- Service restore records startup and running state.
- Fortnite operations back up `GameUserSettings.ini` and modify only the targeted key.
- The engine skips duplicate writes when the desired value is already present.
- Apply and restore are read back and verified; failures are recorded, not presented as success.
- Cleanup is limited to named temporary/cache roots, skips reparse points, and requires explicit category selection.
- AppX removal excludes frameworks, non-removable packages, and Horizon's protected keep list.
- Startup changes retain a local backup before removing a Run value or moving a Startup-folder item.
- Firmware, voltage, clock, and generic router changes remain review-only because Windows cannot apply them safely across arbitrary hardware.
