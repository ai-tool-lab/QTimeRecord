---
name: Precision Kiosk Blue
colors:
  surface: '#f4f6fb'
  surface-dim: '#d9e2f2'
  surface-bright: '#f4f6fb'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#edf2fa'
  surface-container: '#e8eef8'
  surface-container-high: '#dfe6f3'
  surface-container-highest: '#dbe4f4'
  on-surface: '#111827'
  on-surface-variant: '#43495a'
  inverse-surface: '#293040'
  inverse-on-surface: '#edf0ff'
  outline: '#727a8e'
  outline-variant: '#c2c9db'
  surface-tint: '#243c82'
  primary: '#1d2d5b'
  on-primary: '#ffffff'
  primary-container: '#253b80'
  on-primary-container: '#9bb8ff'
  inverse-primary: '#b5c5fc'
  secondary: '#244bbd'
  on-secondary: '#ffffff'
  secondary-container: '#2f60de'
  on-secondary-container: '#f8faff'
  tertiary: '#003e17'
  on-tertiary: '#ffffff'
  tertiary-container: '#005823'
  on-tertiary-container: '#56d474'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#dbe1ff'
  primary-fixed-dim: '#b5c5fc'
  on-primary-fixed: '#051846'
  on-primary-fixed-variant: '#354574'
  secondary-fixed: '#dce1ff'
  secondary-fixed-dim: '#b6c4ff'
  on-secondary-fixed: '#001550'
  on-secondary-fixed-variant: '#093bae'
  tertiary-fixed: '#a6f5ae'
  tertiary-fixed-dim: '#8bd894'
  on-tertiary-fixed: '#002109'
  on-tertiary-fixed-variant: '#005320'
  background: '#f9f9ff'
  on-background: '#141b2b'
  surface-variant: '#dce2f7'
  text-muted: '#59627a'
  border-subtle: '#e5ebf5'
  scan-beam: '#2f60de'
  scan-glow: '#38bdf8'
typography:
  clock-display:
    fontFamily: Inter
    fontSize: 64px
    fontWeight: '700'
    lineHeight: 72px
    letterSpacing: -0.02em
  clock-display-mobile:
    fontFamily: Inter
    fontSize: 44px
    fontWeight: '700'
    lineHeight: 52px
    letterSpacing: -0.01em
  headline-lg:
    fontFamily: Noto Sans
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
  headline-md:
    fontFamily: Noto Sans
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
  headline-sm:
    fontFamily: Noto Sans
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  kiosk-button:
    fontFamily: Noto Sans
    fontSize: 22px
    fontWeight: '700'
    lineHeight: 28px
    letterSpacing: 0.02em
  body-lg:
    fontFamily: Noto Sans
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Noto Sans
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-lg:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 20px
    letterSpacing: 0.01em
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '500'
    lineHeight: 16px
  tabular-data:
    fontFamily: Inter
    fontSize: 15px
    fontWeight: '500'
    lineHeight: 22px
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  pad-2xs: 0.25rem
  pad-xs: 0.5rem
  pad-sm: 0.75rem
  pad-md: 1rem
  pad-lg: 1.5rem
  pad-xl: 2rem
  pad-2xl: 3rem
  gap-grid: 1rem
  margin-screen: 1.5rem
  touch-min: 4rem
  touch-comfort: 5rem
---

## Brand & Style

Precision Kiosk Blue is engineered for high-throughput, mission-critical workforce terminals and dedicated hardware kiosks. The aesthetic pairs industrial-grade reliability with modern corporate poise. 

Key attributes:
- **Atmosphere**: Authoritative, legible, swift, and ultra-reliable.
- **Visual Style**: Clean modern enterprise with tactile kiosk feedback cues. Interfaces utilize crisp, multi-tone blue/slate surface layering alongside high-contrast status markers to ensure glanceable clarity from standing distances under harsh retail or commercial lighting.
- **Target Experience**: Zero-latency perception, clear spatial landmarks, and explicit feedback states for scanning, touch interaction, and administrative oversight.

## Colors

The palette is anchored by deep navy (`#1d2d5b`) and optical indigo (`#244bbd`), providing enterprise authority with vivid interactive cues.

- **Background & Surfaces**: Tonal stepping from pure card white (`#ffffff`) over light slate (`#edf2fa`, `#e8eef8`) onto the core canvas (`#f4f6fb`) establishes visual hierarchy without heavy drop shadows.
- **Feedback & Status Accents**: 
  - Emerald green (`#005823` / `#56d474`) signals active connectivity, valid scans, and system operation.
  - Laser cyan (`#38bdf8`) accents interactive scan beams.
  - High-visibility crimson (`#ba1a1a` / `#ffdad6`) delivers unmistakable alerts, timeouts, and cancellation alerts.

## Typography

The type system blends the geometric precision and monospaced tabular alignment of **Inter** for real-time clocks, numeric badges, and hardware stats, alongside **Noto Sans** for broad CJK language fidelity, clear instructional prose, and kiosk touch targets.

- **Tabular Numerics**: Clocks, countdowns, and employee PIN pads strictly mandate tabular numerical alignment (`font-feature-settings: 'tnum'`) to prevent horizontal jitter during real-time seconds updates.
- **Kiosk Legibility**: Major interactive callouts and confirmation headers preserve generous line-heights and bold weights (600–800) for instant comprehension at arm's length.

## Layout & Spacing

Layouts follow an asymmetric 12-column grid system tuned for landscape kiosks (1920x1080 and 1366x768 POS terminals), responsive to tablet/mobile viewports:

- **Header / Navigation Bar**: Fixed 80px double-deck header isolating store identity and global connectivity status from contextual navigation switches.
- **Main Viewport Stage**: Center stage allocates 8 columns to the interactive primary scanner/viewfinder area, while the remaining 4 columns display real-time shift context and operational guides.
- **Touch Ergonomics**: All interactive elements satisfy a strict minimum hit area of `4rem` (64px) for primary actions and `3.5rem` (56px) for numeric keypad inputs to guarantee miss-free physical touch operation.

## Elevation & Depth

The system uses subtle, tonal layering combined with low-diffusion ambient shadows to provide a sense of hardware durability:

- **Base Layer (L0)**: Canvas tinted at `#f4f6fb`.
- **Card Panels (L1)**: Pure white `#ffffff` cards framed with hairline borders (`1px solid #e5ebf5`) and delicate base shadows (`0 1px 3px rgba(29, 45, 91, 0.05)`).
- **Interactive Recesses (Inset)**: Internal wells, PIN inputs, and status containers use sunken tonal backdrops (`#edf2fa` / `#e8eef8`) with inset borders (`1px solid #dce4f3`).
- **Overlays & Keypads (L2)**: Modals and raised scan targets use elevated depth (`0 20px 25px -5px rgba(17, 24, 39, 0.1), 0 8px 10px -6px rgba(17, 24, 39, 0.1)`) paired with backdrop blur (`backdrop-blur-sm`).

## Shapes

The interface adheres to a structured, semi-compact curvature profile (`roundedness: 1`):
- **Cards & Primary Modules**: `rounded-xl` (12px / 0.75rem) to ensure crisp layout boundaries.
- **Buttons, Keypads & Data Insets**: `rounded-lg` (8px / 0.5rem) providing clear tap boundaries.
- **Status Pills & Indicators**: `rounded-full` (9999px) to signal non-interactive metadata tags and system pulses.
- **Viewfinder Reticle**: Inset 4px L-shaped corner guides defining optical hardware reading windows.

## Components

### Buttons & Kiosk Triggers
- **Primary Kiosk Touch Button**: Solid `#1d2d5b` fill, white text, bold typography, subtle bottom translate on active (`active:translate-y-0.5`). Minimum height: 56px–64px.
- **Secondary Touch Alternative**: Bordered `#edf2fa` background, `#1d2d5b` text, hover transition to `#dbe4f4`.
- **Keypad Buttons**: Large touch squares with centered tabular numerals, 16px border-radius, instant active feedback states (`active:scale-95`).

### Status Badges & Hardware Indicators
- Pill-shaped flex items with pulsing dot indicators (`w-2 h-2 rounded-full animate-pulse`).
- Connected state: Green dot on soft slate-blue pill with 1px outline.

### QR / Barcode Viewfinder Box
- Centered 288x288px (18rem) pure white container framed with navy corner reticle indicators (`shadow-[inset_4px_4px_0_0_#1d2d5b]`).
- Internal animated scanning bar utilizing `#2f60de` with a cyan-tinted ambient glow (`#38bdf8`).
- Subtle background matrix grid (`radial-gradient(#1d2d5b 1px, transparent 1px)` with 12px spacing).

### Information & Announcement Cards
- Slate container cards (`#edf2fa`) framed with `#e0e7f4`.
- Header row with colored category chips (e.g., General Affairs in `#d8e2ff`, Hygiene in green, Deadlines in `#ffdad6`).
- Bottom metadata separator featuring forward navigation icon targets (`chevron_right`).