# GitHub releases

Publish this source repository publicly as TokenTaskbar. Generated binaries, account caches, settings and diagnostics are excluded from Git. Release packages use an explicit allowlist rather than archiving the project folder.

For the first preview, push the source to GitHub, then create and push a version tag such as v0.1.0. The Windows release workflow builds and checks the application, uploads the ZIP and SHA-256 file, and creates a draft release. Review the draft and publish it to make the download available. A manual workflow run builds downloadable artifacts without publishing a release.

For a local build:

```powershell
python -m pip install -r source/requirements-build.txt
./source/build.ps1
python -m unittest discover -s source -v
./source/package-release.ps1 -Version v0.1.0
```

Keep code-signing and third-party icon permissions on the release checklist before promoting beyond preview. No project license has been selected yet.
