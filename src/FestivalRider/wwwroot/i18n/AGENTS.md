# i18n catalogs — Agent rules

## Source of truth

- `en.json` is the source-of-truth catalog. Every other catalog MUST contain the same key set with the same `{N}` positional-placeholder count per key.
- NEVER remove a key from `en.json` once released. NEVER change a key's `{N}` placeholder count. Adding new keys is always safe.
- On rename: ship both old and new keys for one release cycle, then remove the old key in a successor plan.

## Naming conventions

- `nav.{item}` — primary nav items (`nav.bands`, `nav.runningOrder`, `nav.settings`).
- `page.{name}.title` — `<PageTitle>` content.
- `page.{name}.{section}.{label}` — page-scoped headings, buttons, helper text.
- `section.{name}.title` — `<RiderSection>` titles.
- `field.{name}` — shared field labels reused across multiple pages.
- `toast.{producer}.{kind}` — toast messages (`toast.storage.restored`, `toast.settings.showSaved`).
- `confirm.{action}.{field}` — `<ConfirmDialog>` content.
- `enum.{TypeName}.{ValueName}` — enum values (`enum.ContactRole.TourManager`). MUST cover every public enum value under `FestivalRider.Models`.
- `print.{strategyKey}.{label}` — print-strategy labels (`print.band.contactsHeading`).
- `bundle.error.{kind}` — `BundleService` error strings surfaced to the user.
- `bundle.warning.{kind}` — `BundleService` warning strings surfaced to the user.
- `banner.{name}` / `update.{label}` — banners and update toast.

## File naming

- Filename MUST match the lower-cased BCP-47 tag exactly: `en.json`, `fr-fr.json`, `nl-be.json`. NEVER mixed case. NEVER aliases.
- Adding a locale requires: (1) a new `{tag}.json` with every key from `en.json`; (2) an entry in `locales.json`.

## Parity test gate

- `LocalizationCatalogTests` asserts: (a) every catalog is valid JSON `Dictionary<string,string>`; (b) every non-English catalog's key set equals `en.json`'s; (c) for every shared key the `{N}` placeholder count matches; (d) every public enum value under `FestivalRider.Models` has an `enum.{TypeName}.{ValueName}` key in `en.json`; (e) every locale listed in `locales.json` has a corresponding `{tag}.json` and vice-versa.
- A failing parity test MUST block merge.

## Type-safe keys

- A nested static class `LocalizationKeys` mirrors `en.json` dot segments for IDE autocomplete. Adding a key to `en.json` REQUIRES adding the corresponding constant to `LocalizationKeys`. `LocalizationCatalogTests` enforces 1:1 parity between `en.json` keys and `LocalizationKeys` constant values.

## Wire-format invariance

- ALL persisted formats (CSV, bundle JSON/CSV, `AppState` JSON, filenames) MUST use `CultureInfo.InvariantCulture`. NEVER let locale bleed into persisted bytes.
- Print HTML is locale-shaped (dates use `Localization.Culture`). Bundle / CSV pipeline is unchanged.
