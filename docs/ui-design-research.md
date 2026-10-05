# Making the Ceará UI feel deliberately designed

Research date: 5 October 2026. Scope: web research and application inspection. Updated after the Angular migration to reflect the retained interface and available tasks. “AI slop” is treated here as a design complaint about generic choices, weak hierarchy, and empty language, rather than a measurable property of a font or framework. These are design guidelines for future revisions; the migration preserves the existing UI.

## What the sources support

NN/g recommends consistent alignment, a deliberate typographic hierarchy, a restrained palette, and imagery that adds information. Its three-size typography guideline is a rule of thumb, not a requirement to squeeze a dense application into three literal font sizes. [Good Visual Design, Explained, 14 November 2025](https://www.nngroup.com/articles/good-visual-design/)

Linear’s March 2026 refresh preserved information density while reducing the prominence of navigation, unnecessary icons, colored icon backgrounds, and separators. The team also restored predictable action placement and used direct comparisons with the previous interface to iterate. This is a first-party design case study, not evidence that copying Linear improves every product. [A calmer interface for a product in motion](https://linear.app/now/behind-the-latest-design-refresh)

NN/g’s 2026 article identifies handmade visual expression and human-sounding writing as responses to AI fatigue, but stresses authentic fit and warns against communicating qualities a product cannot deliver. It does not establish that an analytical dashboard needs textures or handwritten typography. [Handmade Designs: The New Trust Signal, 10 April 2026](https://www.nngroup.com/articles/handmade-designs/)

## Recommendations for this application

The observations below refer to `app/src/app/app.component.ts`, `app/src/app/app.component.html`, `app/src/styles.css`, and the Angular company and assistant components in `app/src/componentes/`. They are our application of the sources, not claims those authors made about this project.

1. **Use labels that describe available tasks.** Preserve direct titles such as “Empresas” and copy such as “Filtre por município e atividade e consulte os dados de cada empresa.” The map count is labeled “Empresas com contato,” with a note defining the population. Keep natural Portuguese and make source, dates, and ranking criteria understandable.

2. **Give the working surface enough viewport space.** The current desktop header has a 64px minimum height, page titles use 26px text, and the heading has 20px top and 18px bottom padding. Map height adapts to the viewport through `clamp(430px, calc(100dvh - 290px), 720px)`. Preserve the compact header and inline statistics, then verify the map, search, ranking, and company table visually on small laptops.

3. **Preserve readable density.** The body and navigation use 14px text, with 12–13px text for secondary information and data dates. Check actual content wrapping and row counts before reducing these sizes. Preserve tabular numerals and right alignment for numeric comparisons. Compact padding and remove redundant copy before making meaningful information smaller.

4. **Build identity from the actual domain.** The neutral surfaces, blue accent, Geist type, and modest radii already form a coherent starting point. Ceará’s geography, municipality names, activity categories, precise data dates, and understandable ranking criteria offer stronger identity than adding decorative illustration. Make the geographic map the recognizable visual element. Keep source and snapshot information accessible and legible.

5. **Prune decoration selectively.** Audit the repeated eyebrow labels, icon containers, map stamps, borders, and explanatory panels. Keep each treatment when it helps orientation, interpretation, or action. Standardize search, sort, filters, and company-detail access across related views. Avoid universal bans on rounded corners, cards, blue, or a particular font: those would substitute another template for judgment.

## How to judge a future revision

Compare current and revised screens at 1366×768 and a narrow mobile width, in both themes. Ask whether users can find a municipality, explain the map metric, filter companies, open company details, and continue an assistant conversation without hunting. Check empty, loading, error, selected, and long-name states, including offline map fallback and keyboard navigation. Prefer changes that improve these tasks and make the actual data easier to read. See [migration verification](migration-verification.md) for implemented behavior and test results.
