// Keep application imports behind the dedicated config flag so opening this
// HTML with the normal dev server can never reach real services or accounts.
if (import.meta.env.DEV && import.meta.env.VITE_POST_PARSER_QA === "true") {
  void import("./PreviewApp");
} else {
  const root = document.getElementById("root");

  if (root) {
    root.textContent =
      "This preview requires the dedicated safe mock server: node node_modules/vite/bin/vite.js --config vite.qa-post-parser.config.ts";
  }
}
