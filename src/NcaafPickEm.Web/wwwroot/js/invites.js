// JS module for the Invites page (P1-02). Loaded via
// IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/invites.js") - no inline <script>
// per 05-Conventions.md.

/**
 * Shares (or, with no Web Share support, copies) an invite link.
 * @param {string} title
 * @param {string} text
 * @param {string} url
 * @returns {Promise<"shared"|"copied"|"unsupported">} which path was taken.
 */
export async function share(title, text, url) {
    if (navigator.share) {
        try {
            await navigator.share({ title, text, url });
            return "shared";
        } catch (err) {
            // AbortError: the user dismissed the share sheet - not a failure worth reporting.
            if (err && err.name === "AbortError") {
                return "shared";
            }
            // Fall through to clipboard on any other Web Share failure.
        }
    }

    if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(url);
        return "copied";
    }

    return "unsupported";
}
