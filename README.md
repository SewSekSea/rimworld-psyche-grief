# Grief

Losing someone should not be a mood penalty that quietly runs out.

In the base game, a colonist whose wife dies carries a flat penalty for thirty days and then is fine. This mod gives grief a shape instead.

A standalone T&B Modding Psyche mod. Source publication prepared by SweetBriana & Cinder.

- Package ID: `grief.mourning`
- Supported RimWorld versions declared by the current metadata: 1.6, 1.7
- Dependencies: `brrainz.harmony`
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3805078287

## Source status

This repository is a source snapshot for the standalone mod. It is not a new Workshop update or a claim that every declared game version was newly playtested. See BUILD-STATUS.md for fresh compilation results and their limits. No game assemblies, personal settings, saves or private test profiles are included.

## Build

Supply your own licensed RimWorld installation and a matching Harmony assembly. A .NET SDK is required.

For the original net472 / legacy RimWorld 1.6 project:

```powershell
./Build16.ps1 -GameDir 'PATH/TO/RIMWORLD-1.6' -HarmonyDll 'PATH/TO/1.6/0Harmony.dll'
```

For a RimWorld 1.7 build against its root-level managed API:

```powershell
./Build17.ps1 -GameDir 'PATH/TO/RIMWORLD-1.7' -HarmonyDll 'PATH/TO/1.7/0Harmony.dll'
```

The 1.7 helper writes only to `.build/1.7` inside this repository. Native validation and installation are separate steps. The 1.6 helper uses the original project and may restore its Microsoft.NETFramework reference-assemblies package from NuGet.

## License

Original source code and XML definitions are published under MIT. See LICENSE, ASSETS.md and CREDITS.md. Artwork rights are separate.
