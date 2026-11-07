# Third-party notices

- [PKHeX.Core](https://github.com/kwsch/PKHeX), version 26.10.18:
  Copyright © Kaphotics and contributors. GPL-3.0-or-later.
  Used for Generation 4/5/6/7 save parsing and forward conversion and Japanese game text. Restored from NuGet;
  no PKForge source or artwork is copied into this project.
- Microsoft .NET MAUI: MIT license.
- xUnit.net (tests): Apache-2.0 license.

Pokémon names and game titles belong to Nintendo, Creatures Inc. and GAME FREAK inc.
PokemonMove is an unofficial project and is not affiliated with these companies.

PokemonMove is distributed under GPL-3.0-or-later; see LICENSE.
PKHeX.Core 26.10.18 corresponding source is provided alongside development
releases as PKHeX-26.10.18-source.tar.gz, from the upstream 26.10.18 tag:
https://github.com/kwsch/PKHeX/tree/26.10.18
PokemonMove source and build instructions are available at the release tag
in this repository and in the attached PokemonMove source archive.

Microsoft .NET MAUI source (10.0.20): https://github.com/dotnet/maui/tree/10.0.20
.NET runtime source: https://github.com/dotnet/runtime
AndroidX libraries: https://android.googlesource.com/platform/frameworks/support
Kotlin: https://github.com/JetBrains/kotlin
These dependencies retain their respective licenses and copyright notices.

- Pokémon front sprites: hosted by [PokeAPI/sprites](https://github.com/PokeAPI/sprites)
  at commit 1dd55c0436d2a5b6e324c0b780e1b1b416fbfb64. Unmodified normal/shiny
  images for species 1–807 are bundled for offline use. Pokémon artwork remains
  © Nintendo, Creatures Inc., GAME FREAK inc. Form/gender artwork is representative.
  See src/PokemonMove.App/Resources/PokemonSprites/NOTICE.md and SOURCE.json.

- `Gen7MemeKey.cs` and `Gen7SaveSignature.cs` contain adapted PKHeX.Core 26.10.18
  MemeCrypto/MemeKey save-file signing code, GPL-3.0-or-later, copyright Kaphotics
  and contributors. The adapted code limits the API to the save-file key,
  explicitly initializes AES work buffers, and right-aligns/zero-pads RSA integer
  results to their required 96-byte width. A known-answer test vector is adapted
  from upstream `Tests/PKHeX.Core.Tests/Saves/MemeCrypto/MemeCryptoTests.cs`.
  Corresponding adapted source is included in the PokemonMove source archive;
  unmodified upstream source is attached separately as described above.

## DotGothic16

DotGothic16 Regular (unmodified), Copyright 2020 The DotGothic16 Project Authors.
Source: https://github.com/google/fonts/tree/main/ofl/dotgothic16
License: SIL Open Font License 1.1. Full text: src/PokemonMove.App/Resources/Fonts/OFL.txt (also bundled as assets/licenses/DotGothic16-OFL.txt).
