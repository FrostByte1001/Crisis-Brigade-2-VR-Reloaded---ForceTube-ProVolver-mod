# Crisis-Brigade-2-VR-Reloaded---ForceTube-ProVolver-mod
This mod adds ForceTube VR haptic feedback support to Crisis VRigade 2.  At the time of writing this game does not actually have working support for the ForceTube nor Provolver haptic devices on Steam OS,
though an old ForceTubeVr_API dll is shipped, there is no actual usage of the API by the game.

This mod implements ForceTube/Provolver haptics for Rifles, single pistols and very likely dual wielding pistols (I only have one ProVolver to test, but it should work)

REQUIREMENTS:
- Crisis VRigade 2 on steam os. Paid for licenced version.
- BepInEx 6.x for Unity IL2CPP (must be installed separately)
- ForceTube or Provolver VR hardware (optional but the whole point really)

INSTALLATION:
1. Install BepInEx 6.x for Unity IL2CPP into Crisis VRigade 2 first
   Download from: https://github.com/BepInEx/BepInEx/releases
   (Look for BepInEx_UnityIL2CPP_x64_...) and run the game to create folders

3. Extract THIS ZIP to your Crisis VRigade 2 game folder
   Default location e.g: C:\Program Files (x86)\Steam\steamapps\common\Crisis VRigade 2\

4. The files should be found as below after extraction into the games root folder:
   Crisis VRigade 2\
   ├── BepInEx\
   │   ├── plugins\
   │   │   └── ForceTubePatch.dll

   └── ForceTubeVR_API_x64.dll

ForceTubeVR_API_x64.dll is the latest at the time of writing.  It comes from ProTubeVr themselves.  Alternatively you can get it on steam (for free) by installing the "ProTubeVR Companion-App" then copy from (e.g.) .\SteamLibrary\steamapps\common\ProTubeVR Companion-App\build

It must be copied to the games root folder.
