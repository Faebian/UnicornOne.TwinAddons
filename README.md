# UnicornOne Twin Addons

External, independently built extensions for the **UnicornOne digital twin**. This repository contains two example GaN surface addons that change simulated surface behaviour without modifying or rebuilding the UnicornOne host.

The host supplies the simulation engine, addon loader, shared surface state, RHEED and laser-reflectance outputs, and the **Twin Cockpit** GUI. The addons are compiled separately and loaded from `Plugins\Twin` when UnicornOne starts.

> **Simulation only.** These extensions are designed for the UnicornOne Twin, not for control of physical hardware. The Twin API rejects configuration changes when `DeveloperServices.Simulation_SimulationMode` is false.

## Included addons

| Project / output DLL | Purpose | Default state |
| --- | --- | --- |
| `GaNCleanliness/GaNCleanliness.csproj` → `GaNCleanliness.dll` | Stateful surface cleanliness; slows droplet desorption when the surface is less clean | Disabled |
| `GaNLinearBilayerDesorption/GaNLinearBilayerDesorption.csproj` → `GaNLinearBilayerDesorption.dll` | Overrides bilayer coverage kinetics to give the RHEED recovery a real-temperature-dependent *central intensity slope*, with eased ends | Disabled |

### GaN Cleanliness

- Cleanliness is initialised to **0.1** for a new GaN surface and capped at **1.0**.
- It increases by **0.025 once per surface transition**: bilayer formed, droplets formed, droplets fully desorbed, and bilayer fully desorbed. A complete four-stage cycle therefore adds **0.1**.
- Low cleanliness **reduces the physical droplet desorption rate**. This prolongs droplet removal, which in turn delays the onset of bilayer desorption; it does not add an artificial stationary wait.
- The maximum additive duration penalty is configurable. With the default **10 s**, the penalty at cleanliness 0.1 is **9 s** and approaches zero at cleanliness 1.0.
- The *current* cleanliness is a **read-only live value** in the Cockpit. It is simulation state, not a persisted configuration setting.

Configurable parameters:

| Twin API parameter path | Default | Description |
| --- | ---: | --- |
| `Addons.GaNCleanliness.IncrementPerStage` | `0.025` | Increase for each completed stage |
| `Addons.GaNCleanliness.MaximumDropletDelaySeconds` | `10.0` | Extra droplet-desorption time at cleanliness 0 |
| `Addons.GaNCleanliness.Cleanliness` | `0.1` | **Read-only** current surface cleanliness |

### GaN Linear Bilayer Desorption

This addon replaces the *bilayer coverage trajectory*, not the displayed intensity trace directly. It uses the inverse of the existing smoothstep RHEED coverage-to-intensity response so that the **central RHEED intensity recovery gradient** is governed by actual simulated substrate temperature. The start and finish remain curved; laser reflectance observes the resulting shared surface-coverage change as well.

Its central target gradient is:

```text
slope(T) = ReferenceSlopeIntensityPerSecond
         + SlopeTemperatureCoefficient * (RealSubstrateTemperatureC - ReferenceTemperatureC)
```

The implementation constrains the resulting slope to be positive. Temperature is sampled during the evolution rather than only at the start.

| Twin API parameter path | Default | Units |
| --- | ---: | --- |
| `Addons.GaNLinearBilayerDesorption.ReferenceTemperatureC` | `690` | °C |
| `Addons.GaNLinearBilayerDesorption.ReferenceSlopeIntensityPerSecond` | `3.0` | intensity a.u./s |
| `Addons.GaNLinearBilayerDesorption.SlopeTemperatureCoefficient` | `0.02` | intensity a.u./(s·°C) |
| `Addons.GaNLinearBilayerDesorption.EdgeFraction` | `0.12` | fraction of transition |

The illustrative defaults are starting points for experiments, not calibrated physical constants. Check actual gradients against RHEED time-series measurements.

## Requirements

- **Windows** with **Visual Studio 2022** or the equivalent MSBuild tools.
- **.NET Framework 4.7.2 targeting pack**.
- **C# 7.3** (both projects target `AnyCPU`).
- **VS Code** with the Microsoft C# and PowerShell extensions, if using the supplied workspace and tasks.
- A built UnicornOne host with the **Phase 3 Twin addon contracts and loader** installed. The addon projects must be compiled against the host's current `UnicornOne.Abstractions.dll`.

These are **classic .NET Framework class libraries**, not SDK-style .NET 6/8 projects. `dotnet build` is not the documented build path; the scripts locate **VS2022 MSBuild.exe**.

## Repository layout

```text
UnicornOne.TwinAddons/
├── .gitignore
├── .vscode/
│   ├── extensions.json
│   └── tasks.json
├── GaNCleanliness/
│   ├── GaNCleanliness.cs
│   └── GaNCleanliness.csproj
├── GaNLinearBilayerDesorption/
│   ├── GaNLinearBilayerDesorption.cs
│   └── GaNLinearBilayerDesorption.csproj
├── References/
│   └── README.txt             # DLL copied here at build time (not committed)
├── Build-TwinAddons.ps1
├── Deploy-TwinAddons.ps1
├── TwinAddons.code-workspace
└── TwinAddons.sln
```

The addons depend only on **`UnicornOne.Abstractions`**; neither project references `UnicornOne.Platform` nor `UnicornOne.Core`. This separation is intentional.

## Build

1. Build the UnicornOne host in VS2022, so its `UnicornOne.Abstractions.dll` reflects the installed Twin addon contracts.
2. Open `TwinAddons.code-workspace` in VS Code.
3. Press **Ctrl+Shift+B** to run **Build Twin addons (Debug)**, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-TwinAddons.ps1 -Configuration Debug
```

The build script selects a compiled `UnicornOne.Abstractions.dll`, copies it to the local `References` directory, and invokes VS2022 MSBuild for `TwinAddons.sln`. Outputs are written to each project's `bin\Debug` or `bin\Release` directory.

The default host source path is `C:\Users\Bizmuth\source\repos\UnicornOne`. Override it when needed:

```powershell
.\Build-TwinAddons.ps1 -RepoRoot 'D:\Source\UnicornOne' -Configuration Release
```

Or point at the exact built contract assembly:

```powershell
.\Build-TwinAddons.ps1 -AbstractionsDll 'C:\UnicornOne\UnicornOne.Abstractions.dll'
```

You can also set the environment variable `UNICORNONE_REPO` to select your source checkout.

## Deploy

**Close UnicornOne before replacing loaded DLLs**, then run the deployment check and deployment:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Deploy-TwinAddons.ps1 -Configuration Debug -CheckOnly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Deploy-TwinAddons.ps1 -Configuration Debug
```

The default destination is:

```text
C:\UnicornOne\Plugins\Twin\
```

The script copies **only the two addon DLLs**, backs up existing copies as timestamped `.bak` files, and verifies the copied files with SHA-256. To deploy elsewhere:

```powershell
.\Deploy-TwinAddons.ps1 -Configuration Release -TargetDir 'D:\UnicornOne\Plugins\Twin'
```

**Do not deploy `References\UnicornOne.Abstractions.dll` alongside the addons.** The host supplies that assembly; including a separate copy in the plugin folder risks assembly identity/version mismatches. The `.csproj` references deliberately set `<Private>False</Private>`.

## Use in the Twin Cockpit

1. Restart UnicornOne **in Simulation Mode**, with a **GaN surface** active for behavioural tests.
2. Open **Twin → Addons** from the floating Twin Cockpit.
3. Confirm both DLLs appear with status **Loaded**. *Loaded* means discovered, **not enabled**.
4. Enable either addon using its checkbox. New addons start disabled unless their saved configuration says otherwise.
5. Select an addon to inspect and edit its registered parameters. Save changes with **Save JSON** if they should survive a restart.
6. To compare native and modified behaviour, disable both addons and repeat the same Ga dosing/desorption sequence.

Configuration is owned by the **host**, not this repository. The system-specific file is:

```text
C:\UnicornOne\Settings\<SystemUUID>\Twin\twin.json
```

The Twin API also exposes addon discovery and enable/disable to REST, IronPython recipes and external workbooks. They all share the host's simulation-mode guard. For example, in an enabled IronPython recipe:

```python
for addon in twin.Addons():
    recipe.WriteMessage(addon.Id)

twin.EnableAddon("GaNCleanliness", True)
twin.EnableAddon("GaNLinearBilayerDesorption", True)
twin.Save()
```

## Suggested validation

First test the **native baseline** with both addons disabled. Then enable one at a time:

1. **Cleanliness only:** verify 0.1 at initialisation, then 0.125 → 0.150 → 0.175 → 0.200 after the four transitions; successive complete cycles should reduce the droplet-removal delay.
2. **Linear bilayer only:** keep Ga dose and other conditions consistent while varying real simulated substrate temperature. Measure the central RHEED intensity recovery gradient in intensity units/second.
3. **Both enabled:** verify the slowed droplet stage and temperature-controlled bilayer recovery coexist; observe both RHEED and laser reflectance.
4. **Persistence:** save enabled states and parameter overrides, restart, and confirm they reload.
5. **Safety:** in real (non-simulation) mode, parameter modification and addon enable/disable should be rejected gracefully by the host.

Do not compare traces from runs with different dosing or time origins as though they were controlled experiments.

## Extending the project

An addon implements **`ITwinAddon`** from `UnicornOne.Abstractions.Twin`, publishes its parameter descriptors, and implements the relevant optional hook interfaces. These examples demonstrate:

- `ITwinGaNTransitionObserver` — observe the four one-shot surface transitions.
- `ITwinGaNDropletRateModifier` — modify the proposed droplet desorption rate.
- `ITwinGaNBilayerKineticsOverride` — replace native bilayer coverage kinetics.
- `ITwinAddonLiveState` — expose read-only runtime state such as Cleanliness.

The host discovers plugin assemblies at startup. When changing the addon contract, rebuild **UnicornOne first**, then rebuild/redeploy these addons against the updated Abstractions DLL. Avoid depending on private Platform/Core classes: extensibility should flow through the public interfaces.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Abstractions reference not found | Build UnicornOne first or pass `-AbstractionsDll` / `-RepoRoot`. |
| MSBuild not found | Install VS2022 MSBuild and the .NET Framework 4.7.2 targeting pack. |
| Addon missing from Cockpit | Verify `Plugins\Twin`, restart UnicornOne, and inspect host loader errors. |
| Addon shown as Loaded but no change | Check its **Enabled** checkbox, Simulation Mode, and whether the active surface is **GaN**. |
| Assembly/type load error | Rebuild addons against the **same** Abstractions version as the running host; avoid duplicate Abstractions DLLs in the plugin directory. |
| Unable to overwrite a DLL | Close UnicornOne before deployment, then rerun the script. |
| Configuration changes rejected | Check `DeveloperServices.Simulation_SimulationMode` and the host's error/status message. |

## Version control

The supplied `.gitignore` excludes generated `bin/`, `obj/`, local copied assemblies under `References/`, VS user settings, caches, logs and backup files. **Commit the source, `.sln`, `.csproj`, VS Code tasks, scripts and documentation; not the compiled DLLs.**

If a binary was already committed, adding `.gitignore` does not untrack it. Remove it from Git's index with `git rm --cached <path>` while keeping the local file.
