# BIMlify for Revit 2024

BIMlify is a Revit 2024 add-in providing project review, QA/QC, smart sheets, view templates, openings, clash, export, selection and BIM-data workflows.

## Repository layout

- `Source/` — C# source for the BIMlify assemblies.
- `Branding/` and `Icons/` — runtime visual assets.
- Root `BIMlify.*.dll` files — current deployment assemblies used by the add-in package.

## Build requirements

- Autodesk Revit 2024 with `RevitAPI.dll` and `RevitAPIUI.dll` available.
- .NET Framework 4.0-compatible C# build tools.
- Windows Forms.

The source projects use absolute Revit 2024 reference paths. Update those paths if Revit is installed elsewhere. Revit must be restarted after replacing any loaded BIMlify DLL.
