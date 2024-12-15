# FixShaderRightEye
Editor script to perform common VR shader code upgrades for Unity 6.

One of the common symptoms this fixes is that a shader only shows in the left eye in VR.

The technical reason is that there should be some macros called at the beginning of the vertex/geometry/fragment shader to set variables indicating which eye you're rendering for. This script currently upgrades vertex and fragment shaders as geometry shaders are much less common.
