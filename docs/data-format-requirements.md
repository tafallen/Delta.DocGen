# Delta.DocGen — Data Format Requirements

## Context

Delta.DocGen is a standalone tool that parses SpecFlow step-definition files (C#) and feature files, optionally enriches the extracted data via a locally-hosted LLM, and emits a structured data file. That file is the **sole contract** between this generator and the Delta.DocView viewer (a separate application). These requirements govern how that file must be defined, versioned, and secured.

---

## REQ-01 — Schema definition

The output file format must be formally specified so that both the generator and any conforming viewer can agree on structure without ambiguity.

### REQ-01.1 — JSON as the wire format

The output must be valid JSON. JSON is human-readable, diff-friendly in version control, and natively parseable by all target viewer platforms (.NET, browsers).

### REQ-01.2 — Published schema

A JSON Schema document (draft 2020-12 or later) must exist in this repository and be referenced by the output file. The schema must cover:

- All top-level envelope fields (version, metadata, signature)
- The complete `steps` array and every field on a step object
- The `domains` lookup array
- Enumerated values (step type: `Given` | `When` | `Then`; param type: `string` | `int` | `decimal` | `DocString`)
- Required vs optional fields
- String length constraints where relevant

### REQ-01.3 — Step object fields

Each step object must carry the following fields. Fields marked **required** must always be present and non-null.

| Field | Type | Required | Source |
|---|---|---|---|
| `id` | `string` | yes | generated (e.g. `auth-001`) |
| `type` | `"Given"\|"When"\|"Then"` | yes | parsed from C# attribute |
| `pattern` | `string` | yes | parsed from C# attribute |
| `params` | `Param[]` | yes | parsed from C# method signature |
| `file` | `string` | yes | relative path to `.cs` file |
| `line` | `integer` | yes | line number of attribute |
| `domain` | `string` | yes | inferred from folder/namespace |
| `tags` | `string[]` | yes | LLM-enriched; empty array if not enriched |
| `used` | `integer` | yes | count from feature file scan |
| `description` | `string` | yes | LLM-enriched; empty string if not enriched |
| `source` | `string` | yes | verbatim C# method body |
| `suggestsNext` | `string[]` | yes | co-occurrence + optional LLM; empty array if unavailable |

Each `Param` object:

| Field | Type | Required |
|---|---|---|
| `name` | `string` | yes |
| `type` | `string` (enum) | yes |
| `example` | `string` | yes (may be type-defaulted) |

### REQ-01.4 — Top-level envelope

The output file must wrap the data in an envelope that carries versioning and integrity fields alongside the payload:

```jsonc
{
  "$schema": "https://delta.docgen/schema/v1/step-library.schema.json",
  "version": "1.0.0",
  "generatedAt": "2026-05-26T09:00:00Z",
  "generatorVersion": "1.0.0",
  "enriched": true,
  "domains": [ { "id": "Auth", "label": "Auth & Identity" } ],
  "steps": [ /* step objects */ ],
  "signature": {
    "algorithm": "SHA-256",
    "digest": "<hex>"
  }
}
```

---

## REQ-02 — Versioning

### REQ-02.1 — Semantic version in the envelope

The `version` field in the envelope identifies the **schema version** of this file, independent of the generator tool version. It follows Semantic Versioning (semver):

- **MAJOR** — breaking change: a field was removed, renamed, or its type changed. The viewer must refuse to load a file whose major version it does not recognise.
- **MINOR** — additive change: new optional fields were added. A viewer built for v1.x must still load a v1.y file where y > x, ignoring unknown fields.
- **PATCH** — non-structural fix (e.g. corrected enum list in the schema doc only). No change to file content.

### REQ-02.2 — Generator version in the envelope

The `generatorVersion` field records the version of Delta.DocGen that produced the file. This is informational and must not be used by the viewer to make load/reject decisions.

### REQ-02.3 — Viewer enforcement

The viewer must:

1. Read the `version` field before processing any other content.
2. Reject files whose major version is higher than the highest major version the viewer was built to handle, with a user-visible error message.
3. Accept files whose minor or patch version is higher than expected (forward-compatible, ignore unknown fields).
4. Accept files whose minor or patch version is lower than expected (backward-compatible).

### REQ-02.4 — Schema document versioning

The JSON Schema file must live at a version-namespaced path, e.g.:

```
docs/schema/v1/step-library.schema.json
```

When a major version increment occurs, the old schema file must be preserved and a new versioned directory created. This allows the viewer to validate against the exact schema version it targets.

---

## REQ-03 — Integrity and tamper-evidence

The goal is to ensure the viewer can detect accidental corruption or deliberate modification of the output file. This is **tamper-evidence**, not encryption or access control.

### REQ-03.1 — Digest computation

Before writing the file, the generator must:

1. Serialise the complete JSON payload **without** the `signature` object, using canonical (deterministic) serialisation: keys sorted alphabetically, no insignificant whitespace.
2. Compute a SHA-256 digest of the UTF-8 bytes of that canonical form.
3. Encode the digest as a lowercase hex string.
4. Insert the `signature` block into the envelope and write the final file (which may use pretty-printed formatting).

### REQ-03.2 — Digest verification

On load, the viewer must:

1. Extract and remove the `signature` block from the parsed document.
2. Re-serialise the remaining document using the same canonical rules.
3. Compute SHA-256 of the result and compare to the stored digest.
4. Refuse to import the file if the digest does not match, with a clear error message indicating possible corruption or tampering.

### REQ-03.3 — Algorithm agility

The `signature.algorithm` field must be present so that a future version can introduce a stronger algorithm (e.g. SHA-3-256) without breaking older viewers. Viewers must refuse files whose `algorithm` value they do not recognise.

### REQ-03.4 — Scope of protection

The digest covers the entire envelope including `version`, `generatedAt`, `generatorVersion`, `enriched`, `domains`, and `steps`. It does not provide authorship proof (no private key is involved); it only guarantees the file has not changed since the generator wrote it.

---

## Non-requirements (explicit exclusions)

- **Encryption** — the file is not encrypted. Access control is the deployment environment's responsibility.
- **Digital signatures** — asymmetric signing (e.g. RSA, ECDSA) is out of scope for v1. The digest is sufficient to detect corruption.
- **Streaming / incremental formats** — the file is written and read as a single unit.
- **Binary formats** — MessagePack, Protobuf, etc. are out of scope; JSON readability is a requirement.
