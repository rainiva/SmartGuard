# SmartGuard Settings Theme Title Bar Transition Design

Date: 2026-07-03

## Goal

Make Settings window dark/light theme switching feel smoother and more natural by preventing the title bar from switching before the window content transition has visually started.

## Problem

The current Settings theme flow mixes two different timing models:

- the content area transitions through `ThemeTransitionAnimator.AnimateTransition(...)`
- the native window title bar switches immediately through `WindowTitleBarTheme.Apply(...)`

This produces a visible mismatch during manual theme toggles: the title bar snaps first, then the content animates afterward.

## Chosen Approach

Use the existing transition timeline as the single timing source.

- Keep immediate title bar application for first-load, test-mode, no-animation, and same-theme paths.
- For animated manual theme transitions only, defer `WindowTitleBarTheme.Apply(...)` until the animation midpoint callback.
- Reuse the existing `ThemeTransitionAnimator` hooks instead of introducing a second timer or a new custom title bar animation path.

## Design

### 1. Theme timing ownership

`ThemeTransitionAnimator` already owns the content transition duration and midpoint timing through:

- `ThemeTransitionProfile.TotalDuration`
- `ThemeTransitionProfile.TitleBarApplyAt`
- `AnimateTransition(..., onMidpoint: ...)`

The title bar transition will follow that existing midpoint instead of switching immediately from `SettingsThemeCoordinator`.

### 2. SettingsThemeCoordinator behavior

`SettingsThemeCoordinator.ApplyTheme(...)` will keep the current behavior for these paths:

- initial theme application
- UI test mode
- fallback after animation failure
- repeated apply of the same theme

Only the animated branch changes:

- today: call `WindowTitleBarTheme.Apply(window, isDark)` before starting the content animation
- new behavior: pass `() => WindowTitleBarTheme.Apply(window, isDark)` as the animator midpoint callback

This keeps the implementation minimal and preserves existing stable branches.

### 3. WindowTitleBarTheme behavior

`WindowTitleBarTheme` remains the native DWM writer for the title bar and does not gain its own timing logic.

Its responsibility stays narrow:

- remember the last requested dark-mode value for tests
- apply DWM dark-mode attributes when asked

The timing decision stays outside it, in the theme coordinator / animator path.

## Data Flow

Manual dark/light toggle:

1. User changes `tglThemeLight` or `tglThemeDark`
2. `SettingsThemeCoordinator.SetManualTheme(...)` updates state
3. `SettingsThemeCoordinator.ApplyTheme(...)` starts the content animation
4. `ThemeTransitionAnimator` reaches `TitleBarApplyAt`
5. Midpoint callback applies the native title bar theme
6. Animation completes and post-theme refresh work runs

Non-animated paths:

1. Theme source changes
2. `SettingsThemeCoordinator.ApplyTheme(...)` applies title bar immediately
3. Resources apply immediately or through fallback

## Error Handling

- If animated transition setup fails and falls back to immediate apply, the title bar must still be applied immediately in the fallback path.
- If the window has no native handle yet, `WindowTitleBarTheme` should keep its current deferred-on-`SourceInitialized` behavior.
- No new background timers, retry loops, or state flags will be introduced.

## Testing

Add RED tests before implementation:

- a coordinator-level test proving manual animated theme change does not flip the title bar request immediately at animation start
- an animator-level test proving midpoint callbacks are scheduled separately from synchronous animation setup

Verify GREEN with:

- targeted `SmartGuard.Settings.Tests` theme/title-bar tests
- relevant broader `SmartGuard.Settings.Tests` regression coverage

## Out Of Scope

- redesigning the theme palette
- changing overall transition duration or easing profile
- building a custom-drawn title bar
- changing system-follow behavior beyond timing alignment

## Success Criteria

- manual dark/light switching no longer shows a title-bar-first snap
- initial load and fallback paths remain stable
- theme tests cover the timing contract so the regression does not return
