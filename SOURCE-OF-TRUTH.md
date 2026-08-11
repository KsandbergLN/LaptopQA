# Laptop QA source of truth

The canonical editable source is `D:\LaptopQA\V4`.

- Product identity remains `LaptopQATestingV4`; the folder name is not a release-selection mechanism.
- `D:\LaptopQA\Shared` and the launcher/sync scripts at the handoff root are dependencies of the canonical source and are included in the same repository.
- `D:\LaptopQA\V5`, `D:\LaptopQA\Mac`, prototypes, `C:\V2`, recovery folders, build checks, `bin`, `obj`, and `dist` are not editable source.
- Historical alternatives are read-only by policy. Copy one to a reviewed branch before reusing any content.

Only packages with an `Accepted` `package-manifest.json` may be deployed. A folder name, timestamp, or executable alone does not establish acceptance.
