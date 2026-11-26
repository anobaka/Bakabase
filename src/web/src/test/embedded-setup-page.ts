import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const directory = resolve(__dirname, "../../../apps/Bakabase.Service/Components/ServerData");

// Match ImportProgressServer's inlining so jsdom runs the same nonce-scoped script.
export function readSetupPage(name: "ServerSetup" | "ImportProgress") {
  const html = readFileSync(resolve(directory, name + ".html"), "utf8");

  return html.replace("/*__SETUP_I18N__*/", () =>
    readFileSync(resolve(directory, "SetupI18n.js"), "utf8"),
  );
}
