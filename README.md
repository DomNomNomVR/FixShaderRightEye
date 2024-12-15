# FixShaderRightEye
Editor script to perform common VR shader code upgrades for Unity 6.

One of the common symptoms this fixes is an object only showing in the left eye when viewed in VR.

## How to use this:
- Import `FixShadersRightEye.cs` into an `EditorScript` directory
- In the top toolbar: `Tools > DomNomNom > FixShadersRightEye`
- Select which shaders to upgrade, push the button to upgrade them.

## How things work:
The technical reason is that there should be some macros called at the beginning of the vertex/geometry/fragment shader to set variables indicating which eye you're rendering for. This script currently upgrades vertex and fragment shaders as geometry shaders are much less common. This script does rudimentary source code parsing to identify places where these macros should be called.

