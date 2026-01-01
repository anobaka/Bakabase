#!/bin/bash
# Local changelog generation test script
# Usage: ./scripts/test-changelog.sh [previous_tag]
# Example: ./scripts/test-changelog.sh v2.1.13-beta

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_ROOT"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Changelog Generation Test ===${NC}"

# Check if git-cliff is installed
if ! command -v git-cliff &> /dev/null; then
    echo -e "${RED}Error: git-cliff is not installed${NC}"
    echo "Install it with: brew install git-cliff (macOS) or cargo install git-cliff"
    exit 1
fi

# Check if gh CLI is installed (for fetching issue labels)
if ! command -v gh &> /dev/null; then
    echo -e "${YELLOW}Warning: GitHub CLI (gh) is not installed. Will use mock data for issues.${NC}"
    USE_MOCK=true
else
    # Check if authenticated
    if ! gh auth status &> /dev/null 2>&1; then
        echo -e "${YELLOW}Warning: GitHub CLI is not authenticated. Will use mock data for issues.${NC}"
        echo "Run 'gh auth login' to authenticate for real issue data."
        USE_MOCK=true
    else
        USE_MOCK=false
    fi
fi

# Get previous tag
if [ -n "$1" ]; then
    PREVIOUS_TAG="$1"
else
    # Find the most recent tag
    PREVIOUS_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
    if [ -z "$PREVIOUS_TAG" ]; then
        echo -e "${RED}Error: No previous tag found. Please specify one.${NC}"
        exit 1
    fi
fi

echo -e "Previous tag: ${YELLOW}$PREVIOUS_TAG${NC}"
echo -e "Current HEAD: ${YELLOW}$(git rev-parse --short HEAD)${NC}"

# Step 1: Generate initial context with git-cliff
echo -e "\n${GREEN}Step 1: Generating initial context...${NC}"
git-cliff --config cliff.toml "$PREVIOUS_TAG"..HEAD --context > cliff.context.initial.json
echo "Generated: cliff.context.initial.json"

# Step 2: Enrich context with issue labels
echo -e "\n${GREEN}Step 2: Enriching context with issue labels...${NC}"

# Create Node.js script for enrichment
cat > /tmp/enrich-context.mjs << 'ENRICH_SCRIPT'
import fs from 'node:fs';
import { execSync } from 'node:child_process';

const USE_MOCK = process.env.USE_MOCK === 'true';
const text = fs.readFileSync('cliff.context.initial.json', { encoding: 'utf8' });
const initial = JSON.parse(text || '{}')[0];

const issues = {};

// Label display configuration
const labelConfig = [
  { name: 'breaking-changes', emoji: '🚨', title: 'Breaking Changes' },
  { name: 'feature', emoji: '🚀', title: 'Features' },
  { name: 'enhancement', emoji: '✨', title: 'Enhancements' },
  { name: 'architecture optimization', emoji: '🏗️', title: 'Architecture' },
  { name: 'bug', emoji: '🐛', title: 'Bug Fixes' },
  { name: 'experiment', emoji: '🧪', title: 'Experiments' },
];
const labelGroups = {};
for (const cfg of labelConfig) {
  labelGroups[cfg.name] = { emoji: cfg.emoji, title: cfg.title, issues: [] };
}

// Collect all issue numbers from commit links
const numberSet = new Set();
for (const commit of Array.isArray(initial.commits) ? initial.commits : []) {
  const links = Array.isArray(commit.links) ? commit.links : [];
  for (const link of links) {
    const m = /#(\d+)/.exec(link.text || '');
    if (m) {
      numberSet.add(m[1]);
    }
  }
}

console.log(`Found ${numberSet.size} issue references: ${[...numberSet].join(', ')}`);

// Fetch issue details
for (const num of numberSet) {
  try {
    let info;
    if (USE_MOCK) {
      // Mock data for testing
      const mockLabels = ['feature', 'bug']; // Simulate issue with multiple labels
      info = {
        title: `[Mock] Issue #${num} title`,
        url: `https://github.com/anobaka/Bakabase/issues/${num}`,
        number: parseInt(num),
        state: 'closed',
        labels: mockLabels
      };
      console.log(`  #${num}: (mock) labels=[${mockLabels.join(', ')}]`);
    } else {
      // Fetch from GitHub API using gh CLI
      const result = execSync(`gh issue view ${num} --json title,url,number,state,labels`, { encoding: 'utf8' });
      const data = JSON.parse(result);
      const labels = (data.labels || []).map(l => l.name);
      info = {
        title: data.title,
        url: data.url,
        number: data.number,
        state: data.state,
        labels: labels
      };
      console.log(`  #${num}: ${data.title.substring(0, 50)}... labels=[${labels.join(', ')}]`);
    }

    issues[num] = info;
    issues['#' + num] = info;

    // Group issue by each of its labels
    for (const label of info.labels) {
      if (labelGroups[label]) {
        if (!labelGroups[label].issues.find(i => i.number === info.number)) {
          labelGroups[label].issues.push(info);
        }
      }
    }
  } catch (e) {
    console.warn(`  Warning: Failed to fetch issue #${num}: ${e.message}`);
  }
}

// Attach issues to commits
for (const commit of Array.isArray(initial.commits) ? initial.commits : []) {
  const links = Array.isArray(commit.links) ? commit.links : [];
  const list = [];
  for (const link of links) {
    const m = /#(\d+)/.exec(link.text || '');
    if (m) {
      const n = m[1];
      const issue = issues[n] || issues['#' + n] || null;
      if (issue) list.push(issue);
    }
  }
  commit.extra ??= {};
  commit.extra["issues"] = list;
}

// Add labelGroups to context
initial.extra ??= {};
initial.extra.labelGroups = labelGroups;
initial.extra.labelOrder = labelConfig.map(c => c.name);
initial.version = process.env.VERSION || '0.0.0-test';
initial.timestamp = Math.floor(Date.now() / 1000);

const json = JSON.stringify([initial], null, 2);
fs.writeFileSync('cliff.context.enriched.json', json);
console.log('\nGenerated: cliff.context.enriched.json');

// Print summary
console.log('\nLabel groups summary:');
for (const [label, group] of Object.entries(labelGroups)) {
  if (group.issues.length > 0) {
    console.log(`  ${group.emoji} ${group.title}: ${group.issues.length} issue(s)`);
  }
}
ENRICH_SCRIPT

USE_MOCK=$USE_MOCK VERSION="test-$(date +%Y%m%d)" node /tmp/enrich-context.mjs

# Step 3: Generate final changelog
echo -e "\n${GREEN}Step 3: Generating final changelog...${NC}"
git-cliff --config cliff.toml --from-context cliff.context.enriched.json > CHANGELOG_TEST.md
echo "Generated: CHANGELOG_TEST.md"

# Show result
echo -e "\n${GREEN}=== Generated Changelog ===${NC}"
cat CHANGELOG_TEST.md

# Cleanup
echo -e "\n${GREEN}=== Cleanup ===${NC}"
echo "Test files created:"
echo "  - cliff.context.initial.json"
echo "  - cliff.context.enriched.json"
echo "  - CHANGELOG_TEST.md"
echo ""
echo "Run 'rm -f cliff.context.*.json CHANGELOG_TEST.md' to clean up"
