---
name: LumosPresenter Design System
light-colors:
  surface: '#faf8ff'
  surface-dim: '#d2d9f4'
  surface-bright: '#faf8ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f2f3ff'
  surface-container: '#eaedff'
  surface-container-high: '#e2e7ff'
  surface-container-highest: '#dae2fd'
  on-surface: '#131b2e'
  on-surface-variant: '#424754'
  inverse-surface: '#283044'
  inverse-on-surface: '#eef0ff'
  outline: '#727785'
  outline-variant: '#c2c6d6'
  surface-tint: '#005ac2'
  primary: '#0058be'
  on-primary: '#ffffff'
  primary-container: '#2170e4'
  on-primary-container: '#fefcff'
  inverse-primary: '#adc6ff'
  secondary: '#006c49'
  on-secondary: '#ffffff'
  secondary-container: '#6cf8bb'
  on-secondary-container: '#00714d'
  tertiary: '#825100'
  on-tertiary: '#ffffff'
  tertiary-container: '#a36700'
  on-tertiary-container: '#fffbff'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#d8e2ff'
  primary-fixed-dim: '#adc6ff'
  on-primary-fixed: '#001a42'
  on-primary-fixed-variant: '#004395'
  secondary-fixed: '#6ffbbe'
  secondary-fixed-dim: '#4edea3'
  on-secondary-fixed: '#002113'
  on-secondary-fixed-variant: '#005236'
  tertiary-fixed: '#ffddb8'
  tertiary-fixed-dim: '#ffb95f'
  on-tertiary-fixed: '#2a1700'
  on-tertiary-fixed-variant: '#653e00'
  background: '#faf8ff'
  on-background: '#131b2e'
  surface-variant: '#dae2fd'

dark-colors:
  surface: '#0b1326'
  surface-dim: '#0b1326'
  surface-bright: '#31394d'
  surface-container-lowest: '#060e20'
  surface-container-low: '#131b2e'
  surface-container: '#171f33'
  surface-container-high: '#222a3d'
  surface-container-highest: '#2d3449'
  on-surface: '#dae2fd'
  on-surface-variant: '#c2c6d6'
  inverse-surface: '#dae2fd'
  inverse-on-surface: '#283044'
  outline: '#8c909f'
  outline-variant: '#424754'
  surface-tint: '#adc6ff'
  primary: '#adc6ff'
  on-primary: '#002e6a'
  primary-container: '#4d8eff'
  on-primary-container: '#00285d'
  inverse-primary: '#005ac2'
  secondary: '#4edea3'
  on-secondary: '#003824'
  secondary-container: '#00a572'
  on-secondary-container: '#00311f'
  tertiary: '#ffb95f'
  on-tertiary: '#472a00'
  tertiary-container: '#ca8100'
  on-tertiary-container: '#3e2400'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#d8e2ff'
  primary-fixed-dim: '#adc6ff'
  on-primary-fixed: '#001a42'
  on-primary-fixed-variant: '#004395'
  secondary-fixed: '#6ffbbe'
  secondary-fixed-dim: '#4edea3'
  on-secondary-fixed: '#002113'
  on-secondary-fixed-variant: '#005236'
  tertiary-fixed: '#ffddb8'
  tertiary-fixed-dim: '#ffb95f'
  on-tertiary-fixed: '#2a1700'
  on-tertiary-fixed-variant: '#653e00'
  background: '#0b1326'
  on-background: '#dae2fd'
  surface-variant: '#2d3449'
  slate-surface: '#1E293B'
  slate-muted: '#64748B'
  emerald-live: '#10B981'
  amber-warning: '#F59E0B'
  rose-error: '#F43F5E'
  navy-deep: '#020617'
typography:
  display-live:
    fontFamily: Hanken Grotesk
    fontSize: 48px
    fontWeight: '700'
    lineHeight: 56px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Hanken Grotesk
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
  headline-md:
    fontFamily: Hanken Grotesk
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  body-lg:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  status-label:
    fontFamily: JetBrains Mono
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
    letterSpacing: 0.05em
  mono-ui:
    fontFamily: JetBrains Mono
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  headline-lg-mobile:
    fontFamily: Hanken Grotesk
    fontSize: 28px
    fontWeight: '600'
    lineHeight: 36px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  unit: 4px
  gutter: 16px
  margin-mobile: 16px
  margin-desktop: 32px
  container-max: 1440px
---

## Brand & Style

The design system is engineered for "LumosPresenter," a mission-critical tool for live scripture display. The brand personality is **authoritative, dependable, and precision-oriented**, mirroring the high-stakes environment of a live production booth. It prioritizes the "Operator-First" philosophy, where clarity and speed of information retrieval take precedence over decorative elements.

The visual style is **Corporate / Modern with a Functional utility bias**. It utilizes high-contrast interfaces and a structured density to evoke the feeling of a professional command center. The aesthetic is "Technical Minimalist"—clean and spacious where focus is required, but data-dense where control is necessary.

**Key Brand Pillars:**
- **Vigilance:** Always-on monitoring through clear status indicators and real-time feedback.
- **Legibility:** High-contrast color pairings and robust typography optimized for low-light environments.
- **Reliability:** A rigid layout structure that feels stable and predictable under pressure.

## Colors

The palette is optimized for **Dark Mode** as the default and primary state, specifically tailored for the dim lighting of sound booths and control rooms.

- **Primary (Blue #3B82F6):** Reserved for primary controls, navigation, and focus states. It represents the "Command" layer.
- **Secondary / Live (Emerald #10B981):** This is the most critical "Action" color. It signifies "Live" status—what the audience currently sees.
- **Tertiary / Warning (Amber #F59E0B):** Used for "Confirm Mode" cues and system warnings. It signifies high-priority attention without immediate alarm.
- **Neutral (Navy/Slate #0F172A):** The foundation of the UI. We use `navy-deep` (#020617) for the primary background to maximize contrast with text, and `slate-surface` (#1E293B) for containers and cards to create depth.

**Color Usage Guidelines:**
- Avoid pure black (#000000) to prevent OLED smearing; use `navy-deep`.
- Use `rose-error` sparingly for hardware disconnects or engine failures.
- All text on `navy-deep` must be at least `slate-200` (#E2E8F0) to ensure WCAG AAA contrast levels for accessibility in high-glare or low-light situations.

## Typography

The typography strategy separates **Display**, **Interface**, and **Data**.

1.  **Display (Hanken Grotesk):** A sharp, contemporary sans-serif used for headlines and the "Active Verse" display. It conveys professionalism and high-tech precision.
2.  **Interface (Inter):** The workhorse for the operator console. Its neutral, systematic nature ensures that instructions and labels are legible and unobtrusive.
3.  **Data & Status (JetBrains Mono):** Used for live transcription feeds, verse references (e.g., "JOHN 3:16"), and system logs. The monospaced nature helps operators scan reference numbers quickly and gives the app its "mission-critical" utility feel.

**Hierarchy Rules:**
- **Upper Case:** Use for `status-label` to denote system states (e.g., "LISTENING", "LIVE", "STAGED").
- **Optical Sizing:** For the Projection Display page, use `display-live`. Ensure a minimum of 4.5:1 contrast ratio, though 7:1 is preferred for projection.

## Layout & Spacing

The design system utilizes a **Fixed Grid** approach for the Operator Console to ensure controls do not move unexpectedly during high-pressure live use.

- **Grid Model:** A 12-column grid on desktop with 16px gutters.
- **Rhythm:** An 8px base unit (derived from a 4px sub-unit) governs all padding and margins to maintain mathematical consistency.
- **Structure:** 
    - **Sidebar (Left):** 280px fixed width for primary navigation and engine settings.
    - **Main Control Area (Center):** Fluid within the 12-column grid for the transcription feed and verse staging.
    - **Status Rail (Right):** 320px fixed width for live history and system health metrics.

**Responsiveness:**
- **Mobile (<768px):** The layout collapses into a single-column stack. The "Live" display preview moves to a sticky top bar so the operator can always see what is being projected.
- **Tablet (768px - 1024px):** Sidebars become collapsible drawers to prioritize the staging area.

## Elevation & Depth

This design system avoids traditional skeuomorphism and heavy shadows in favor of **Tonal Layers** and **Low-Contrast Outlines**. This prevents the UI from feeling "mushy" and maintains the crispness needed for an operator interface.

- **Surface Levels:**
    - **Level 0 (Background):** `navy-deep` (#020617).
    - **Level 1 (Cards/Containers):** `slate-surface` (#1E293B).
    - **Level 2 (Popovers/Modals):** `slate-800` (#1E293B) with a subtle 1px border of `slate-700` (#334155).
- **Interactive Depth:** Instead of shadows, use **inner glows or border-color shifts** to indicate focus. For example, an active input field should have a 1px `primary-blue` border and no shadow.
- **Live State:** The "Live" container should have a subtle outer glow using `emerald-live` with 20% opacity to clearly distinguish it from the "Staged" or "Draft" areas.

## Shapes

The shape language is **Soft (0.25rem / 4px)**. This minimal rounding retains a serious, "engineered" look while removing the harshness of 90-degree corners. 

- **Primary Elements:** Buttons and Input fields use the 4px base radius.
- **Containers:** Larger cards and the staging area use `rounded-lg` (8px).
- **Status Pills:** Small status indicators (e.g., "CONFIRMED") may use `rounded-full` (pill-shaped) to distinguish them from interactive buttons.

## Components

### Buttons
- **Primary (Action):** Filled `primary-blue` with white text. High-contrast.
- **Live/Commit:** Filled `emerald-live` with white text. Used for the "Push to Screen" action.
- **Ghost/Utility:** `slate-700` border with transparent background. Used for secondary settings.

### Input Fields
- Dark backgrounds (`navy-deep`) with `slate-700` borders. 
- Placeholder text in `slate-muted`. 
- Focus state: 2px `primary-blue` border.

### Status Indicators (Pills)
- Use `jetbrainsMono` for text.
- **LISTENING:** Pulse animation on a small emerald dot next to the text.
- **WARNING:** Solid `amber-warning` background with black text for maximum visibility.

### Verse Cards
- **Staged Verse:** `slate-surface` background, white text. Large "Push Live" button on the right.
- **Live Verse:** A thick 4px left-border of `emerald-live`. Background is slightly lighter than the staging area to draw the eye.

### Transcription Feed
- Uses `mono-ui` typography. 
- Spoken references are automatically highlighted with a subtle `primary-blue` background tint (20% opacity) to show the parser is working.

### EasyWorship Import Modal
- Structured list with "Status" icons (Ready, Mapping, Error).
- Large, clear "Confirm Import" button at the bottom right.