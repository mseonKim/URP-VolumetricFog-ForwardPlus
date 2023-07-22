# Universal Forward+ Volumetric Fog (Global)

This repository is copied from Unity HDRP Volumetric Fog.

Note that this feature is working as a global fog volume. So you can't place local fog volumes while HDRP reqruies them.

This feature is available on URP Forward+ from 2022.3.0f1 (2022 LTS) version.


## Limitations
1. XR not supported
2. Additional Directional & Local(point & spot) lights are only available in Forward+
   
   (In other words, only MainLight is working for Forward)
   
3. Additional light shadows are not supported due to performance
4. DiffuseGI not contributes to lighting