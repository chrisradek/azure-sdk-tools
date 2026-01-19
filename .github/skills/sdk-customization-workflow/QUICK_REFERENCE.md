# SDK Customization Workflow Quick Reference

## Workflow Loop Pattern

```
┌─────────────────────────────────────────────────────────┐
│  1. Call azsdk_sdk_customization_workflow               │
│  2. Execute the requested action                        │
│  3. Call azsdk_sdk_customization_workflow with result   │
│  4. Repeat until isComplete: true                       │
└─────────────────────────────────────────────────────────┘
```

## Result JSON Templates

### Classification
```json
{"type": "classification", "tspApplicable": true}
{"type": "classification", "tspApplicable": false}
```

### TypeSpec Fix
```json
{"type": "tsp_fix_applied", "description": "what was changed"}
{"type": "tsp_fix_not_applicable", "reason": "why it can't help"}
```

### SDK Fix
```json
{"type": "sdk_fix_applied", "description": "what was changed"}
{"type": "sdk_fix_failed", "reason": "why it failed"}
```

### Generate/Build
```json
{"type": "generate_complete", "success": true, "output": "..."}
{"type": "generate_complete", "success": false, "errors": "..."}
{"type": "build_complete", "success": true}
{"type": "build_complete", "success": false, "errors": "..."}
```

## Phase Flow

```
Classify ──┬── tspApplicable=true ──► AttemptTspFix ──┬── applied ──► Regenerate ──► Build
           │                                          │
           │                                          └── not applicable ──┐
           │                                                                │
           └── tspApplicable=false ─────────────────────────────────────────┴──► AttemptSdkFix ──► Build

Build ──┬── success ──► Success (done)
        │
        └── failure ──┬── iterations remain ──► Classify (retry)
                      │
                      └── max iterations ──► Failure (done)
```

## Tools Used

| Tool | Purpose |
|------|---------|
| `azsdk_sdk_customization_workflow` | Workflow coordinator |
| `azsdk_sdk_fix` | Apply SDK code patches |
| `azsdk_package_generate` | Regenerate SDK from TypeSpec |
| `azsdk_package_build` | Build/compile SDK package |
