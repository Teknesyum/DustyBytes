# Diagrams

Every main flow described in the README has one diagram here. A new flow adds a section.

## Scan To Removal

```mermaid
flowchart LR
  A[Scan] --> B[Usage Signals]
  B --> C[Units]
  C --> D[Score]
  D --> E[Your Choice]
  E --> F[Worker Checks Protected List]
  F --> G[Quarantine Or Recycle Bin]
```

Scan, then usage signals, then units, then score, then the user's choice, then the worker's protected list check, then quarantine or the recycle bin.

## UI And Worker

```mermaid
sequenceDiagram
  participant UI as UI Process
  participant W as Elevated Worker
  participant P as Protected List
  UI->>W: Start With Parent Id And UAC
  UI->>W: Request Over Named Pipe
  W->>W: Check Caller Id And User Sid
  W->>P: Check Every Path
  P-->>W: Allowed Or Denied With Reason
  W-->>UI: Progress And Item Results
```

The UI starts the worker once through UAC, sends a request over the named pipe, the worker checks the caller and the user SID, checks every path against the protected list, and returns progress and a result per item.

## Uninstall

```mermaid
flowchart LR
  A[Pick Program] --> B[Restore Point]
  B --> C[Registry Export]
  C --> D[Vendor Uninstaller]
  D --> E[Leftover Scan]
  E --> F[Score High Medium Low]
  F --> G[Your Choice]
  G --> H[Quarantine]
```

The user picks a program, a restore point and a registry export are made, the vendor uninstaller runs, leftovers are scanned and scored, the user chooses, and the chosen leftovers go to quarantine.

## Quick Rescan

```mermaid
flowchart LR
  A[Saved Scan And USN Cursor] --> B[Read Journal Since Cursor]
  B --> C{Journal Intact}
  C -- Yes --> D[Apply Changes To Tree]
  C -- No --> E[Full MFT Scan]
  D --> F[New Cursor]
  E --> F
```

The saved scan and its USN cursor are loaded, the journal is read since the cursor, changes are applied to the tree if the journal is intact, otherwise a full MFT scan runs, and a new cursor is saved.
