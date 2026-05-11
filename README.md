# AccessoryTab

`AccessoryTab` is a Vintage Story mod that adds a dedicated accessory tab with synced extra equipment slots for cosmetic and wearable items.

## Features

- Extra accessory-style equipment slots
- Multiplayer syncing for equipped accessories
- Work-in-progress compatibility for more specialized accessory slot types

## Requirements

- `.NET 8`
- A local Vintage Story installation
- The `VINTAGE_STORY` environment variable pointing to the game install directory so the referenced assemblies can be resolved during build

## Build

Open `AccessoryTab.slnx` in Visual Studio or run:

`dotnet build AccessoryTab.slnx`

The mod project outputs to `AccessoryTab/bin/<Configuration>/Mods/mod`.

## Project Layout

- `AccessoryTab/` - main mod source
- `CakeBuild/` - build helper project
- `Releases/` - packaged outputs

## Mod Metadata

- Mod ID: `accessorytab`
- Name: `AccessoryTab`
- Side: `universal`

## Status

Current progress notes live in `STATUS.md`.
