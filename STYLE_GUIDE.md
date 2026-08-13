# OpenShock Frontend Style Guide

How the OpenShock web UI is put together: markup structure, layout, color, spacing, and how code is split into components.

This document is written for someone who does **not** have `@openshock/svelte-core`, `bits-ui`, `formsnap`, `vaul`, or any other OpenShock package.
It assumes only:

- **Tailwind CSS v4** (the `@theme` / `@custom-variant` / `@utility` syntax, not v3 config files)
- **shadcn** primitives (`Button`, `Card`, `Table`, `Dialog`, `Sidebar`, `Field`, `Empty`, `Popover`, `DropdownMenu`, `Skeleton`, `Spinner`, `Sonner`, ...)
- **Lucide** icons

Everything else is described as raw HTML/CSS or as Tailwind utility strings you can paste anywhere.
Where the codebase uses an in-house component, this guide gives you its full anatomy so you can rebuild it in one file.

---

## 1. Foundations

### 1.1 The token system

There is exactly one source of truth for color: a set of CSS custom properties defined on `:root` and overridden on `.dark`.
Nothing in the app hardcodes a neutral color.
The palette is the stock shadcn **neutral** theme in OKLCH, with `--radius: 0.625rem`.

Paste this whole block after `@import 'tailwindcss';`:

```css
@custom-variant dark (&:is(.dark *));

:root {
  --radius: 0.625rem;

  --background: oklch(1 0 0);
  --foreground: oklch(0.145 0 0);
  --card: oklch(1 0 0);
  --card-foreground: oklch(0.145 0 0);
  --popover: oklch(1 0 0);
  --popover-foreground: oklch(0.145 0 0);
  --primary: oklch(0.205 0 0);
  --primary-foreground: oklch(0.985 0 0);
  --secondary: oklch(0.97 0 0);
  --secondary-foreground: oklch(0.205 0 0);
  --muted: oklch(0.97 0 0);
  --muted-foreground: oklch(0.556 0 0);
  --accent: oklch(0.97 0 0);
  --accent-foreground: oklch(0.205 0 0);
  --destructive: oklch(0.577 0.245 27.325);
  --border: oklch(0.922 0 0);
  --input: oklch(0.922 0 0);
  --ring: oklch(0.708 0 0);

  --chart-1: oklch(0.646 0.222 41.116);
  --chart-2: oklch(0.6 0.118 184.704);
  --chart-3: oklch(0.398 0.07 227.392);
  --chart-4: oklch(0.828 0.189 84.429);
  --chart-5: oklch(0.769 0.188 70.08);

  --sidebar: oklch(0.985 0 0);
  --sidebar-foreground: oklch(0.145 0 0);
  --sidebar-primary: oklch(0.205 0 0);
  --sidebar-primary-foreground: oklch(0.985 0 0);
  --sidebar-accent: oklch(0.97 0 0);
  --sidebar-accent-foreground: oklch(0.205 0 0);
  --sidebar-border: oklch(0.922 0 0);
  --sidebar-ring: oklch(0.708 0 0);
}

.dark {
  --background: oklch(0.145 0 0);
  --foreground: oklch(0.985 0 0);
  --card: oklch(0.205 0 0);
  --card-foreground: oklch(0.985 0 0);
  --popover: oklch(0.205 0 0);
  --popover-foreground: oklch(0.985 0 0);
  --primary: oklch(0.922 0 0);
  --primary-foreground: oklch(0.205 0 0);
  --secondary: oklch(0.269 0 0);
  --secondary-foreground: oklch(0.985 0 0);
  --muted: oklch(0.269 0 0);
  --muted-foreground: oklch(0.708 0 0);
  --accent: oklch(0.269 0 0);
  --accent-foreground: oklch(0.985 0 0);
  --destructive: oklch(0.704 0.191 22.216);
  --border: oklch(1 0 0 / 10%);
  --input: oklch(1 0 0 / 15%);
  --ring: oklch(0.556 0 0);

  --chart-1: oklch(0.488 0.243 264.376);
  --chart-2: oklch(0.696 0.17 162.48);
  --chart-3: oklch(0.769 0.188 70.08);
  --chart-4: oklch(0.627 0.265 303.9);
  --chart-5: oklch(0.645 0.246 16.439);

  --sidebar: oklch(0.205 0 0);
  --sidebar-foreground: oklch(0.985 0 0);
  --sidebar-primary: oklch(0.488 0.243 264.376);
  --sidebar-primary-foreground: oklch(0.985 0 0);
  --sidebar-accent: oklch(0.269 0 0);
  --sidebar-accent-foreground: oklch(0.985 0 0);
  --sidebar-border: oklch(1 0 0 / 10%);
  --sidebar-ring: oklch(0.556 0 0);
}
```

Then expose them to Tailwind, plus the radius ramp:

```css
@theme inline {
  --radius-sm: calc(var(--radius) - 4px);   /* 0.25rem */
  --radius-md: calc(var(--radius) - 2px);   /* 0.375rem */
  --radius-lg: var(--radius);               /* 0.625rem */
  --radius-xl: calc(var(--radius) + 4px);   /* 0.875rem */

  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  /* ...one --color-* line per token above, including sidebar-* and chart-* */
}
```

**Reading the values.** Every neutral has chroma `0`, so the entire UI chrome is achromatic.
Light mode runs `L 1.0` (background) down to `L 0.145` (text); dark mode is that ramp inverted, with cards *lighter* than the page (`0.205` on `0.145`) rather than darker.
Only three tokens carry chroma: `--destructive` (red, hue 27 light / 22 dark), the five chart colors, and `--sidebar-primary` in dark mode (indigo, `0.488 0.243 264`).

**Borders in dark mode are alpha, not a flat gray.** `--border: oklch(1 0 0 / 10%)` and `--input: oklch(1 0 0 / 15%)`.
This matters: a border drawn on a card (`L 0.205`) and the same border on the page (`L 0.145`) render at different apparent lightness, which is what keeps the layering readable.

### 1.2 Base layer

```css
@layer base {
  * {
    @apply border-border outline-ring/50;
  }
  body {
    @apply bg-background text-foreground;
  }
  button:not([disabled]),
  [role='button']:not([disabled]) {
    cursor: pointer;
  }
}
```

The universal `border-border` is load-bearing: components write `border` / `border-b` / `divide-y` with **no color class**, and get the theme border for free.
If you drop this rule, borders fall back to `currentColor` and the whole UI goes wrong.

The `cursor: pointer` rule exists because Tailwind v4 stopped shipping it on buttons.

### 1.3 Dark mode mechanism

Class-based, not media-query based: `.dark` is toggled on `<html>` and the custom variant is `dark (&:is(.dark *))`.
Consequences you must respect:

- Write `dark:` variants only when a literal color is unavoidable, never for tokens.
- The theme is persisted and applied before paint, so there is no light flash.
- Some surfaces are **dark-only by design** and do not participate in the toggle: the landing hero (`bg-zinc-950 text-white`) and the guided-tour popovers.

### 1.4 Radius, shadow, spacing

| Scale | Value | Where |
|---|---|---|
| `rounded-sm` | 0.25rem | rare |
| `rounded-md` | 0.375rem | **default for surfaces**: cards, list containers, inline blocks |
| `rounded-lg` | 0.625rem | dialogs, larger panels |
| `rounded-xl` / `rounded-2xl` | 0.875rem / 1rem | empty-state icon medallion |
| `rounded-full` | pill | status dots, avatars |

Shadows are used sparingly.
The UI separates surfaces with **borders and background lightness**, not elevation.
The only notable shadows are `shadow-lg` on floating popups and the fake-key shadow on `<kbd>`.

Spacing is the default Tailwind 0.25rem scale.
In practice the layout uses a very narrow vocabulary:

- `gap-1` / `gap-2` inside a control cluster (buttons next to each other, icon + label)
- `gap-3` between a row's sub-elements
- `gap-4` between siblings in a page body, and between grid cells
- `gap-6` between major page sections
- `p-2` / `px-3 py-2` inside compact card chrome, `px-4 py-3` for list rows, `p-4` for large clickable tiles

**Use `gap` on a flex/grid parent, not margins on children.**
Margins appear only for a few deliberate one-offs (`mb-4` under a page header, `ml-auto` to push a cluster right).

### 1.5 Typography

There is no custom font stack; the Tailwind default sans is used, with `font-mono` for code, keys, tokens, and firmware versions.

| Role | Classes |
|---|---|
| Page title | `text-3xl font-bold` (`text-2xl font-bold` on denser pages) |
| Section heading | `text-lg font-semibold` |
| Card title | `text-sm font-medium` (stat cards) or default card title size |
| Big stat number | `text-2xl font-bold` |
| Body | inherited, no class |
| Subtitle / helper | `text-muted-foreground` with `mt-1` |
| Meta / timestamps | `text-muted-foreground text-sm` |
| Fine print | `text-muted-foreground text-xs` |
| Error text | `text-destructive text-sm` |

Rule of thumb: **weight and color carry hierarchy, size is a distant third.**
Most of the app lives at exactly two sizes (default and `text-sm`) with `font-medium`/`font-semibold` and `text-muted-foreground` doing the work.

### 1.6 When literal colors are allowed

Semantic tokens cover chrome. Literal Tailwind colors are permitted only for **meaning**, and the set is small and consistent:

| Meaning | Class |
|---|---|
| Online / success | `text-green-500`, dot `bg-green-500`, dot on a card `bg-green-400`, icon `text-green-800` |
| Offline / error | `text-red-500`, dot `bg-red-500`, icon `text-red-800` |
| Muted icon glyphs in header/footer | `text-gray-600 dark:text-gray-300` |
| Destructive action | the `destructive` token, never a raw red |
| Dev-environment banner | `bg-[orangered] text-white` |

Brand and third-party literals, for reference:

- Brand accent (dot-grid spotlight): `rgba(225, 74, 109, 0.9)` / `#e14a6d`
- Tour popover surface: `#13131a`, on `rgba(255,255,255,0.1)` borders
- Heart in the footer: `#e25555`
- Vendor logos keep their own brand hex (`#5865F2` Discord, Cloudflare orange, and so on) and are the one place literal color is expected

**Never** invent a new gray. If you reach for `bg-gray-100`, the answer is `bg-muted`; for `bg-white`, it is `bg-card` or `bg-background`.

---

## 2. Layout

### 2.1 The app shell

One shell wraps every route. It is a full-viewport, non-scrolling frame with exactly one scroll container inside it.

```
┌──────────┬────────────────────────────────────────┐
│          │ [dev banner]              (flex-none)  │
│ Sidebar  ├────────────────────────────────────────┤
│ 16rem    │ Header      h-12 border-b  (shrink-0)  │
│ / 3rem   ├────────────────────────────────────────┤
│ collapsed│ <main>      flex-1 min-h-0             │
│          │             overflow-x-hidden          │
│          │             ← the only scroller        │
│          ├────────────────────────────────────────┤
│          │ Footer      text-sm      (flex-none)   │
└──────────┴────────────────────────────────────────┘
```

```html
<div class="flex h-screen w-screen flex-1 flex-col overflow-hidden">
  <!-- optional dev banner -->
  <div class="top-0 left-0 z-1 flex-none bg-[orangered] text-center text-white">…</div>

  <header class="flex h-12 shrink-0 items-center gap-2 border-b">…</header>

  <main class="min-h-0 flex-1 overflow-x-hidden">
    <!-- page content -->
  </main>

  <footer class="text-muted-foreground bottom-0 flex flex-none items-center justify-between px-2 text-sm">…</footer>
</div>
```

The three rules that make this work:

1. The shell is `h-screen overflow-hidden`, so the document never scrolls.
2. `main` is `flex-1 min-h-0`. Without `min-h-0` a flex child refuses to shrink below its content and the page grows instead of scrolling.
3. Header and footer are `shrink-0` / `flex-none`.

Sidebar geometry (shadcn sidebar defaults, stated here so you can match it):

- expanded `--sidebar-width: 16rem`
- collapsed to icons `--sidebar-width-icon: 3rem`
- mobile drawer `18rem`
- width transitions `transition-[width] duration-300 ease-in-out`
- collapse mode is `icon`, not `offcanvas`, on desktop
- open/closed state is persisted in a `sidebar:state` cookie and read server-side, so SSR renders the correct width with no hydration flash

### 2.2 Header anatomy

Left to right: sidebar toggle, vertical separator, breadcrumbs, elastic spacer, theme switch, then either a user menu or auth buttons.

```html
<header class="flex h-12 shrink-0 items-center gap-2 border-b">
  <div class="flex w-full items-center gap-2 px-3">
    <button class="size-8" title="Toggle Sidebar">…</button>
    <hr class="mr-2 h-4 w-px" />          <!-- vertical separator -->
    <nav aria-label="Breadcrumb" class="shrink-0">…</nav>

    <div class="flex flex-1 flex-row items-center justify-between space-x-2 py-2 pr-2">
      <div class="flex-1"></div>          <!-- elastic spacer -->
      …theme switch, avatar + name, or Login / Sign Up…
    </div>
  </div>
</header>
```

Notes worth copying:

- The user's display name is `hidden lg:inline-block`; the avatar (`h-8 rounded-full`) always shows.
- The GitHub/Discord icon links are `hidden sm:flex`.
- Auth buttons are `variant="outline"` with the icon **after** the label (`Login <LogIn />`).

### 2.3 Sidebar structure

Two groups pinned apart by a spacer, so navigation sits at the top and account/admin sit at the bottom:

```html
<aside data-collapsible="icon">
  <header>
    <a href="/home" aria-label="OpenShock home">
      <span class="pointer-events-none flex">
        <LogoMark class="ml-[0.667px] h-7.5 shrink-0" />
        <span class="ml-1.5 flex grow items-center">
          <LogoText class="h-auto max-h-7.5 transition-opacity delay-100 duration-200
                           group-data-[collapsible=icon]:opacity-0
                           group-data-[collapsible=icon]:delay-0" />
        </span>
      </span>
    </a>
  </header>

  <div class="content">
    <!-- primary nav group -->
    <div class="grow"></div>   <!-- pushes the rest down -->
    <!-- admin group (collapsible), settings group -->
  </div>
</aside>
```

Details:

- The wordmark fades out on collapse (`opacity-0`) while the mark stays. There is a `delay-100` on fade-in and `delay-0` on fade-out so the text does not fight the width animation.
- Every menu item is `icon + label`, and the label is what becomes the tooltip in icon mode.
- A group can be collapsible; its label doubles as the trigger, with a chevron that rotates via `group-data-[state=open]/collapsible:rotate-180`.
- Active state is computed by path prefix (`path === href || path.startsWith(href + '/')`), so `/settings/account` lights up `Settings`.

### 2.4 Page container

Every page's outermost element. This is the single most reused piece of layout in the codebase:

```html
<div class="container mx-auto flex h-full flex-col items-start justify-start
            gap-4 px-4 pt-4 pb-1 sm:px-12 sm:pt-12 sm:pb-2">
  …
</div>
```

- Centered, max-width-capped, full height of `main`.
- Vertical stack with a **default `gap-4`** between direct children, which is why pages rarely need margins.
- Padding roughly triples at `sm` (`px-4 → px-12`, `pt-4 → pt-12`). The bottom padding stays tiny because the footer supplies the visual base.
- Children are `items-start`, so anything that should be full width says `w-full` explicitly.

Variants seen in the app: `class="w-full"` on data-heavy pages, and `class="items-center-safe justify-center-safe p-0!"` for centered auth screens.

### 2.5 Page header

```html
<div class="mb-4 w-full">
  <div class="flex w-full flex-wrap items-center gap-x-2 gap-y-1">
    <h1 class="text-3xl font-bold">Hubs</h1>
    <!-- action cluster, only if actions exist -->
    <div class="ml-auto flex flex-wrap items-center justify-end gap-1">
      <button>+ Add Hub</button>
      <button>↻ Refresh</button>
    </div>
  </div>
  <p class="mt-1 text-muted-foreground">This is a list of all hubs you own</p>
</div>
```

`flex-wrap` plus `ml-auto` is the whole responsive story: on narrow screens the action cluster drops to its own line and stays right-aligned.
Primary actions carry a Lucide icon **before** the label.

### 2.6 Auth page layout

Auth routes replace the container with a centered column and put the logo above the card:

```html
<div class="container mx-auto flex h-full flex-col items-center-safe justify-center-safe gap-4 p-0!">
  <span class="flex items-center gap-2 self-center font-medium">
    <img class="h-8" src="/logo.svg" alt="OpenShock Logo" />
  </span>
  <div class="flex max-w-sm flex-col gap-6">
    <!-- card -->
  </div>
</div>
```

`max-w-sm` (24rem) is the auth card width. The `-safe` alignment keeps content reachable when the card is taller than the viewport.

### 2.7 The landing hero

The only marketing surface, and the only place with decorative motion:

```html
<section class="relative flex h-full flex-col items-center justify-center space-y-6
                overflow-hidden bg-zinc-950 text-center text-white">
  <!-- dot grid, absolutely positioned, pointer-events-none -->
  <img class="h-10 sm:h-16 md:h-22" src="/logo.svg" alt="OpenShock Logo" />
  <p class="relative text-lg opacity-75 md:text-2xl">…</p>
  <div class="relative flex space-x-4 pt-8 text-sm opacity-75">…</div>
</section>
```

Content sits above the background because each block is `relative`.
Hierarchy here is built with **opacity**, not color: everything below the logo is `opacity-75`.

The dot grid is two stacked layers, a static one and a brand-colored one revealed by a mask that follows the cursor:

```css
.bg-grid {
  background-image: radial-gradient(circle, rgba(255, 255, 255, 0.07) 1px, transparent 1px);
  background-size: 28px 28px;
  background-position: center center;
}
.bg-grid-spotlight {
  background-image: radial-gradient(circle, rgba(225, 74, 109, 0.9) 1px, transparent 1px);
  background-size: 28px 28px;
  background-position: center center;
  mask-image: radial-gradient(circle 220px at var(--mouse-x, -9999px) var(--mouse-y, -9999px),
                              black 0%, transparent 75%);
  -webkit-mask-image: radial-gradient(circle 220px at var(--mouse-x, -9999px) var(--mouse-y, -9999px),
                                      black 0%, transparent 75%);
}
```

Both layers live in `<div class="pointer-events-none absolute inset-0">`, `--mouse-x/--mouse-y` are written as inline styles, and updates are throttled through a single `requestAnimationFrame`.
The mouse position is measured relative to the container's `getBoundingClientRect()`, not the viewport.

---

## 3. Content patterns

These are the four ways a page body is ever laid out. Pick one; do not invent a fifth.

### 3.1 Stat cards

```html
<div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
  <Card>
    <CardHeader class="flex flex-row items-center justify-between pb-2">
      <CardTitle class="text-sm font-medium">Shockers</CardTitle>
      <Zap class="text-muted-foreground size-4" />
    </CardHeader>
    <CardContent>
      <div class="text-2xl font-bold">12</div>
      <p class="text-muted-foreground text-xs">3 online</p>
    </CardContent>
  </Card>
</div>
```

Fixed column count, because the number of stats is known.
Header is title-left / glyph-right with `pb-2` to tighten it against the number.
A status stat swaps the icon for a dot: `<div class="size-2 rounded-full bg-green-500">`.

### 3.2 Auto-fill card grid

For an unknown number of equally weighted cards:

```html
<div class="grid w-full grid-cols-[repeat(auto-fill,minmax(20rem,1fr))] gap-4">…</div>
```

`20rem` is the canonical card width across the app, and it is not a coincidence: the device card is itself `w-80` (= 20rem).
No breakpoint classes are needed; the grid reflows by itself.

When cards are grouped (for example by parent device), each group is:

```html
<div class="flex w-full flex-col gap-3">
  <div class="flex items-center gap-2">
    <span class="size-2.5 rounded-full bg-green-400" title="Online"></span>
    <span class="text-lg font-semibold">Hub name</span>
    <span class="text-muted-foreground text-xs">4 shockers</span>
  </div>
  <div class="grid w-full grid-cols-[repeat(auto-fill,minmax(20rem,1fr))] gap-4">…</div>
</div>
```

with `gap-6` between groups.

### 3.3 Divided list rows

The pattern for settings-style lists (tokens, sessions, connections):

```html
<div class="divide-y rounded-md border">
  <div class="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3">
    <div class="flex min-w-0 flex-1 items-center gap-2">
      <span class="truncate font-medium">Token name</span>
    </div>
    <div class="text-muted-foreground flex shrink-0 flex-wrap items-center gap-x-4 text-sm">
      <span>Created 04/12/25</span>
      <span>Last used 2 hours ago</span>
      <span>Never expires</span>
    </div>
    <!-- action menu -->
  </div>
</div>
```

Anatomy to preserve:

- Container owns the border and the dividers; rows own only padding.
- The identity column is `min-w-0 flex-1` and its text is `truncate`. Both are required, `truncate` alone does nothing inside a flex child that will not shrink.
- The metadata cluster is `shrink-0` and `text-sm text-muted-foreground`, with `gap-x-4` between facts.
- Row-level `flex-wrap` + `gap-y-1` gives free mobile wrapping.

### 3.4 Tables, and their mobile fallback

Desktop uses a plain shadcn table. The action column is `class="w-0"` so it collapses to its content.
Empty state is a single full-width cell: `<td colspan="5" class="h-24 text-center">No hubs found.</td>`.

Below the mobile breakpoint the table is **not** made scrollable; it is replaced with a list:

```html
<div class="grid w-full gap-6">
  <div class="flex items-center justify-between gap-4">
    <div class="flex items-center gap-4">
      <Router class="size-8" />
      <div class="flex flex-col">
        <strong>Hub name</strong>
        <span class="text-red-500">Offline</span>
      </div>
    </div>
    <!-- same action menu as the table row -->
  </div>
</div>
```

The breakpoint is a JS media query at **768px** (`md`), evaluated once and shared, rather than duplicated CSS.
This is deliberate: the two renderings differ in structure, not just in width.

---

## 4. Component specs

These are the in-house components. Each is self-contained; rebuild from the markup below.

### 4.1 Device card

Fixed-width card with a bordered header strip, a slot for control actions, and a body that can be covered by an overlay.

```html
<div class="bg-card flex w-80 flex-col overflow-hidden rounded-md border">
  <!-- header -->
  <div class="border-border/60 flex items-center gap-2 border-b px-3 py-2">
    <div class="flex min-w-0 flex-1 flex-col">
      <h2 class="truncate text-sm leading-tight font-semibold" title="Name">Name</h2>
      <div class="text-muted-foreground flex items-center gap-1.5 text-[11px]">
        <span class="size-1.5 shrink-0 rounded-full bg-green-400" title="Online"></span>
        <span class="truncate" title="Hub">Hub</span>
      </div>
    </div>
    <div class="shrink-0"><!-- live toggle --></div>
    <div class="shrink-0"><!-- pause toggle --></div>
    <div class="shrink-0"><!-- overflow menu --></div>
  </div>

  <!-- body -->
  <div class="relative flex flex-col items-center gap-2 p-2">
    <!-- absolutely positioned pause overlay -->
    <!-- controls -->
  </div>
</div>
```

Points of interest: the header divider is `border-border/60`, a *softer* border than the card's own outline, so the internal split reads as secondary.
The sub-label uses an off-scale `text-[11px]`, the one intentional exception to the type scale.
`overflow-hidden` on the root is what keeps the header strip inside the rounded corners.

### 4.2 Empty state

Two sizes off one component. Default is a large centered block that fills the space; `compact` is a small bordered box.

```html
<!-- default -->
<div class="flex flex-col items-center justify-center gap-6 py-12 text-center sm:gap-8">
  <div class="flex flex-col items-center gap-3">
    <div class="bg-muted text-muted-foreground flex size-16 items-center justify-center rounded-2xl sm:size-20">
      <KeyRound class="size-8 sm:size-10" />
    </div>
    <h3 class="text-xl font-medium sm:text-2xl">No API tokens</h3>
    <p class="text-muted-foreground text-base sm:text-lg">Generate a token to authenticate with the OpenShock API.</p>
  </div>
  <div><!-- optional action button --></div>
</div>

<!-- compact: same structure, drop the size bumps, add: -->
<div class="flex-none rounded-md border …">…</div>
```

The medallion is the only place `rounded-2xl` appears.

### 4.3 Inline code and keys

```html
<code class="relative rounded bg-muted px-[0.3rem] py-[0.2rem] font-mono text-sm font-semibold">…</code>

<kbd class="inline-flex min-h-[30px] items-center justify-center rounded-md border border-gray-200
            bg-white px-1.5 py-1 font-mono text-sm text-gray-800
            shadow-[0px_2px_0px_0px_rgba(0,0,0,0.08)]
            dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-200
            dark:shadow-[0px_2px_0px_0px_rgba(255,255,255,0.1)]">Ctrl</kbd>
```

The key cap is the one component that uses literal grays and an explicit `dark:` pair, because it is imitating a physical object rather than a themed surface.
The 2px hard shadow (inverted to white in dark mode) is what sells it.

### 4.4 Text field

Every field is label + control + a **permanently reserved** message line, so validation never shifts layout:

```html
<div class="flex flex-col gap-2">
  <label for="x">Hub Name</label>

  <div class="relative flex grow flex-row items-center gap-2">
    <input id="x" class="grow" aria-invalid="true" aria-describedby="x-validation" />
    <!-- optional popup, e.g. password rules -->
    <div class="absolute top-full left-0 z-10 mt-1 w-full rounded-md border border-gray-200
                bg-white p-2 shadow-lg dark:border-gray-700 dark:bg-gray-800" role="tooltip">…</div>
  </div>

  <!-- message line, or an empty aria-hidden div of identical height -->
  <p id="x-validation" class="-mt-2! mb-2 h-4 truncate text-xs"
     role="status" aria-live="polite" aria-atomic="true">Must be at least 4 characters</p>
</div>
```

The `-mt-2! mb-2 h-4` on the message line, mirrored exactly by the empty placeholder `<div>`, is the trick: the slot always occupies 1rem.
Message color comes from the validation severity, and a message may carry a trailing link (`text-blue-500 underline`) for external references.
Fields with a trailing affordance (show/hide password, copy) wrap the input in an input-group with an inline-end addon instead of overlaying an absolute button.

### 4.5 Footer

```html
<footer class="text-muted-foreground bottom-0 flex flex-none items-center justify-between px-2 text-sm">
  <div>Made with <span style="color: #e25555;">&hearts;</span> by the <a href="…">OpenShock Team</a></div>
  <div class="flex items-center gap-2">
    <!-- connection status: Wifi (text-green-800) / WifiOff (text-red-800), h-4 w-4, opens a detail popover -->
  </div>
</footer>
```

The status glyph is a `size-icon` ghost button forced to `h-4 w-4`, with an `aria-label`, and its popover holds a two-column table (`[&_td]:px-4 [&_td]:last:text-right`) of connection state and backend version.

---

## 5. State presentation

Every async surface has four visual states, and they are always rendered the same way.

**Loading, page level:** centered spinner in the container.

```html
<Spinner class="size-20 text-gray-600 dark:text-gray-300" />   <!-- route gate -->
<div class="flex h-64 w-full items-center justify-center"><Spinner class="size-8 …" /></div>  <!-- section -->
<div class="flex items-center gap-3 p-12"><Spinner class="size-6" /><span class="text-muted-foreground">Loading shockers...</span></div>
```

**Loading, form level:** skeletons that mirror the real control heights, so nothing jumps.

```html
<Skeleton class="h-9 w-full" />   <!-- an input or button -->
<Skeleton class="h-1 w-full" />   <!-- a separator -->
<Skeleton class="h-16 w-full" />  <!-- a captcha widget -->
```

`h-9` is the standard control height. Match it.

**Empty:** the empty-state component (§4.2) for collections, a single centered table cell for tables.

**Error:** inline and quiet, never a modal.

```html
<div class="flex w-full flex-col items-center gap-3 py-12">
  <p class="text-destructive text-sm">Failed to load API tokens.</p>
  <button class="…outline">Try again</button>
</div>
```

Field-level errors go in the reserved message line; request-level errors go to a toast (`Sonner`, `position="top-center"`).
A form-level message sits above the fields as `<p class="text-destructive mb-4 text-center text-sm" role="alert">`.

**Onboarding nudge:** a dashed card, which is the app's visual shorthand for "nothing here yet, do this next".

```html
<Card class="border-dashed">
  <CardHeader><CardTitle>Get Started</CardTitle><CardDescription>…</CardDescription></CardHeader>
  <CardFooter><button>Set Up a Hub</button></CardFooter>
</Card>
```

**Transitions.** Motion is minimal and functional:

- Sidebar width `duration-300 ease-in-out`; logo text `duration-200` with the delay asymmetry described in §2.3.
- Accordions animate height against a measured content-height variable, `0.2s ease-out`.
- Caret blink is `1.25s ease-out infinite`.
- Spinners and a refresh icon during refetch use `animate-spin`.
- For crossfading two states in the same box without collapse, everything is stacked in one grid cell:

```css
.transition-in-place { display: grid; }
.transition-in-place > * { grid-area: 1/1/2/2; }
```

---

## 6. Responsive rules

Breakpoints are stock Tailwind. Only four are used, and each has a job.

| Breakpoint | Used for |
|---|---|
| `sm` (640px) | container padding step-up, 1→3 column stat grid, empty-state size bump |
| `md` (768px) | sidebar becomes fixed rather than a drawer; **the table/card structural swap** |
| `lg` (1024px) | reveals secondary text, e.g. the username next to the avatar |
| `xl` | rare |

Principles:

1. **Mobile-first.** Base classes are the small layout; breakpoints add.
2. **Prefer intrinsic responsiveness.** `grid-cols-[repeat(auto-fill,minmax(20rem,1fr))]` and `flex-wrap` handle most cases with zero breakpoints. Reach for `sm:`/`md:` only when the content count is fixed.
3. **Horizontal scrolling is a bug.** The shell is `overflow-x-hidden`; the fix for a wide table is a different layout, not a scrollbar.
4. **Truncate, do not wrap, in dense rows.** `min-w-0 flex-1` + `truncate` + a `title` attribute carrying the full text.
5. **Hide chrome, never content.** `hidden sm:flex` is for social links and decorative labels only.

Accessibility habits that show up in the markup: `aria-label` on icon-only buttons, `title` on truncated text and status dots, `aria-hidden="true"` on decorative layers and spacer divs, `role="status"` + `aria-live="polite"` on validation lines, `role="alert"` on form errors, `aria-invalid` + `aria-describedby` wiring inputs to their messages.

---

## 7. How code is split into components

### 7.1 Three tiers

```
packages/svelte-core/src/lib/components/   ← tier 1: shared design system (own package)
  ui/                                        vendored shadcn primitives, one folder per primitive
  Container.svelte  PageHeader.svelte  EmptyState.svelte  Code.svelte  Keyboard.svelte  …
  input/  stepper/  datetime-picker/  dialog-manager/  multi-select-combobox/  svg/  metadata/
  index.ts                                   barrel

src/lib/components/                        ← tier 2: app-wide, product-specific
  ControlModules/   Table/   auth/   shares/   input/   svg/   utils/
  Turnstile.svelte  ExpirationPicker.svelte  Stepper.svelte  …

src/routes/<route>/                        ← tier 3: colocated, used by exactly one page
  +page.svelte  columns.ts  data-table-actions.svelte  dialog-token-edit.svelte
```

The promotion rule is simple and strictly followed:

- **Used by one route → live in that route's folder.** Do not preemptively move it to `$lib`.
- **Used by two or more routes → `src/lib/components/`.**
- **Product-agnostic and reusable by another app → the design-system package.** Anything in tier 1 must not know what a shocker is.

Route-level chrome (`Header.svelte`, `Footer.svelte`, `Sidebar.svelte`, `Breadcrumb.svelte`, `WelcomeScreen.svelte`) sits next to the root `+layout.svelte`, not in `$lib`, because it belongs to the layout.

### 7.2 Naming

- **PascalCase** for hand-written components: `PageHeader.svelte`, `ShockerCard.svelte`.
- **kebab-case** for vendored shadcn parts and their route-local companions: `sidebar-provider.svelte`, `data-table-actions.svelte`, `dialog-token-edit.svelte`.
- Multi-part components get a folder plus `index.ts`, and are imported namespaced: `import * as Card from '…/ui/card'` then `<Card.Root>`, `<Card.Header>`, `<Card.Title>`.
- Private sub-pieces go in an `impl/` folder (`ControlModules/impl/PauseOverlay.svelte`), which marks them as not-for-import-elsewhere.
- Non-component companions keep their own extension: `columns.ts`, `types.ts`, `ModuleType.ts`, `*-state.svelte.ts` for state.

### 7.3 Layout components pass through, they do not wrap logic

Every shared layout component takes `class` and merges it, so callers can adjust without forking:

```ts
class={cn('container mx-auto flex h-full flex-col …', className)}
```

`cn` is `clsx` + `tailwind-merge`, so a caller's `p-0!` genuinely replaces the default padding instead of fighting it.
Build this helper yourself if you do not have it; the merge behaviour is what makes the pattern safe.

Optional regions are slots, and the wrapper markup for a region is only rendered when the slot is filled:

```svelte
{#if children}
  <div class="ml-auto flex flex-wrap items-center justify-end gap-1">
    {@render children()}
  </div>
{/if}
```

That conditional matters visually: an empty action row would still consume `gap` on the parent.

Variant props stay boolean and few. `EmptyState` has exactly one (`compact`) and switches whole class strings on it rather than accumulating modifiers.

### 7.4 When a page gets split

A route file stays whole until one of these is true:

1. **A row-level action cluster.** Table/list row menus always become their own file (`data-table-actions.svelte`, `token-actions.svelte`), because they carry their own dialogs and mutations.
2. **A dialog with a body of its own.** Extracted as `dialog-*.svelte`. Trivial dialogs stay inline as a snippet in the page (see below).
3. **Column definitions.** Always `columns.ts`, never inline in the page.
4. **A repeated visual block.** The moment the same markup appears twice it becomes a component, even if only used on that page.

Small, single-use markup is a **snippet inside the page file**, not a component. This is very common for one-off dialog bodies:

```svelte
{#snippet createHubSnippet(props)}
  <Dialog.Header><Dialog.Title>Create Hub</Dialog.Title></Dialog.Header>
  <TextInput label="Hub Name" placeholder="My Hub" bind:value={props.data.name} />
  <Button disabled={!props.data.name.trim()} onclick={() => props.resolve(…)}>Create</Button>
{/snippet}
```

Snippets are also used to de-duplicate repeated markup *within* one file (the sidebar renders group → menu → sub-item entirely through nested snippets driven by a data array).
If you are on a framework without snippets, the equivalents are a local render function or a small file-local component; the principle is that one-page markup does not earn a file.

### 7.5 Nav and menus are data, not markup

The sidebar declares typed arrays of groups → menus → sub-items, each entry `{ title, Icon, href, subItems? }`, and renders them through one generic snippet.
Role-gated sections are appended to the array conditionally.
Add a nav entry by editing the array, never by pasting markup.

### 7.6 Where styles are allowed to live

1. **Utility classes in the markup.** The default, and the overwhelming majority.
2. **A scoped `<style>` block.** Only when the effect cannot be expressed as utilities: mask gradients, layered background gradients, keyframes tied to one component. The dot grid is the canonical example.
3. **The shared theme file.** Tokens, base layer, keyframes, custom variants, custom utilities. Nothing app-specific.
4. **The app's own `app.css`.** Third-party CSS overrides only, for example theming the tour library's popovers to match the dark UI.

Inline `style` attributes are reserved for genuinely dynamic values that cannot be a class: `--mouse-x`, `--sidebar-width`, and a couple of one-off brand colors.

Two conventions that keep utility strings readable:

- Classes are auto-sorted (Prettier's Tailwind plugin); do not hand-order them.
- Conditional classes are written as a whole-string ternary rather than concatenated fragments:

```svelte
class={onlineHubCount > 0 ? 'size-2 rounded-full bg-green-500' : 'size-2 rounded-full bg-red-500'}
```

The repetition is intentional; it keeps both branches greppable and sortable.

---

## 8. Checklist

Before shipping a screen:

- [ ] Outermost element is the page container; nothing re-implements its padding.
- [ ] Section spacing comes from the container's `gap-4`, or an explicit `gap-6` between major blocks. No stray margins.
- [ ] Title uses `text-3xl font-bold`, subtitle is `mt-1 text-muted-foreground`, actions are `ml-auto` in a wrapping cluster.
- [ ] No literal gray, white, or black. Tokens only, except for the sanctioned status colors.
- [ ] Borders and dividers carry **no** color class.
- [ ] Every async region has loading, empty, and error renderings, and the loading one preserves layout height.
- [ ] Flex children that truncate have `min-w-0`, and a `title` with the full text.
- [ ] Nothing scrolls horizontally at 360px wide.
- [ ] Icon-only controls have `aria-label`; decorative layers have `aria-hidden`.
- [ ] Checked in both themes, including the alpha borders on cards vs. page background.
