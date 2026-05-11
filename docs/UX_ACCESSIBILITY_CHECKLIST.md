# UX And Accessibility Checklist

Use this checklist for every new tool and during significant UX refactors.

## Keyboard

- The primary input receives focus on load.
- The primary action is reachable without a mouse.
- `Enter` or `Ctrl+Enter` is wired when the tool has a clear main action.
- The tab order is explicit when the layout is dense or non-linear.
- Icon-only buttons have a tooltip and an automation name.

## Async Behavior

- Long-running operations show visible progress.
- Inputs that should not change during execution are disabled or read-only.
- A visible `Stop` or `Cancel` action exists when cancellation is supported.
- Repeated clicks cannot start duplicate concurrent runs.

## Feedback States

- Validation errors are shown inline.
- Empty states are explicit and localized.
- Results surfaces stay hidden until there is meaningful content.
- Filtered-no-result states are different from first-use empty states when relevant.

## Layout

- The tool remains usable in split view or narrow panes.
- Dense action bars wrap instead of crushing the main input.
- Large result grids hide secondary columns before becoming unreadable.
- Footers and status areas stay readable on narrow widths.

## Sidebar And Search

- Session sidebar search stays full-width on the first toolbar row, with action buttons on the second row.
- Icon-only sidebar actions keep localized tooltips and `AutomationProperties.Name`.
- Long session names preserve the leading identifier; tooltips expose the full `DisplayName`.

## Localization

- No user-visible placeholder or default text is hardcoded in XAML.
- Visible demo values are avoided unless they are intentional product presets.
- Watermarks, labels, tooltips, and status messages come from i18n keys.

## Validation Pass

- Run `dotnet build Heimdall.slnx -c Debug`.
- Open the tool in a narrow pane and a normal pane.
- Test first-use empty state, error state, loading state, and success state.
- Test the full flow without using the mouse.
