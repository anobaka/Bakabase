/**
 * i18n Migration Utility Script
 *
 * This script helps migrate from natural language keys to token-based keys.
 *
 * Usage:
 *   npx ts-node scripts/i18n-migrate.ts scan <directory>     - Scan directory for t() calls
 *   npx ts-node scripts/i18n-migrate.ts migrate <file>       - Migrate a single file
 *   npx ts-node scripts/i18n-migrate.ts extract <module>     - Extract keys for a module
 */

import * as fs from "fs";
import * as path from "path";

// Key mapping from old natural language keys to new token-based keys
const keyMappings: Record<string, string> = {
  // ==================== common.action ====================
  Save: "common.action.save",
  Cancel: "common.action.cancel",
  Delete: "common.action.delete",
  Confirm: "common.action.confirm",
  Edit: "common.action.edit",
  Add: "common.action.add",
  Remove: "common.action.remove",
  Close: "common.action.close",
  Start: "common.action.start",
  Stop: "common.action.stop",
  Pause: "common.action.pause",
  Resume: "common.action.resume",
  Refresh: "common.action.refresh",
  Search: "common.action.search",
  Preview: "common.action.preview",
  Configure: "common.action.configure",
  Validate: "common.action.validate",
  Back: "common.action.back",
  Next: "common.action.next",
  Previous: "common.action.previous",
  Submit: "common.action.submit",
  Reset: "common.action.reset",
  Clear: "common.action.clear",
  Copy: "common.action.copy",
  Duplicate: "common.action.duplicate",
  Enable: "common.action.enable",
  Disable: "common.action.disable",
  Import: "common.action.import",
  Export: "common.action.export",
  Download: "common.action.download",
  Upload: "common.action.upload",
  Play: "common.action.play",
  "Open folder": "common.action.openFolder",
  "Clear all": "common.action.clearAll",
  "Select all": "common.action.selectAll",

  // ==================== common.label ====================
  Yes: "common.label.yes",
  No: "common.label.no",
  None: "common.label.none",
  All: "common.label.all",
  Name: "common.label.name",
  Path: "common.label.path",
  Status: "common.label.status",
  Time: "common.label.time",
  Order: "common.label.order",
  Priority: "common.label.priority",
  Source: "common.label.source",
  Message: "common.label.message",
  Property: "common.label.property",
  Value: "common.label.value",
  Type: "common.label.type",
  Pattern: "common.label.pattern",
  Extensions: "common.label.extensions",
  Total: "common.label.total",
  Count: "common.label.count",
  Progress: "common.label.progress",
  Remaining: "common.label.remaining",
  Description: "common.label.description",
  Introduction: "common.label.introduction",
  Operations: "common.label.operations",
  Setting: "common.label.setting",
  Settings: "common.label.settings",
  Configuration: "common.label.configuration",
  "Custom Properties": "common.label.customProperties",
  "Internal Properties": "common.label.internalProperties",
  "Reserved Properties": "common.label.reservedProperties",
  Warning: "common.label.warning",
  Error: "common.label.error",
  "Created at": "common.label.createdAt",
  "Change log": "common.label.changeLog",
  Development: "common.label.development",
  Log: "common.label.log",
  Children: "common.label.children",

  // ==================== common.state ====================
  "Loading...": "common.state.loading",
  Saved: "common.state.saved",
  Selected: "common.state.selected",
  "No data": "common.state.noData",
  "No results": "common.state.noResults",

  // ==================== common.confirm ====================
  "Are you sure?": "common.confirm.default",
  "Sure?": "common.confirm.short",
  "Are you sure you want to continue?": "common.confirm.continue",
  "Continue anyway": "common.confirm.continueAnyway",
  "Be careful, this operation can not be undone":
    "common.confirm.irreversibleWarning",

  // ==================== status ====================
  Completed: "status.completed",
  Running: "status.running",
  Paused: "status.paused",
  Stopped: "status.stopped",
  Pending: "status.pending",
  Failed: "status.failed",
  Success: "status.success",
  Downloading: "status.downloading",
  Syncing: "status.syncing",
  Started: "status.started",
  Resumed: "status.resumed",
  "Up-to-date": "status.upToDate",

  // ==================== errors ====================
  "Failed to load data": "error.load.data",
  "Failed to save mark": "error.save.mark",
  "Failed to delete mark": "error.delete.mark",
  "Failed to delete marks": "error.delete.marks",
  "Failed to update app": "error.update.app",
  "Failed to add media library": "error.add.mediaLibrary",
  "Failed to remove media library": "error.remove.mediaLibrary",
  "Failed to update property": "error.update.property",
  "Failed to process data": "error.process.data",
  "Failed to get latest version": "error.get.latestVersion",
  "Unknown error": "error.unknown",

  // ==================== validation ====================
  "Name is required": "validation.name.required",
  "Name cannot be empty": "validation.name.notEmpty",
  "Executable path is required": "validation.executablePath.required",

  // ==================== datetime ====================
  "YYYY-MM-DD": "datetime.format.date",
  "YYYY-MM-DD HH:mm:ss": "datetime.format.dateTime",
  "HH:mm": "datetime.format.timeShort",
  "HH:mm:ss": "datetime.format.time",
  "HH:mm:ss.SSS": "datetime.format.timeMs",
};

/**
 * Escape special regex characters in a string
 */
function escapeRegex(str: string): string {
  return str.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Scan a file for t() calls and return all keys found
 */
function scanFileForKeys(filePath: string): string[] {
  const content = fs.readFileSync(filePath, "utf-8");
  const keys: string[] = [];

  // Match t('...') and t("...")
  const singleQuoteRegex = /t\('([^']+)'\)/g;
  const doubleQuoteRegex = /t\("([^"]+)"\)/g;

  let match;
  while ((match = singleQuoteRegex.exec(content)) !== null) {
    keys.push(match[1]);
  }
  while ((match = doubleQuoteRegex.exec(content)) !== null) {
    keys.push(match[1]);
  }

  return keys;
}

/**
 * Scan a directory recursively for all .tsx and .ts files
 */
function scanDirectory(
  dirPath: string,
  filePattern: RegExp = /\.(tsx?|jsx?)$/
): string[] {
  const files: string[] = [];

  function scan(dir: string) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!entry.name.startsWith(".") && entry.name !== "node_modules") {
          scan(fullPath);
        }
      } else if (filePattern.test(entry.name)) {
        files.push(fullPath);
      }
    }
  }

  scan(dirPath);
  return files;
}

/**
 * Migrate keys in a file using the key mappings
 */
function migrateFile(
  filePath: string,
  mappings: Record<string, string> = keyMappings
): { migrated: number; content: string } {
  let content = fs.readFileSync(filePath, "utf-8");
  let migrated = 0;

  for (const [oldKey, newKey] of Object.entries(mappings)) {
    const escapedOldKey = escapeRegex(oldKey);

    // Replace t('oldKey') with t('newKey')
    const singleQuotePattern = new RegExp(
      `t\\('${escapedOldKey}'\\)`,
      "g"
    );
    const doubleQuotePattern = new RegExp(
      `t\\("${escapedOldKey}"\\)`,
      "g"
    );

    const singleMatches = content.match(singleQuotePattern)?.length || 0;
    const doubleMatches = content.match(doubleQuotePattern)?.length || 0;

    if (singleMatches > 0 || doubleMatches > 0) {
      content = content.replace(singleQuotePattern, `t('${newKey}')`);
      content = content.replace(doubleQuotePattern, `t('${newKey}')`);
      migrated += singleMatches + doubleMatches;
    }
  }

  return { migrated, content };
}

/**
 * Generate translation file content from key mappings
 */
function generateTranslationFile(
  prefix: string,
  mappings: Record<string, string>,
  sourceFile: string
): Record<string, string> {
  const result: Record<string, string> = {};

  // Read source translations
  const sourceContent = JSON.parse(fs.readFileSync(sourceFile, "utf-8"));

  for (const [oldKey, newKey] of Object.entries(mappings)) {
    if (newKey.startsWith(prefix)) {
      const value = sourceContent[oldKey] || oldKey;
      result[newKey] = value;
    }
  }

  return result;
}

/**
 * Extract all keys used in a module (page or component)
 */
function extractModuleKeys(
  modulePath: string
): { keys: string[]; files: string[] } {
  const files = scanDirectory(modulePath);
  const allKeys: Set<string> = new Set();

  for (const file of files) {
    const keys = scanFileForKeys(file);
    keys.forEach((key) => allKeys.add(key));
  }

  return {
    keys: Array.from(allKeys).sort(),
    files,
  };
}

/**
 * Print usage information
 */
function printUsage() {
  console.log(`
i18n Migration Utility Script

Usage:
  npx ts-node scripts/i18n-migrate.ts <command> [options]

Commands:
  scan <directory>       Scan directory for all t() calls and list unique keys
  migrate <file>         Migrate a single file using key mappings (dry run)
  migrate <file> --write Migrate a single file and save changes
  extract <module-path>  Extract all keys used in a module (page/component)
  generate <prefix>      Generate translation file for keys with given prefix

Examples:
  npx ts-node scripts/i18n-migrate.ts scan src/pages/dashboard
  npx ts-node scripts/i18n-migrate.ts migrate src/pages/dashboard/index.tsx
  npx ts-node scripts/i18n-migrate.ts extract src/components/FileExplorer
  npx ts-node scripts/i18n-migrate.ts generate common
`);
}

// Main CLI
const args = process.argv.slice(2);
const command = args[0];

switch (command) {
  case "scan": {
    const targetDir = args[1];
    if (!targetDir) {
      console.error("Error: Please provide a directory to scan");
      process.exit(1);
    }

    const files = scanDirectory(targetDir);
    const allKeys: Map<string, string[]> = new Map();

    for (const file of files) {
      const keys = scanFileForKeys(file);
      for (const key of keys) {
        if (!allKeys.has(key)) {
          allKeys.set(key, []);
        }
        allKeys.get(key)!.push(path.relative(process.cwd(), file));
      }
    }

    console.log(`\nFound ${allKeys.size} unique keys in ${files.length} files:\n`);

    const sortedKeys = Array.from(allKeys.entries()).sort((a, b) =>
      a[0].localeCompare(b[0])
    );

    for (const [key, filePaths] of sortedKeys) {
      const mapped = keyMappings[key];
      const status = mapped ? `→ ${mapped}` : "(not mapped)";
      console.log(`  "${key}" ${status}`);
      console.log(`    Used in: ${filePaths.join(", ")}`);
    }
    break;
  }

  case "migrate": {
    const targetFile = args[1];
    const writeFlag = args[2] === "--write";

    if (!targetFile) {
      console.error("Error: Please provide a file to migrate");
      process.exit(1);
    }

    const { migrated, content } = migrateFile(targetFile);

    if (writeFlag) {
      fs.writeFileSync(targetFile, content);
      console.log(`Migrated ${migrated} keys in ${targetFile}`);
    } else {
      console.log(`\nDry run - would migrate ${migrated} keys in ${targetFile}`);
      console.log("Use --write flag to save changes");
    }
    break;
  }

  case "extract": {
    const modulePath = args[1];
    if (!modulePath) {
      console.error("Error: Please provide a module path");
      process.exit(1);
    }

    const { keys, files } = extractModuleKeys(modulePath);
    console.log(`\nModule: ${modulePath}`);
    console.log(`Files scanned: ${files.length}`);
    console.log(`Unique keys found: ${keys.length}\n`);

    for (const key of keys) {
      const mapped = keyMappings[key];
      const status = mapped ? `→ ${mapped}` : "";
      console.log(`  "${key}" ${status}`);
    }
    break;
  }

  case "generate": {
    const prefix = args[1];
    if (!prefix) {
      console.error("Error: Please provide a prefix (e.g., common, status, error)");
      process.exit(1);
    }

    const enSource = path.join(__dirname, "../src/locales/en.json");
    const cnSource = path.join(__dirname, "../src/locales/cn.json");

    const enResult = generateTranslationFile(prefix, keyMappings, enSource);
    const cnResult = generateTranslationFile(prefix, keyMappings, cnSource);

    console.log(`\nEnglish translations for prefix "${prefix}":`);
    console.log(JSON.stringify(enResult, null, 2));

    console.log(`\nChinese translations for prefix "${prefix}":`);
    console.log(JSON.stringify(cnResult, null, 2));
    break;
  }

  default:
    printUsage();
}

export { keyMappings, scanFileForKeys, migrateFile, extractModuleKeys };
