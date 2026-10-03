# Local reader dependencies

All renderer scripts, workers, fonts, CMaps and image decoders are shipped locally.
Reading never downloads executable code from a CDN.

## Foliate / Silo WebUI renderer

`vendor/foliate-js` is copied from the official public Silo Server repository,
`web/vendor/foliate-js`, at commit
`ad899be9d4fd9f33d4b9e9ac6873166026661c6d` (2026-09-28 parity reference):
https://github.com/Silo-Server/silo-server/tree/ad899be9d4fd9f33d4b9e9ac6873166026661c6d/web/vendor/foliate-js

Original project: https://github.com/johnfactotum/foliate-js
License: MIT, copyright John Factotum. Full text: `vendor/foliate-js/LICENSE`.
Its `README.md` and `package.json` retain upstream attribution and dependency
information. Vendored zip.js uses BSD-3-Clause (`vendor/LICENSE-zip.js`);
vendored fflate uses MIT (`vendor/LICENSE-fflate`). These license texts were
obtained from the zip.js v2.7.52 and fflate v0.8.2 official repositories, matching
the versions recorded by Foliate's manifest. The prebuilt vendored JavaScript is
the exact Silo reference, rather than a newly resolved dependency build.

Local adaptations:

- `pdf.js`: replace bundler-only PDF.js alias/global and public paths with local
  relative ES module, worker, stylesheet, CMap, font and WASM paths.
- `pdf.js`: present the actual PDF.js page bitmap as an inert PNG image in the
  scripts-disabled frame, retaining its selectable text and annotation layers.
  Release the intermediate canvas after decoding the image.
- `paginator.js`, `fixed-layout.js`: remove `allow-scripts` from ebook iframe
  sandboxes. Parent renderer access retains `allow-same-origin`; embedded book
  scripts, event handlers and forms are not trusted.
- `fixed-layout.js`: omit constructed-stylesheet polyfill; Evergreen WebView2
  supports that API natively.

CFI parsing/generation, EPUB parsing, section sizes, navigation and progress
algorithms are unchanged. The surrounding desktop bridge is in `reader.mjs`.

## PDF.js 6.2.108

Source project: https://github.com/mozilla/pdf.js
Distribution: https://registry.npmjs.org/pdfjs-dist/-/pdfjs-dist-6.2.108.tgz
The version matches the exact Silo reference's declared dependency. Only runtime
build modules, viewer stylesheet, CMaps, standard fonts, WASM and associated
licenses are included; source maps and test/example applications are omitted.

Archive SHA-256:
`b3e68d5cda70551a90b3f771419d379e20fc788ce056fa32de73608e01df47f4`

`build/pdf.min.mjs` SHA-256:
`e0be3863c23c8af2305b16548febd58e7f8874a460253317d7771cddbc1c0f6d`

`build/pdf.worker.min.mjs` SHA-256:
`0613f41490dd6aaceed7a93fbbd38c85e6d6aa60474b6588c6e7709cfbe18cb3`

License: Apache-2.0 (`vendor/pdfjs/LICENSE`). Additional font/decoder licenses
are retained alongside their files in `standard_fonts`, `cmaps` and `wasm`.
PDF document scripting and dynamic evaluation are disabled in Foliate's PDF
loader. The host denies external resources, permissions and new windows.
