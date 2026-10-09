// JS module for the NumberStepper component (D-188). Loaded via
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/number-stepper.js") - no inline
// <script> per 05-Conventions.md.

/**
 * Selects the value box's text so typing replaces the number instead of appending to it.
 * @param {HTMLInputElement} input
 */
export function selectAll(input) {
    input?.select();
}
