# Licensing & legal policy

## Hard rules

- **No** official Formula 1 branding, team liveries, driver likenesses, sponsor
  logos, real circuit likenesses, or copyrighted audio.
- **No** assets ripped or extracted from EA/Codemasters F1, Assetto Corsa,
  iRacing, Forza, Gran Turismo, or any commercial title.
- A public GitHub repo does **not** make its binary art reusable — licence must
  be explicit and permit commercial redistribution.
- Fictional teams, fictional branding, generic Formula-racing designs only.
- Never purchase assets/subscriptions/credits; never use a paid API without a
  pre-existing authorised key.
- Never print, commit, or log API keys/tokens/passwords.

## Asset classes in this repo

| Class | Licence | Attribution | Commercial |
|---|---|---|---|
| Generated Blender modules | CC0-1.0 (project-owned original) | no | yes |
| Generated PBR textures | CC0-1.0 (project-owned original) | no | yes |
| Poly Haven (when fetched) | CC0-1.0 | no | yes |
| ambientCG (when fetched) | CC0-1.0 | no | yes |

Everything currently committed is project-generated CC0-equivalent — zero
third-party licence exposure. `Assets/ThirdParty/asset_manifest.json` records
provenance + SHA-256 for every asset; anything whose source/creator/licence is
unknown is rejected.

## Meshy (optional)

Only with a pre-existing `MESHY_API_KEY` (absent here → unused). If enabled: keep
the key in an ignored `.env`, never in `.mcp.json`/code/logs; use only for
generic trackside props (never real cars, driver likenesses, team designs,
sponsor branding, whole tracks, or safety-critical collision geometry); generate
low-cost previews before spending credits.
